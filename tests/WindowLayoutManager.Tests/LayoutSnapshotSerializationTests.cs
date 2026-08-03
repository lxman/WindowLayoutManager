using Newtonsoft.Json;
using WindowLayoutManager.Models;
using Xunit;

namespace WindowLayoutManager.Tests
{
    /// <summary>
    /// The on-disk JSON is a compatibility surface: files written by one build are read by the next,
    /// and by other VS instances. These pin the shape rather than the storage plumbing.
    /// </summary>
    public class LayoutSnapshotSerializationTests
    {
        [Fact]
        public void NewSnapshot_HasEmptyCollectionsNotNulls()
        {
            var snapshot = new LayoutSnapshot();

            Assert.NotNull(snapshot.ViewStates);
            Assert.NotNull(snapshot.Breakpoints);
            Assert.Empty(snapshot.ViewStates);
            Assert.Empty(snapshot.Breakpoints);
        }

        [Fact]
        public void DeserializingAnEntryWithoutCollections_LeavesThemEmptyNotNull()
        {
            // A snapshot written before a collection existed must not deserialize into a null that
            // the capture path would then dereference.
            const string json = @"[{ ""Id"": ""abc"", ""Name"": ""Old"", ""SchemaVersion"": 1 }]";

            var list = JsonConvert.DeserializeObject<System.Collections.Generic.List<LayoutSnapshot>>(json);

            LayoutSnapshot snapshot = Assert.Single(list);
            Assert.NotNull(snapshot.ViewStates);
            Assert.NotNull(snapshot.Breakpoints);
            Assert.Empty(snapshot.ViewStates);
            Assert.Empty(snapshot.Breakpoints);
        }

        [Fact]
        public void UnknownFieldsFromANewerBuild_AreIgnoredRatherThanThrowing()
        {
            const string json = @"[{
                ""Id"": ""abc"",
                ""Name"": ""Forward"",
                ""SchemaVersion"": 1,
                ""SomethingAddedLater"": { ""nested"": [1, 2, 3] }
            }]";

            var list = JsonConvert.DeserializeObject<System.Collections.Generic.List<LayoutSnapshot>>(json);

            Assert.Equal("Forward", Assert.Single(list).Name);
        }

        [Fact]
        public void PropertyNamesAreStableOnTheWire()
        {
            var snapshot = new LayoutSnapshot
            {
                Id = "id-1",
                Name = "Layout",
                Created = "2026-08-03T00:00:00.0000000Z",
                SchemaVersion = 1,
                VsVersion = "18.0",
                DocWellBlob = "AQID",
            };

            string json = JsonConvert.SerializeObject(snapshot);

            Assert.Contains(@"""Id"":""id-1""", json);
            Assert.Contains(@"""Name"":""Layout""", json);
            Assert.Contains(@"""Created"":""2026-08-03T00:00:00.0000000Z""", json);
            Assert.Contains(@"""SchemaVersion"":1", json);
            Assert.Contains(@"""VsVersion"":""18.0""", json);
            Assert.Contains(@"""DocWellBlob"":""AQID""", json);
            Assert.Contains(@"""ViewStates"":[]", json);
            Assert.Contains(@"""Breakpoints"":[]", json);
        }

        [Fact]
        public void ViewStateEntry_RoundTrips()
        {
            var entry = new ViewStateEntry
            {
                Moniker = @"C:\repo\File.cs",
                ScrollLine = 3,
                CaretLine = 7,
                CaretColumn = 11,
                IsSplit = true,
                HasSelection = true,
                SelectionStartLine = 7,
                SelectionStartColumn = 11,
                SelectionEndLine = 9,
                SelectionEndColumn = 2,
            };

            var result = JsonConvert.DeserializeObject<ViewStateEntry>(JsonConvert.SerializeObject(entry));

            Assert.Equal(entry.Moniker, result.Moniker);
            Assert.Equal(entry.ScrollLine, result.ScrollLine);
            Assert.Equal(entry.CaretLine, result.CaretLine);
            Assert.Equal(entry.CaretColumn, result.CaretColumn);
            Assert.True(result.IsSplit);
            Assert.True(result.HasSelection);
            Assert.Equal(entry.SelectionStartLine, result.SelectionStartLine);
            Assert.Equal(entry.SelectionStartColumn, result.SelectionStartColumn);
            Assert.Equal(entry.SelectionEndLine, result.SelectionEndLine);
            Assert.Equal(entry.SelectionEndColumn, result.SelectionEndColumn);
        }

        [Fact]
        public void BreakpointEntry_RoundTripsIncludingRawEnumIntegers()
        {
            var entry = new BreakpointEntry
            {
                File = @"C:\repo\File.cs",
                Line = 42,
                Column = 1,
                Condition = "x == null",
                ConditionType = 2,
                HitCountTarget = 5,
                HitCountType = 3,
                Enabled = false,
                Tag = "watch",
                Language = "C#",
            };

            var result = JsonConvert.DeserializeObject<BreakpointEntry>(JsonConvert.SerializeObject(entry));

            Assert.Equal(entry.File, result.File);
            Assert.Equal(entry.Line, result.Line);
            Assert.Equal(entry.Column, result.Column);
            Assert.Equal(entry.Condition, result.Condition);
            Assert.Equal(2, result.ConditionType);
            Assert.Equal(5, result.HitCountTarget);
            Assert.Equal(3, result.HitCountType);
            Assert.False(result.Enabled);
            Assert.Equal("watch", result.Tag);
            Assert.Equal("C#", result.Language);
        }
    }
}
