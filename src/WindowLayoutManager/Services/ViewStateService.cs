using System.Collections.Generic;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using WindowLayoutManager.Models;
using IServiceProvider = System.IServiceProvider;

namespace WindowLayoutManager.Services
{
    /// <summary>
    /// Captures scroll/caret/selection for every open text document, and restores a single document's
    /// view state once its editor view has completed a layout pass. All positions are clamped on
    /// restore so edits made since capture cannot throw.
    /// </summary>
    internal sealed class ViewStateService
    {
        private IVsEditorAdaptersFactoryService _adapters;

        private IVsEditorAdaptersFactoryService GetAdapters(IServiceProvider serviceProvider)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_adapters != null) return _adapters;
            var componentModel = serviceProvider.GetService(typeof(SComponentModel)) as IComponentModel;
            _adapters = componentModel?.GetService<IVsEditorAdaptersFactoryService>();
            return _adapters;
        }

        /// <summary>Resolves the code window for an open document frame; null for non-text documents.</summary>
        public static IVsCodeWindow GetCodeWindow(IVsWindowFrame frame, out string moniker)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            moniker = null;

            if (ErrorHandler.Succeeded(frame.GetProperty((int)__VSFPROPID.VSFPROPID_pszMkDocument, out object monikerObj)))
                moniker = monikerObj as string;
            if (string.IsNullOrEmpty(moniker)) return null;

            if (ErrorHandler.Failed(frame.GetProperty((int)__VSFPROPID.VSFPROPID_DocView, out object docView)))
                return null;
            return docView as IVsCodeWindow;   // null for designer / non-text doc
        }

        /// <summary>True when the code window currently has a secondary (Window.Split) view.</summary>
        public static bool IsSplit(IVsCodeWindow codeWindow)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return codeWindow != null
                && ErrorHandler.Succeeded(codeWindow.GetSecondaryView(out IVsTextView secondary))
                && secondary != null;
        }

        /// <summary>Resolves the WPF text view (primary pane) for an open document frame; null for non-text documents.</summary>
        public IWpfTextView GetWpfTextView(IServiceProvider serviceProvider, IVsWindowFrame frame, out string moniker)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            IVsCodeWindow codeWindow = GetCodeWindow(frame, out moniker);
            if (codeWindow == null) return null;

            if (ErrorHandler.Failed(codeWindow.GetPrimaryView(out IVsTextView textViewAdapter)) || textViewAdapter == null)
                return null;

            return GetAdapters(serviceProvider)?.GetWpfTextView(textViewAdapter);
        }

        /// <summary>Captures view state for all open text documents.</summary>
        public List<ViewStateEntry> CaptureAll(IServiceProvider serviceProvider)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var result = new List<ViewStateEntry>();

            if (!(serviceProvider.GetService(typeof(SVsUIShell)) is IVsUIShell uiShell)) return result;
            if (ErrorHandler.Failed(uiShell.GetDocumentWindowEnum(out IEnumWindowFrames frames)) || frames == null)
                return result;

            var batch = new IVsWindowFrame[1];
            while (frames.Next(1, batch, out uint fetched) == VSConstants.S_OK && fetched == 1)
            {
                IVsCodeWindow codeWindow = GetCodeWindow(batch[0], out _);
                IWpfTextView view = GetWpfTextView(serviceProvider, batch[0], out string moniker);
                if (view?.TextViewLines == null || !view.TextViewLines.IsValid) continue;

                ViewStateEntry entry = CaptureView(moniker, view);
                entry.IsSplit = IsSplit(codeWindow);
                result.Add(entry);
            }
            return result;
        }

        private static ViewStateEntry CaptureView(string moniker, IWpfTextView view)
        {
            SnapshotPoint caret = view.Caret.Position.BufferPosition;
            ITextSnapshotLine caretLine = caret.GetContainingLine();
            int scrollLine = view.TextViewLines.FirstVisibleLine.Start.GetContainingLine().LineNumber;

            var entry = new ViewStateEntry
            {
                Moniker = moniker,
                ScrollLine = scrollLine,
                CaretLine = caretLine.LineNumber,
                CaretColumn = caret.Position - caretLine.Start.Position,
            };

            if (!view.Selection.IsEmpty)
            {
                SnapshotPoint start = view.Selection.Start.Position;
                SnapshotPoint end = view.Selection.End.Position;
                ITextSnapshotLine startLine = start.GetContainingLine();
                ITextSnapshotLine endLine = end.GetContainingLine();
                entry.HasSelection = true;
                entry.SelectionStartLine = startLine.LineNumber;
                entry.SelectionStartColumn = start.Position - startLine.Start.Position;
                entry.SelectionEndLine = endLine.LineNumber;
                entry.SelectionEndColumn = end.Position - endLine.Start.Position;
            }
            return entry;
        }

        /// <summary>Applies a captured view state to a realized view, clamping all positions to the current text.</summary>
        public void RestoreOne(IWpfTextView view, ViewStateEntry entry)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            ITextSnapshot snapshot = view.TextSnapshot;

            view.Caret.MoveTo(PointAt(snapshot, entry.CaretLine, entry.CaretColumn));

            if (entry.HasSelection)
            {
                SnapshotPoint start = PointAt(snapshot, entry.SelectionStartLine, entry.SelectionStartColumn);
                SnapshotPoint end = PointAt(snapshot, entry.SelectionEndLine, entry.SelectionEndColumn);
                if (end.Position < start.Position) { SnapshotPoint t = start; start = end; end = t; }
                view.Selection.Select(new SnapshotSpan(start, end), false);
            }
            else
            {
                view.Selection.Clear();
            }

            // Scroll last so the saved top line wins over any caret-induced scroll.
            ITextSnapshotLine scrollLine = snapshot.GetLineFromLineNumber(Clamp(entry.ScrollLine, 0, snapshot.LineCount - 1));
            view.DisplayTextLineContainingBufferPosition(scrollLine.Start, 0.0, ViewRelativePosition.Top);
        }

        private static SnapshotPoint PointAt(ITextSnapshot snapshot, int line, int column)
        {
            ITextSnapshotLine snapLine = snapshot.GetLineFromLineNumber(Clamp(line, 0, snapshot.LineCount - 1));
            return new SnapshotPoint(snapshot, snapLine.Start.Position + Clamp(column, 0, snapLine.Length));
        }

        private static int Clamp(int value, int min, int max) =>
            value < min ? min : (value > max ? max : value);
    }
}
