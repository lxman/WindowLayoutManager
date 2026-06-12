using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using WindowLayoutManager.ToolWindows;

namespace WindowLayoutManager.Commands
{
    /// <summary>View ▸ Other Windows ▸ Window Layouts — opens (and creates) the tool window.</summary>
    internal sealed class OpenLayoutWindowCommand
    {
        public const int CommandId = 0x0100;
        public static readonly Guid CommandSet = LayoutCommandIds.CommandSet;
        private readonly AsyncPackage _package;

        private OpenLayoutWindowCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            var menuCommandId = new CommandID(CommandSet, CommandId);
            commandService.AddCommand(new MenuCommand(Execute, menuCommandId));
        }

        public static OpenLayoutWindowCommand Instance { get; private set; }

        public static async System.Threading.Tasks.Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            Instance = new OpenLayoutWindowCommand(package, commandService);
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            ToolWindowPane window = _package.FindToolWindow(typeof(LayoutWindow), 0, true);
            if (window?.Frame == null)
                throw new NotSupportedException("Cannot create tool window");

            var frame = (IVsWindowFrame)window.Frame;
            Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(frame.Show());
        }
    }
}
