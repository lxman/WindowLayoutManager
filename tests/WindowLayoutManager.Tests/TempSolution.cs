using System;
using System.IO;
using System.Text;
using WindowLayoutManager.Services;

namespace WindowLayoutManager.Tests
{
    /// <summary>
    /// A throwaway solution directory on disk, with helpers for reaching the layouts file the way
    /// <see cref="SnapshotStore"/> lays it out. Deletes itself on dispose.
    /// </summary>
    internal sealed class TempSolution : IDisposable
    {
        public TempSolution(string solutionName = "MySolution")
        {
            Root = Path.Combine(Path.GetTempPath(), "WindowLayoutManager.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Name = solutionName;
            Info = new SolutionInfo(Root, solutionName);
        }

        public string Root { get; }

        public string Name { get; }

        public SolutionInfo Info { get; }

        /// <summary>
        /// The expected layouts path, spelled out here rather than borrowed from the production
        /// helper so that a change to the storage layout fails these tests instead of moving with them.
        /// </summary>
        public string LayoutsPath => Path.Combine(Root, ".vs", "WindowLayoutManager", Name, "layouts.json");

        public string QuarantinePath => LayoutsPath + ".bad";

        public void WriteLayoutsFile(string contents)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LayoutsPath));
            File.WriteAllText(LayoutsPath, contents, new UTF8Encoding(false));
        }

        public string ReadLayoutsFile() => File.ReadAllText(LayoutsPath, Encoding.UTF8);

        public byte[] ReadLayoutsBytes() => File.ReadAllBytes(LayoutsPath);

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); }
            catch { /* a leftover temp dir must never fail a test run */ }
        }
    }
}
