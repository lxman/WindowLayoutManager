using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using WindowLayoutManager.Models;

namespace WindowLayoutManager.Services
{
    /// <summary>
    /// Result of <see cref="SnapshotStore.LoadAsync"/>. <see cref="IsAuthoritative"/> tells callers
    /// whether <see cref="Snapshots"/> may be modified and written back: when false, the file exists
    /// but could not be read, and rewriting it would destroy layouts we never saw.
    /// </summary>
    internal sealed class SnapshotLoadResult
    {
        public SnapshotLoadResult(List<LayoutSnapshot> snapshots, bool isAuthoritative, string warning)
        {
            Snapshots = snapshots;
            IsAuthoritative = isAuthoritative;
            Warning = warning;
        }

        public List<LayoutSnapshot> Snapshots { get; }

        /// <summary>True when the file was read (or does not exist), so load-modify-save cannot lose data.</summary>
        public bool IsAuthoritative { get; }

        /// <summary>Short user-facing description of a load problem, or null when the load was clean.</summary>
        public string Warning { get; }
    }

    /// <summary>
    /// Reads and writes the per-solution snapshot list at
    /// <c>&lt;solutionDir&gt;\.vs\WindowLayoutManager\&lt;solutionName&gt;\layouts.json</c>.
    /// Keyed by solution name as well as directory (mirroring VS's own <c>.vs\&lt;name&gt;\v18</c>
    /// convention) so several .sln files sharing one directory keep separate layout lists.
    /// Pure file I/O — never touches COM, always called off the UI thread.
    /// </summary>
    internal sealed class SnapshotStore
    {
        private const int CurrentSchemaVersion = 1;

        /// <summary>The schema version stamped onto newly captured snapshots.</summary>
        public static int SchemaVersion => CurrentSchemaVersion;

        private static string GetLayoutsPath(SolutionInfo solution) =>
            Path.Combine(solution.Directory, ".vs", "WindowLayoutManager",
                Sanitize(solution.Name), "layouts.json");

        private static string Sanitize(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
                sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            return sb.ToString();
        }

        /// <summary>
        /// Loads the snapshot list. A missing file yields an empty, authoritative list. A corrupt
        /// file is quarantined to <c>layouts.json.bad</c> (preserving its bytes for recovery) and
        /// yields an empty, authoritative list. An unreadable file (locked, I/O error) yields an
        /// empty, NON-authoritative list that callers must not write back.
        /// </summary>
        public async Task<SnapshotLoadResult> LoadAsync(SolutionInfo solution, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(solution?.Directory) || string.IsNullOrEmpty(solution.Name))
                return new SnapshotLoadResult(new List<LayoutSnapshot>(), true, null);
            string path = GetLayoutsPath(solution);
            return await Task.Run(() => LoadCore(path), ct).ConfigureAwait(false);
        }

        private static SnapshotLoadResult LoadCore(string path)
        {
            if (!File.Exists(path))
                return new SnapshotLoadResult(new List<LayoutSnapshot>(), true, null);

            string json;
            try
            {
                json = File.ReadAllText(path, Encoding.UTF8);
            }
            catch
            {
                return new SnapshotLoadResult(new List<LayoutSnapshot>(), false,
                    "Layouts file could not be read.");
            }

            try
            {
                var list = JsonConvert.DeserializeObject<List<LayoutSnapshot>>(json) ?? new List<LayoutSnapshot>();
                // Forward-compatibility: ignore any record written by a newer schema.
                list.RemoveAll(s => s.SchemaVersion > CurrentSchemaVersion);
                return new SnapshotLoadResult(list, true, null);
            }
            catch
            {
                // Corrupt JSON. Quarantine the file so its bytes survive for manual recovery and
                // a later save starts from a clean slate instead of overwriting the evidence.
                try
                {
                    File.Copy(path, path + ".bad", true);
                    File.Delete(path);
                    return new SnapshotLoadResult(new List<LayoutSnapshot>(), true,
                        "Layouts file was corrupt — backed up as layouts.json.bad.");
                }
                catch
                {
                    return new SnapshotLoadResult(new List<LayoutSnapshot>(), false,
                        "Layouts file is corrupt and could not be backed up.");
                }
            }
        }

        /// <summary>Writes the snapshot list atomically (temp file + replace).</summary>
        public async Task SaveAsync(SolutionInfo solution, List<LayoutSnapshot> snapshots, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(solution?.Directory) || string.IsNullOrEmpty(solution.Name)) return;
            string path = GetLayoutsPath(solution);
            await Task.Run(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string json = JsonConvert.SerializeObject(snapshots, Formatting.Indented);
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, json, new UTF8Encoding(false));
                if (File.Exists(path))
                    File.Replace(tmp, path, null);   // atomic on NTFS; last writer wins between VS instances
                else
                    File.Move(tmp, path);
            }, ct).ConfigureAwait(false);
        }
    }
}
