using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio.Shell;
using WindowLayoutManager.Commands;
using WindowLayoutManager.Services;
using WindowLayoutManager.ToolWindows;
using Task = System.Threading.Tasks.Task;

namespace WindowLayoutManager
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("Window Layout Manager",
        "Save and restore named, per-solution working-context snapshots: open documents, view state, and breakpoints.",
        "1.0.0")]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [Guid(PackageGuids.PackageString)]
    [ProvideToolWindow(typeof(LayoutWindow), Style = VsDockStyle.Tabbed, Window = "DocumentWell")]
    // Load in the background once a solution is open (UICONTEXT.SolutionExists) so the tool window can
    // show the active solution's snapshots without the user opening it first.
    [ProvideAutoLoad("f1536ef8-92ec-443c-9ed7-fdadf150da82", PackageAutoLoadFlags.BackgroundLoad)]
    public sealed class WindowLayoutManagerPackage : AsyncPackage
    {
        public static WindowLayoutManagerPackage Instance { get; private set; }

        internal SolutionContext SolutionContext { get; private set; }
        internal SnapshotStore SnapshotStore { get; private set; }
        internal LayoutApplyCoordinator ApplyCoordinator { get; private set; }

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            Instance = this;

            SolutionContext = new SolutionContext(this);
            SnapshotStore = new SnapshotStore();
            ApplyCoordinator = new LayoutApplyCoordinator(this, SolutionContext, SnapshotStore);

            SolutionContext.Initialize();

            await OpenLayoutWindowCommand.InitializeAsync(this);
        }

        protected override void Dispose(bool disposing)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (disposing)
                SolutionContext?.Dispose();
            base.Dispose(disposing);
        }
    }

    public static class PackageGuids
    {
        public const string PackageString = "8EC62364-3FD8-410B-9216-12CB49877E33";
        public static readonly Guid Package = new Guid(PackageString);
    }
}
