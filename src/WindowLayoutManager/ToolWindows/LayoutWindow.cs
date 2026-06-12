using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;
using WindowLayoutManager.Commands;

namespace WindowLayoutManager.ToolWindows
{
    [Guid("A21C276D-4406-434E-90C9-7C98685C5D17")]
    public sealed class LayoutWindow : ToolWindowPane
    {
        private readonly LayoutWindowControl _control;
        private OleMenuCommandService _commandService;

        public LayoutWindow() : base(null)
        {
            Caption = "Window Layouts";
            BitmapImageMoniker = KnownMonikers.LayoutPanel;
            _control = new LayoutWindowControl();
            Content = _control;
        }

        protected override void Initialize()
        {
            base.Initialize();

            // Register the layout commands on the pane's own command service (not the package's)
            // so they only participate in command routing while this window has focus. The .vsct
            // ships no keybindings; a user-assigned binding pressed elsewhere simply finds no
            // handler, which is the intent.
            _commandService = GetService(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (_commandService == null) return;

            AddLayoutCommand(LayoutCommandIds.ApplyLayout, () => _control.ApplySelected());
            AddLayoutCommand(LayoutCommandIds.RenameLayout, () => _control.RenameSelected());
            AddLayoutCommand(LayoutCommandIds.DeleteLayout, () => _control.DeleteSelected());

            _control.ShowLayoutContextMenu = ShowLayoutContextMenu;
        }

        private void AddLayoutCommand(int commandId, Action execute)
        {
            var command = new OleMenuCommand(
                (s, e) => execute(),
                new CommandID(LayoutCommandIds.CommandSet, commandId));
            command.BeforeQueryStatus += (s, e) =>
            {
                var cmd = (OleMenuCommand)s;
                cmd.Enabled = _control.HasSelection;
            };
            _commandService.AddCommand(command);
        }

        /// <summary>Shows the VSCT-defined layout context menu at the given screen point (device pixels).</summary>
        private void ShowLayoutContextMenu(System.Windows.Point screenPoint)
        {
            _commandService?.ShowContextMenu(
                new CommandID(LayoutCommandIds.CommandSet, LayoutCommandIds.LayoutContextMenu),
                (int)screenPoint.X, (int)screenPoint.Y);
        }
    }
}
