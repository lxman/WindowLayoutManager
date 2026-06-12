using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Threading;
using WindowLayoutManager.Models;
using IServiceProvider = System.IServiceProvider;
using Task = System.Threading.Tasks.Task;

namespace WindowLayoutManager.Services
{
    /// <summary>
    /// Orchestrates capturing a snapshot and applying one: handles UI-thread vs. background-thread
    /// switching, the apply ordering (dirty-guard close, breakpoints, doc-well, then deferred view-state), the version
    /// gate on the opaque doc-well blob, and the unsaved-changes guard before closing documents.
    /// </summary>
    internal sealed class LayoutApplyCoordinator
    {
        // Win32 message-box results returned by VsShellUtilities.ShowMessageBox.
        private const int IdCancel = 2, IdYes = 6, IdNo = 7;

        private readonly AsyncPackage _package;
        private readonly IServiceProvider _sp;
        private readonly SolutionContext _solutionContext;
        private readonly SnapshotStore _store;

        private readonly DocumentWellService _documentWell = new DocumentWellService();
        private readonly ViewStateService _viewState = new ViewStateService();
        private readonly BreakpointService _breakpoints = new BreakpointService();

        public LayoutApplyCoordinator(AsyncPackage package, SolutionContext solutionContext, SnapshotStore store)
        {
            _package = package;
            _sp = package;
            _solutionContext = solutionContext;
            _store = store;
        }

        /// <summary>Raised (on the UI thread) after the snapshot list changes, so the tool window can reload.</summary>
        public event EventHandler SnapshotsChanged;

        /// <summary>Raised (on the UI thread) with a short status message for the tool window.</summary>
        public event EventHandler<string> Status;

        // ---- Save ----------------------------------------------------------------------------

        public async Task SaveSnapshotAsync(string name, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync(ct);

            SolutionInfo solution = _solutionContext.CurrentSolution;
            if (solution == null) return;

            DTE dte = GetDte();
            var snapshot = new LayoutSnapshot
            {
                Id = Guid.NewGuid().ToString("D"),
                Name = name.Trim(),
                Created = DateTime.UtcNow.ToString("o"),
                SchemaVersion = SnapshotStore.SchemaVersion,
                VsVersion = GetVsMajorMinor(dte),
                DocWellBlob = _documentWell.CaptureBlob(_sp),
                ViewStates = _viewState.CaptureAll(_sp),
                Breakpoints = _breakpoints.CaptureAll(dte),
            };

            await TaskScheduler.Default;   // persist off the UI thread
            SnapshotLoadResult loaded = await _store.LoadAsync(solution, ct);
            if (!loaded.IsAuthoritative)
            {
                // The existing file couldn't be read; writing now would wipe layouts we never saw.
                await _package.JoinableTaskFactory.SwitchToMainThreadAsync(ct);
                Status?.Invoke(this, "Layout not saved. " + loaded.Warning);
                return;
            }

            // Saving under an existing name replaces that layout (after confirmation) rather than
            // accumulating identically named entries. The replacement keeps the old Id and list
            // position, so it stays where the user expects it.
            int existingIndex = loaded.Snapshots.FindIndex(
                s => string.Equals(s.Name, snapshot.Name, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
            {
                await _package.JoinableTaskFactory.SwitchToMainThreadAsync(ct);
                int result = VsShellUtilities.ShowMessageBox(
                    _sp,
                    $"A layout named \"{loaded.Snapshots[existingIndex].Name}\" already exists.\n\nReplace it with the current state?",
                    "Window Layout Manager",
                    OLEMSGICON.OLEMSGICON_QUERY,
                    OLEMSGBUTTON.OLEMSGBUTTON_YESNO,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                if (result != IdYes)
                {
                    Status?.Invoke(this, "Layout not saved — name already in use.");
                    return;
                }
                snapshot.Id = loaded.Snapshots[existingIndex].Id;
                snapshot.Name = loaded.Snapshots[existingIndex].Name;   // keep original casing
                await TaskScheduler.Default;
                loaded.Snapshots[existingIndex] = snapshot;
            }
            else
            {
                loaded.Snapshots.Add(snapshot);
            }
            await _store.SaveAsync(solution, loaded.Snapshots, ct);

            await _package.JoinableTaskFactory.SwitchToMainThreadAsync(ct);
            SnapshotsChanged?.Invoke(this, EventArgs.Empty);
        }

        // ---- Apply ---------------------------------------------------------------------------

        public async Task ApplySnapshotAsync(LayoutSnapshot snapshot, CancellationToken ct)
        {
            if (snapshot == null) return;
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync(ct);

            DTE dte = GetDte();

            // Applying mid-debug would close documents and rip out the live breakpoint set under
            // the running process — technically possible, never what the user meant.
            if (IsDebugging(dte))
            {
                Status?.Invoke(this, "Stop debugging before applying a layout.");
                return;
            }

            bool restoreDocWell = IsBlobCompatible(snapshot, dte);

            // 1. Close the current documents first. This step carries the only cancellable prompt,
            //    so it must run before anything destructive — a cancelled apply leaves the
            //    breakpoint set and everything else untouched.
            if (restoreDocWell && !TryCloseOpenDocuments())
            {
                Status?.Invoke(this, "Apply cancelled — unsaved changes.");
                return;
            }

            // 2. Breakpoints — independent of the editor views.
            _breakpoints.RestoreAll(dte, snapshot.Breakpoints);

            // 3. Document well, gated on VS-version compatibility of the opaque blob.
            if (restoreDocWell)
            {
                _documentWell.RestoreBlob(_sp, snapshot.DocWellBlob);
            }
            else
            {
                Status?.Invoke(this,
                    $"Document layout skipped — saved on VS {snapshot.VsVersion}, current is {GetVsMajorMinor(dte)}. " +
                    "View state and breakpoints still applied.");
            }

            // 4. Restore view state as each editor view becomes ready.
            RestoreViewStates(dte, snapshot.ViewStates);
        }

        // ---- View-state restore (deferred to first layout) -----------------------------------

        private void RestoreViewStates(DTE dte, List<ViewStateEntry> viewStates)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (viewStates == null || viewStates.Count == 0) return;
            if (!(_sp.GetService(typeof(SVsUIShell)) is IVsUIShell uiShell)) return;

            // Map the currently-open document frames by moniker.
            var byMoniker = new Dictionary<string, IVsWindowFrame>(StringComparer.OrdinalIgnoreCase);
            foreach (IVsWindowFrame frame in CollectDocumentFrames(uiShell))
            {
                ViewStateService.GetCodeWindow(frame, out string moniker);
                if (!string.IsNullOrEmpty(moniker) && !byMoniker.ContainsKey(moniker))
                    byMoniker[moniker] = frame;
            }

            // Re-split at dispatcher idle, not inline: Window.Split runs through the WPF focus
            // engine, and firing it while the just-reopened frames are still settling throws an
            // async ArgumentNullException ("element") from a posted focus operation that no
            // try/catch here can reach. By idle, activation and focus are stable.
            _package.JoinableTaskFactory.RunAsync(async () =>
            {
                await System.Windows.Threading.Dispatcher.Yield(
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                try { RestoreSplits(dte, viewStates, byMoniker); }
                catch { /* best effort — leave windows unsplit */ }
            }).Task.FileAndForget("WindowLayoutManager/RestoreSplits");

            foreach (ViewStateEntry entry in viewStates)
            {
                if (string.IsNullOrEmpty(entry?.Moniker)) continue;
                if (!byMoniker.TryGetValue(entry.Moniker, out IVsWindowFrame frame)) continue;
                IWpfTextView view = _viewState.GetWpfTextView(_sp, frame, out _);
                if (view != null)
                    ApplyWhenReady(view, entry);
            }
        }

        private static void RestoreSplits(DTE dte, List<ViewStateEntry> viewStates,
            Dictionary<string, IVsWindowFrame> byMoniker)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            EnvDTE.Window previouslyActive = null;
            try { previouslyActive = dte?.ActiveWindow; } catch { /* no active window */ }

            bool anySplit = false;
            foreach (ViewStateEntry entry in viewStates)
            {
                if (entry == null || !entry.IsSplit || string.IsNullOrEmpty(entry.Moniker)) continue;
                if (!byMoniker.TryGetValue(entry.Moniker, out IVsWindowFrame frame)) continue;

                // Window.Split is a toggle — never fire it on a window that is somehow already split.
                IVsCodeWindow codeWindow = ViewStateService.GetCodeWindow(frame, out _);
                if (codeWindow == null || ViewStateService.IsSplit(codeWindow)) continue;

                try
                {
                    frame.Show();   // activate so the command targets this window
                    // Put keyboard focus on the editor itself before the command runs; the split
                    // command's focus handoff needs a focused element to start from.
                    if (ErrorHandler.Succeeded(codeWindow.GetPrimaryView(out IVsTextView primary)) && primary != null)
                        primary.SendExplicitFocus();
                    dte.ExecuteCommand("Window.Split");
                    anySplit = true;
                }
                catch { /* command unavailable for this editor — leave unsplit */ }
            }

            if (anySplit)
            {
                try { previouslyActive?.Activate(); } catch { /* window gone */ }
            }
        }

        private void ApplyWhenReady(IWpfTextView view, ViewStateEntry entry)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (view.TextViewLines != null && view.TextViewLines.IsValid)
            {
                SafeRestore(view, entry);
                return;
            }

            // The view hasn't laid out yet (e.g. a background tab). Restore on its first layout pass.
            EventHandler<TextViewLayoutChangedEventArgs> handler = null;
            handler = (s, e) =>
            {
                view.LayoutChanged -= handler;   // one-shot
                SafeRestore(view, entry);
            };
            view.LayoutChanged += handler;
        }

        private void SafeRestore(IWpfTextView view, ViewStateEntry entry)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try { _viewState.RestoreOne(view, entry); } catch { /* drifted/closed view */ }
        }

        // ---- Document closing + dirty guard --------------------------------------------------

        /// <summary>
        /// Closes all open document frames so a reopened layout replaces (not appends to) the current
        /// one. If any document is dirty, asks once whether to save. Returns false when the user
        /// cancels — nothing has been closed or modified at that point.
        /// </summary>
        private bool TryCloseOpenDocuments()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!(_sp.GetService(typeof(SVsUIShell)) is IVsUIShell uiShell)) return true;

            List<IVsWindowFrame> frames = CollectDocumentFrames(uiShell);

            bool anyDirty = false;
            foreach (IVsWindowFrame f in frames)
            {
                if (IsFrameDirty(f)) { anyDirty = true; break; }
            }

            uint closeFlag = (uint)__FRAMECLOSE.FRAMECLOSE_NoSave;
            if (anyDirty)
            {
                int result = VsShellUtilities.ShowMessageBox(
                    _sp,
                    "Some open documents have unsaved changes.\n\nSave them before applying this layout?",
                    "Window Layout Manager",
                    OLEMSGICON.OLEMSGICON_QUERY,
                    OLEMSGBUTTON.OLEMSGBUTTON_YESNOCANCEL,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);

                if (result == IdCancel) return false;
                closeFlag = result == IdYes
                    ? (uint)__FRAMECLOSE.FRAMECLOSE_SaveIfDirty
                    : (uint)__FRAMECLOSE.FRAMECLOSE_NoSave;
            }

            foreach (IVsWindowFrame f in frames)
            {
                try { f.CloseFrame(closeFlag); } catch { /* best effort */ }
            }
            return true;
        }

        // ---- Helpers -------------------------------------------------------------------------

        private static bool IsFrameDirty(IVsWindowFrame frame)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (ErrorHandler.Failed(frame.GetProperty((int)__VSFPROPID.VSFPROPID_DocData, out object docData)))
                return false;
            return docData is IVsPersistDocData persist
                && ErrorHandler.Succeeded(persist.IsDocDataDirty(out int dirty))
                && dirty != 0;
        }

        private static List<IVsWindowFrame> CollectDocumentFrames(IVsUIShell uiShell)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var list = new List<IVsWindowFrame>();
            if (ErrorHandler.Failed(uiShell.GetDocumentWindowEnum(out IEnumWindowFrames frames)) || frames == null)
                return list;
            var batch = new IVsWindowFrame[1];
            while (frames.Next(1, batch, out uint fetched) == VSConstants.S_OK && fetched == 1)
                list.Add(batch[0]);
            return list;
        }

        private static bool IsDebugging(DTE dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try { return dte?.Debugger?.CurrentMode != dbgDebugMode.dbgDesignMode; }
            catch { return false; }
        }

        private static DTE GetDte()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return Package.GetGlobalService(typeof(DTE)) as DTE;
        }

        private static string GetVsMajorMinor(DTE dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try { return dte?.Version ?? ""; } catch { return ""; }
        }

        private static bool IsBlobCompatible(LayoutSnapshot snapshot, DTE dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (string.IsNullOrEmpty(snapshot.DocWellBlob)) return false;
            if (string.IsNullOrEmpty(snapshot.VsVersion)) return true;   // unknown writer → attempt
            return MajorOf(snapshot.VsVersion) == MajorOf(GetVsMajorMinor(dte));
        }

        private static string MajorOf(string version) =>
            string.IsNullOrEmpty(version) ? "" : version.Split('.')[0];
    }
}
