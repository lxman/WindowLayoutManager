using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WindowLayoutManager.Models;
using WindowLayoutManager.Services;
using Xunit;

namespace WindowLayoutManager.Tests
{
    public class SnapshotStoreTests
    {
        private static readonly CancellationToken None = CancellationToken.None;

        private static SnapshotStore NewStore() => new SnapshotStore();

        private static LayoutSnapshot NewSnapshot(string name, int schemaVersion = 1)
        {
            return new LayoutSnapshot
            {
                Id = Guid.NewGuid().ToString("D"),
                Name = name,
                Created = DateTime.UtcNow.ToString("o"),
                SchemaVersion = schemaVersion,
                VsVersion = "18.0",
                DocWellBlob = Convert.ToBase64String(new byte[] { 1, 2, 3 }),
            };
        }

        // ---- Load: absent and degenerate inputs ------------------------------------------------

        [Fact]
        public async Task LoadAsync_WhenFileMissing_ReturnsEmptyAuthoritativeList()
        {
            using (var solution = new TempSolution())
            {
                SnapshotLoadResult result = await NewStore().LoadAsync(solution.Info, None);

                Assert.Empty(result.Snapshots);
                Assert.True(result.IsAuthoritative);
                Assert.Null(result.Warning);
            }
        }

        [Fact]
        public async Task LoadAsync_WhenSolutionIsNull_ReturnsEmptyAuthoritativeList()
        {
            SnapshotLoadResult result = await NewStore().LoadAsync(null, None);

            Assert.Empty(result.Snapshots);
            Assert.True(result.IsAuthoritative);
        }

        [Theory]
        [InlineData(null, "Name")]
        [InlineData("", "Name")]
        [InlineData(@"C:\dir", null)]
        [InlineData(@"C:\dir", "")]
        public async Task LoadAsync_WhenSolutionIdentityIncomplete_ReturnsEmptyAuthoritativeList(
            string directory, string name)
        {
            SnapshotLoadResult result = await NewStore().LoadAsync(new SolutionInfo(directory, name), None);

            Assert.Empty(result.Snapshots);
            Assert.True(result.IsAuthoritative);
        }

        // ---- Load: well-formed content ---------------------------------------------------------

        [Fact]
        public async Task LoadAsync_ReadsSnapshotsWrittenBySave()
        {
            using (var solution = new TempSolution())
            {
                SnapshotStore store = NewStore();
                LayoutSnapshot saved = NewSnapshot("Debugging session");
                saved.ViewStates.Add(new ViewStateEntry
                {
                    Moniker = @"C:\repo\Program.cs",
                    ScrollLine = 12,
                    CaretLine = 20,
                    CaretColumn = 4,
                    IsSplit = true,
                    HasSelection = true,
                    SelectionStartLine = 20,
                    SelectionStartColumn = 4,
                    SelectionEndLine = 22,
                    SelectionEndColumn = 8,
                });
                saved.Breakpoints.Add(new BreakpointEntry
                {
                    File = @"C:\repo\Program.cs",
                    Line = 20,
                    Column = 1,
                    Condition = "i > 5",
                    ConditionType = 1,
                    HitCountTarget = 3,
                    HitCountType = 2,
                    Enabled = true,
                    Tag = "loop",
                    Language = "C#",
                });

                await store.SaveAsync(solution.Info, new List<LayoutSnapshot> { saved }, None);
                SnapshotLoadResult result = await store.LoadAsync(solution.Info, None);

                Assert.True(result.IsAuthoritative);
                LayoutSnapshot loaded = Assert.Single(result.Snapshots);
                Assert.Equal(saved.Id, loaded.Id);
                Assert.Equal(saved.Name, loaded.Name);
                Assert.Equal(saved.Created, loaded.Created);
                Assert.Equal(saved.SchemaVersion, loaded.SchemaVersion);
                Assert.Equal(saved.VsVersion, loaded.VsVersion);
                Assert.Equal(saved.DocWellBlob, loaded.DocWellBlob);

                ViewStateEntry view = Assert.Single(loaded.ViewStates);
                Assert.Equal(@"C:\repo\Program.cs", view.Moniker);
                Assert.Equal(12, view.ScrollLine);
                Assert.Equal(20, view.CaretLine);
                Assert.Equal(4, view.CaretColumn);
                Assert.True(view.IsSplit);
                Assert.True(view.HasSelection);
                Assert.Equal(22, view.SelectionEndLine);
                Assert.Equal(8, view.SelectionEndColumn);

                BreakpointEntry breakpoint = Assert.Single(loaded.Breakpoints);
                Assert.Equal(@"C:\repo\Program.cs", breakpoint.File);
                Assert.Equal(20, breakpoint.Line);
                Assert.Equal("i > 5", breakpoint.Condition);
                Assert.Equal(3, breakpoint.HitCountTarget);
                Assert.Equal(2, breakpoint.HitCountType);
                Assert.True(breakpoint.Enabled);
                Assert.Equal("loop", breakpoint.Tag);
            }
        }

        [Fact]
        public async Task LoadAsync_PreservesSnapshotOrder()
        {
            using (var solution = new TempSolution())
            {
                SnapshotStore store = NewStore();
                var snapshots = new List<LayoutSnapshot>
                {
                    NewSnapshot("First"), NewSnapshot("Second"), NewSnapshot("Third"),
                };

                await store.SaveAsync(solution.Info, snapshots, None);
                SnapshotLoadResult result = await store.LoadAsync(solution.Info, None);

                Assert.Equal(new[] { "First", "Second", "Third" },
                    result.Snapshots.Select(s => s.Name).ToArray());
            }
        }

        [Fact]
        public async Task LoadAsync_WhenJsonIsNullLiteral_ReturnsEmptyAuthoritativeList()
        {
            using (var solution = new TempSolution())
            {
                solution.WriteLayoutsFile("null");

                SnapshotLoadResult result = await NewStore().LoadAsync(solution.Info, None);

                Assert.Empty(result.Snapshots);
                Assert.True(result.IsAuthoritative);
                Assert.Null(result.Warning);
            }
        }

        // ---- Load: forward compatibility -------------------------------------------------------

        [Fact]
        public async Task LoadAsync_DropsSnapshotsFromNewerSchema_AndKeepsCurrentOnes()
        {
            using (var solution = new TempSolution())
            {
                SnapshotStore store = NewStore();
                var snapshots = new List<LayoutSnapshot>
                {
                    NewSnapshot("Current", SnapshotStore.SchemaVersion),
                    NewSnapshot("FromTheFuture", SnapshotStore.SchemaVersion + 1),
                    NewSnapshot("FarFuture", SnapshotStore.SchemaVersion + 99),
                };
                await store.SaveAsync(solution.Info, snapshots, None);

                SnapshotLoadResult result = await store.LoadAsync(solution.Info, None);

                LayoutSnapshot kept = Assert.Single(result.Snapshots);
                Assert.Equal("Current", kept.Name);
                Assert.True(result.IsAuthoritative);
            }
        }

        [Fact]
        public async Task LoadAsync_KeepsSnapshotsFromOlderSchema()
        {
            using (var solution = new TempSolution())
            {
                SnapshotStore store = NewStore();
                await store.SaveAsync(solution.Info,
                    new List<LayoutSnapshot> { NewSnapshot("Ancient", 0) }, None);

                SnapshotLoadResult result = await store.LoadAsync(solution.Info, None);

                Assert.Equal("Ancient", Assert.Single(result.Snapshots).Name);
            }
        }

        [Fact]
        public void SchemaVersion_IsOne()
        {
            Assert.Equal(1, SnapshotStore.SchemaVersion);
        }

        // ---- Load: corrupt file quarantine -----------------------------------------------------

        [Fact]
        public async Task LoadAsync_WhenFileCorrupt_QuarantinesItAndStaysAuthoritative()
        {
            using (var solution = new TempSolution())
            {
                const string corrupt = "{ this is not valid json ][";
                solution.WriteLayoutsFile(corrupt);

                SnapshotLoadResult result = await NewStore().LoadAsync(solution.Info, None);

                Assert.Empty(result.Snapshots);
                // Authoritative: the bad bytes are safely aside, so a later save cannot lose anything.
                Assert.True(result.IsAuthoritative);
                Assert.Contains("corrupt", result.Warning, StringComparison.OrdinalIgnoreCase);

                Assert.False(File.Exists(solution.LayoutsPath));
                Assert.True(File.Exists(solution.QuarantinePath));
                Assert.Equal(corrupt, File.ReadAllText(solution.QuarantinePath, Encoding.UTF8));
            }
        }

        [Fact]
        public async Task LoadAsync_WhenCorruptTwice_OverwritesTheEarlierQuarantineFile()
        {
            using (var solution = new TempSolution())
            {
                SnapshotStore store = NewStore();

                solution.WriteLayoutsFile("first corruption [");
                await store.LoadAsync(solution.Info, None);

                solution.WriteLayoutsFile("second corruption [");
                SnapshotLoadResult result = await store.LoadAsync(solution.Info, None);

                Assert.True(result.IsAuthoritative);
                Assert.Equal("second corruption [",
                    File.ReadAllText(solution.QuarantinePath, Encoding.UTF8));
            }
        }

        [Fact]
        public async Task LoadAsync_AfterQuarantine_SaveStartsFromCleanSlate()
        {
            using (var solution = new TempSolution())
            {
                SnapshotStore store = NewStore();
                solution.WriteLayoutsFile("garbage {{{");

                SnapshotLoadResult loaded = await store.LoadAsync(solution.Info, None);
                loaded.Snapshots.Add(NewSnapshot("Fresh"));
                await store.SaveAsync(solution.Info, loaded.Snapshots, None);

                SnapshotLoadResult reloaded = await store.LoadAsync(solution.Info, None);
                Assert.Equal("Fresh", Assert.Single(reloaded.Snapshots).Name);
            }
        }

        // ---- Load: unreadable file is NOT authoritative -----------------------------------------

        [Fact]
        public async Task LoadAsync_WhenFileLocked_ReturnsNonAuthoritativeResult()
        {
            using (var solution = new TempSolution())
            {
                await NewStore().SaveAsync(solution.Info,
                    new List<LayoutSnapshot> { NewSnapshot("Precious") }, None);

                // Hold the file open exclusively so the read fails the way a rival process would cause.
                using (new FileStream(solution.LayoutsPath, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    SnapshotLoadResult result = await NewStore().LoadAsync(solution.Info, None);

                    Assert.Empty(result.Snapshots);
                    // The critical assertion: callers must not write back over layouts they never saw.
                    Assert.False(result.IsAuthoritative);
                    Assert.NotNull(result.Warning);
                }

                // The unread file is still intact, and was never quarantined.
                Assert.False(File.Exists(solution.QuarantinePath));
                SnapshotLoadResult afterUnlock = await NewStore().LoadAsync(solution.Info, None);
                Assert.Equal("Precious", Assert.Single(afterUnlock.Snapshots).Name);
            }
        }

        // ---- Save ------------------------------------------------------------------------------

        [Fact]
        public async Task SaveAsync_WritesToTheDocumentedPathUnderDotVs()
        {
            using (var solution = new TempSolution("Contoso"))
            {
                await NewStore().SaveAsync(solution.Info,
                    new List<LayoutSnapshot> { NewSnapshot("A") }, None);

                string expected = Path.Combine(
                    solution.Root, ".vs", "WindowLayoutManager", "Contoso", "layouts.json");
                Assert.True(File.Exists(expected), $"expected layouts at {expected}");
            }
        }

        [Fact]
        public async Task SaveAsync_CreatesMissingDirectories()
        {
            using (var solution = new TempSolution())
            {
                Assert.False(Directory.Exists(Path.Combine(solution.Root, ".vs")));

                await NewStore().SaveAsync(solution.Info,
                    new List<LayoutSnapshot> { NewSnapshot("A") }, None);

                Assert.True(File.Exists(solution.LayoutsPath));
            }
        }

        [Fact]
        public async Task SaveAsync_ReplacesExistingFileAndLeavesNoTempFile()
        {
            using (var solution = new TempSolution())
            {
                SnapshotStore store = NewStore();

                await store.SaveAsync(solution.Info,
                    new List<LayoutSnapshot> { NewSnapshot("Original") }, None);
                await store.SaveAsync(solution.Info,
                    new List<LayoutSnapshot> { NewSnapshot("Replacement") }, None);

                SnapshotLoadResult result = await store.LoadAsync(solution.Info, None);
                Assert.Equal("Replacement", Assert.Single(result.Snapshots).Name);

                Assert.False(File.Exists(solution.LayoutsPath + ".tmp"));
                Assert.Equal(new[] { "layouts.json" },
                    Directory.GetFiles(Path.GetDirectoryName(solution.LayoutsPath))
                             .Select(Path.GetFileName).ToArray());
            }
        }

        [Fact]
        public async Task SaveAsync_WritesUtf8WithoutByteOrderMark()
        {
            using (var solution = new TempSolution())
            {
                await NewStore().SaveAsync(solution.Info,
                    new List<LayoutSnapshot> { NewSnapshot("Ünïcødé ✓") }, None);

                byte[] bytes = solution.ReadLayoutsBytes();
                Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                    "layouts.json must not carry a UTF-8 BOM");

                SnapshotLoadResult result = await NewStore().LoadAsync(solution.Info, None);
                Assert.Equal("Ünïcødé ✓", Assert.Single(result.Snapshots).Name);
            }
        }

        [Fact]
        public async Task SaveAsync_WritesAnEmptyListWhenThereAreNoSnapshots()
        {
            using (var solution = new TempSolution())
            {
                SnapshotStore store = NewStore();

                await store.SaveAsync(solution.Info, new List<LayoutSnapshot>(), None);

                Assert.True(File.Exists(solution.LayoutsPath));
                SnapshotLoadResult result = await store.LoadAsync(solution.Info, None);
                Assert.Empty(result.Snapshots);
                Assert.True(result.IsAuthoritative);
            }
        }

        [Theory]
        [InlineData(null, "Name")]
        [InlineData("", "Name")]
        [InlineData(@"C:\dir", null)]
        [InlineData(@"C:\dir", "")]
        public async Task SaveAsync_WhenSolutionIdentityIncomplete_DoesNothing(string directory, string name)
        {
            // Must not throw, and must not create anything on disk.
            await NewStore().SaveAsync(new SolutionInfo(directory, name),
                new List<LayoutSnapshot> { NewSnapshot("A") }, None);
        }

        [Fact]
        public async Task SaveAsync_WhenSolutionIsNull_DoesNothing()
        {
            await NewStore().SaveAsync(null, new List<LayoutSnapshot> { NewSnapshot("A") }, None);
        }

        // ---- Keying: directory + solution name --------------------------------------------------

        [Fact]
        public async Task SolutionsSharingADirectory_KeepSeparateLayoutLists()
        {
            // The documented reason for keying on name as well as directory: several .sln files in
            // one repo directory must not pool their layouts.
            using (var shared = new TempSolution("Frontend"))
            {
                SnapshotStore store = NewStore();
                var frontend = new SolutionInfo(shared.Root, "Frontend");
                var backend = new SolutionInfo(shared.Root, "Backend");

                await store.SaveAsync(frontend, new List<LayoutSnapshot> { NewSnapshot("FE layout") }, None);
                await store.SaveAsync(backend, new List<LayoutSnapshot> { NewSnapshot("BE layout") }, None);

                Assert.Equal("FE layout",
                    Assert.Single((await store.LoadAsync(frontend, None)).Snapshots).Name);
                Assert.Equal("BE layout",
                    Assert.Single((await store.LoadAsync(backend, None)).Snapshots).Name);
            }
        }

        [Theory]
        [InlineData("My:Solution")]
        [InlineData("Solution*Name")]
        [InlineData("A/B")]
        [InlineData(@"A\B")]
        [InlineData("Question?")]
        [InlineData("Pipe|Name")]
        [InlineData("Quote\"Name")]
        public async Task SolutionNamesWithInvalidPathCharacters_RoundTrip(string solutionName)
        {
            using (var temp = new TempSolution())
            {
                SnapshotStore store = NewStore();
                var solution = new SolutionInfo(temp.Root, solutionName);

                await store.SaveAsync(solution, new List<LayoutSnapshot> { NewSnapshot("Layout") }, None);
                SnapshotLoadResult result = await store.LoadAsync(solution, None);

                Assert.Equal("Layout", Assert.Single(result.Snapshots).Name);
            }
        }

        [Fact]
        public async Task InvalidPathCharactersInSolutionName_AreReplacedWithUnderscores()
        {
            using (var temp = new TempSolution())
            {
                var solution = new SolutionInfo(temp.Root, "My:Solution*Name");

                await NewStore().SaveAsync(solution, new List<LayoutSnapshot> { NewSnapshot("A") }, None);

                string expected = Path.Combine(
                    temp.Root, ".vs", "WindowLayoutManager", "My_Solution_Name", "layouts.json");
                Assert.True(File.Exists(expected), $"expected layouts at {expected}");
            }
        }

        [Fact]
        public async Task SolutionNamesDifferingOnlyByCase_AreTreatedAsOneOnWindows()
        {
            // Windows paths are case-insensitive, so "Contoso" and "contoso" land in the same file.
            // Pinning the behaviour so a future move to a case-sensitive store is a deliberate choice.
            using (var temp = new TempSolution())
            {
                SnapshotStore store = NewStore();

                await store.SaveAsync(new SolutionInfo(temp.Root, "Contoso"),
                    new List<LayoutSnapshot> { NewSnapshot("Upper") }, None);
                SnapshotLoadResult result = await store.LoadAsync(
                    new SolutionInfo(temp.Root, "contoso"), None);

                Assert.Equal("Upper", Assert.Single(result.Snapshots).Name);
            }
        }
    }
}
