using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using WindowLayoutManager.Models;

namespace WindowLayoutManager.ToolWindows
{
    public partial class LayoutWindowControl : UserControl
    {
        private readonly ObservableCollection<SnapshotDisplayItem> _items = new ObservableCollection<SnapshotDisplayItem>();
        private Services.SolutionInfo _currentSolution;
        private bool _subscribed;

        public LayoutWindowControl()
        {
            InitializeComponent();
            SnapshotList.ItemsSource = _items;

            // WPF raises Unloaded whenever the content leaves the visual tree — float, re-dock, or
            // tab move, not just close — so constructor-time subscriptions would be torn down for
            // good on the first re-dock. Pair them with Loaded/Unloaded instead, and re-seed on
            // each Loaded since events may have been missed while detached.
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            WindowLayoutManagerPackage pkg = WindowLayoutManagerPackage.Instance;
            if (pkg == null) return;

            if (!_subscribed)
            {
                pkg.SolutionContext.SolutionOpened += OnSolutionOpened;
                pkg.SolutionContext.SolutionClosed += OnSolutionClosed;
                pkg.ApplyCoordinator.SnapshotsChanged += OnSnapshotsChanged;
                pkg.ApplyCoordinator.Status += OnStatus;
                _subscribed = true;
            }

            _currentSolution = pkg.SolutionContext.CurrentSolution;
            if (_currentSolution != null)
            {
                ReloadAsync().Forget();
            }
            else
            {
                _items.Clear();
                StatusText.Text = "No solution open.";
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (!_subscribed) return;
            WindowLayoutManagerPackage pkg = WindowLayoutManagerPackage.Instance;
            if (pkg == null) return;
            pkg.SolutionContext.SolutionOpened -= OnSolutionOpened;
            pkg.SolutionContext.SolutionClosed -= OnSolutionClosed;
            pkg.ApplyCoordinator.SnapshotsChanged -= OnSnapshotsChanged;
            pkg.ApplyCoordinator.Status -= OnStatus;
            _subscribed = false;
        }

        private void OnSolutionOpened(object sender, Services.SolutionInfo solution)
        {
            _currentSolution = solution;
            ReloadAsync().Forget();
        }

        private void OnSolutionClosed(object sender, EventArgs e)
        {
            _currentSolution = null;
            _items.Clear();
            StatusText.Text = "No solution open.";
        }

        private void OnSnapshotsChanged(object sender, EventArgs e) => ReloadAsync().Forget();

        private void OnStatus(object sender, string message) => StatusText.Text = message;

        private async Task ReloadAsync()
        {
            Services.SolutionInfo solution = _currentSolution;
            WindowLayoutManagerPackage pkg = WindowLayoutManagerPackage.Instance;
            if (pkg == null || solution == null) return;

            var loaded = await pkg.SnapshotStore.LoadAsync(solution, CancellationToken.None);
            await pkg.JoinableTaskFactory.SwitchToMainThreadAsync();
            _items.Clear();
            foreach (LayoutSnapshot s in loaded.Snapshots)
                _items.Add(new SnapshotDisplayItem(s));
            StatusText.Text = loaded.Warning
                ?? (_items.Count == 0 ? "No saved layouts." : $"{_items.Count} layout(s).");
        }

        // ---- Save ----

        private void NameTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            SaveCurrent();
            e.Handled = true;
        }

        private void SaveCurrent()
        {
            string name = NameTextBox.Text?.Trim();
            if (string.IsNullOrEmpty(name)) { StatusText.Text = "Enter a name first."; return; }

            WindowLayoutManagerPackage pkg = WindowLayoutManagerPackage.Instance;
            if (pkg == null) return;
            NameTextBox.Text = string.Empty;
            pkg.JoinableTaskFactory
               .RunAsync(() => pkg.ApplyCoordinator.SaveSnapshotAsync(name, CancellationToken.None))
               .Task.FileAndForget("WindowLayoutManager/Save");
        }

        // ---- Context menu ----

        /// <summary>Set by the owning <see cref="LayoutWindow"/> pane; shows the VSCT context menu
        /// at a screen point (device pixels).</summary>
        internal Action<Point> ShowLayoutContextMenu { get; set; }

        internal bool HasSelection => SnapshotList.SelectedItem is SnapshotDisplayItem;

        // WPF ListBox does not move selection on right-click; do it ourselves so the context
        // menu always targets the row under the cursor.
        private void SnapshotList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            DependencyObject source = e.OriginalSource as DependencyObject;
            while (source != null && !(source is ListBoxItem))
                source = System.Windows.Media.VisualTreeHelper.GetParent(source);
            if (source is ListBoxItem item)
                item.IsSelected = true;
        }

        private void SnapshotList_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!HasSelection) return;
            ShowLayoutContextMenu?.Invoke(SnapshotList.PointToScreen(e.GetPosition(SnapshotList)));
            e.Handled = true;
        }

        // ---- Apply ----

        private void SnapshotList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Double-clicking inside the inline rename TextBox is word selection, not apply.
            DependencyObject source = e.OriginalSource as DependencyObject;
            while (source != null && !(source is TextBox) && !(source is ListBox))
                source = System.Windows.Media.VisualTreeHelper.GetParent(source);
            if (source is TextBox) return;

            ApplySelected();
        }

        private void SnapshotList_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            ApplySelected();
            e.Handled = true;
        }

        internal void ApplySelected()
        {
            if (!(SnapshotList.SelectedItem is SnapshotDisplayItem item)) return;
            WindowLayoutManagerPackage pkg = WindowLayoutManagerPackage.Instance;
            if (pkg == null) return;
            pkg.JoinableTaskFactory
               .RunAsync(() => pkg.ApplyCoordinator.ApplySnapshotAsync(item.Snapshot, CancellationToken.None))
               .Task.FileAndForget("WindowLayoutManager/Apply");
        }

        // ---- Delete / Rename ----

        internal void DeleteSelected()
        {
            if (SnapshotList.SelectedItem is SnapshotDisplayItem item)
                DeleteAsync(item.Snapshot.Id).Forget();
        }

        private async Task DeleteAsync(string id)
        {
            Services.SolutionInfo solution = _currentSolution;
            WindowLayoutManagerPackage pkg = WindowLayoutManagerPackage.Instance;
            if (pkg == null || solution == null) return;

            var loaded = await pkg.SnapshotStore.LoadAsync(solution, CancellationToken.None);
            if (!loaded.IsAuthoritative)
            {
                await pkg.JoinableTaskFactory.SwitchToMainThreadAsync();
                StatusText.Text = "Delete failed. " + loaded.Warning;
                return;
            }
            loaded.Snapshots.RemoveAll(s => s.Id == id);
            await pkg.SnapshotStore.SaveAsync(solution, loaded.Snapshots, CancellationToken.None);
            await ReloadAsync();
        }

        internal void RenameSelected()
        {
            if (SnapshotList.SelectedItem is SnapshotDisplayItem item)
                item.IsEditing = true;
        }

        private void EditBox_Loaded(object sender, RoutedEventArgs e)
        {
            var box = (TextBox)sender;
            if (!(box.DataContext is SnapshotDisplayItem item) || !item.IsEditing) return;
            box.Focus();
            box.SelectAll();
        }

        private void EditBox_KeyDown(object sender, KeyEventArgs e)
        {
            var box = (TextBox)sender;
            if (e.Key == Key.Enter)
            {
                CommitRename(box);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CancelRename(box);
                e.Handled = true;
            }
        }

        // Clicking away (or the context menu opening) ends the edit like Solution Explorer does:
        // commit. CommitRename is a no-op if Enter/Esc already ended it.
        private void EditBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
            => CommitRename((TextBox)sender);

        private void CommitRename(TextBox box)
        {
            if (!(box.DataContext is SnapshotDisplayItem item) || !item.IsEditing) return;
            item.IsEditing = false;
            SnapshotList.Focus();

            string updated = box.Text?.Trim();
            if (string.IsNullOrEmpty(updated) || updated == item.Snapshot.Name) return;
            RenameAsync(item.Snapshot.Id, updated).Forget();
        }

        private void CancelRename(TextBox box)
        {
            if (!(box.DataContext is SnapshotDisplayItem item) || !item.IsEditing) return;
            item.IsEditing = false;
            box.Text = item.Name;
            SnapshotList.Focus();
        }

        private async Task RenameAsync(string id, string newName)
        {
            Services.SolutionInfo solution = _currentSolution;
            WindowLayoutManagerPackage pkg = WindowLayoutManagerPackage.Instance;
            if (pkg == null || solution == null) return;

            var loaded = await pkg.SnapshotStore.LoadAsync(solution, CancellationToken.None);
            if (!loaded.IsAuthoritative)
            {
                await pkg.JoinableTaskFactory.SwitchToMainThreadAsync();
                StatusText.Text = "Rename failed. " + loaded.Warning;
                return;
            }
            LayoutSnapshot match = loaded.Snapshots.Find(s => s.Id == id);
            if (match != null) match.Name = newName;
            await pkg.SnapshotStore.SaveAsync(solution, loaded.Snapshots, CancellationToken.None);
            await ReloadAsync();
        }
    }
}
