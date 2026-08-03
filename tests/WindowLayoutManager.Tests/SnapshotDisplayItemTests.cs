using System.Collections.Generic;
using System.ComponentModel;
using WindowLayoutManager.Models;
using Xunit;

namespace WindowLayoutManager.Tests
{
    public class SnapshotDisplayItemTests
    {
        private static (SnapshotDisplayItem Item, List<string> Raised) NewItemWatchingChanges(
            string name = "Original")
        {
            var item = new SnapshotDisplayItem(new LayoutSnapshot { Name = name });
            var raised = new List<string>();
            item.PropertyChanged += (s, e) => raised.Add(e.PropertyName);
            return (item, raised);
        }

        [Fact]
        public void Constructor_TakesTheNameFromTheSnapshot()
        {
            var snapshot = new LayoutSnapshot { Name = "Debugging" };

            var item = new SnapshotDisplayItem(snapshot);

            Assert.Equal("Debugging", item.Name);
            Assert.Same(snapshot, item.Snapshot);
        }

        [Fact]
        public void Name_WhenChanged_RaisesPropertyChanged()
        {
            (SnapshotDisplayItem item, List<string> raised) = NewItemWatchingChanges();

            item.Name = "Renamed";

            Assert.Equal("Renamed", item.Name);
            Assert.Equal(new[] { nameof(SnapshotDisplayItem.Name) }, raised.ToArray());
        }

        [Fact]
        public void Name_WhenSetToTheSameValue_DoesNotRaisePropertyChanged()
        {
            (SnapshotDisplayItem item, List<string> raised) = NewItemWatchingChanges("Same");

            item.Name = "Same";

            Assert.Empty(raised);
        }

        [Fact]
        public void IsEditing_WhenToggled_RaisesPropertyChanged()
        {
            (SnapshotDisplayItem item, List<string> raised) = NewItemWatchingChanges();
            Assert.False(item.IsEditing);

            item.IsEditing = true;

            Assert.True(item.IsEditing);
            Assert.Equal(new[] { nameof(SnapshotDisplayItem.IsEditing) }, raised.ToArray());
        }

        [Fact]
        public void IsEditing_WhenSetToTheSameValue_DoesNotRaisePropertyChanged()
        {
            (SnapshotDisplayItem item, List<string> raised) = NewItemWatchingChanges();

            item.IsEditing = false;

            Assert.Empty(raised);
        }

        [Fact]
        public void Name_DoesNotWriteThroughToTheUnderlyingSnapshot()
        {
            // The display name is edited inline before the rename is committed; the snapshot itself
            // must not change until the coordinator saves it.
            var snapshot = new LayoutSnapshot { Name = "Original" };
            var item = new SnapshotDisplayItem(snapshot);

            item.Name = "Typed but not committed";

            Assert.Equal("Original", snapshot.Name);
        }

        [Fact]
        public void ImplementsINotifyPropertyChanged()
        {
            Assert.IsAssignableFrom<INotifyPropertyChanged>(
                new SnapshotDisplayItem(new LayoutSnapshot()));
        }
    }
}
