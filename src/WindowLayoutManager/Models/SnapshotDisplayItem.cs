using System.ComponentModel;

namespace WindowLayoutManager.Models
{
    /// <summary>Bindable wrapper around a <see cref="LayoutSnapshot"/> for the tool-window list.</summary>
    public sealed class SnapshotDisplayItem : INotifyPropertyChanged
    {
        private string _name;

        public SnapshotDisplayItem(LayoutSnapshot snapshot)
        {
            Snapshot = snapshot;
            _name = snapshot.Name;
        }

        public LayoutSnapshot Snapshot { get; }

        public string Name
        {
            get => _name;
            set
            {
                if (_name == value) return;
                _name = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }

        private bool _isEditing;

        /// <summary>True while the row shows the inline rename TextBox instead of the name label.</summary>
        public bool IsEditing
        {
            get => _isEditing;
            set
            {
                if (_isEditing == value) return;
                _isEditing = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEditing)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
