using System.Collections.Generic;

namespace WindowLayoutManager.Models
{
    /// <summary>
    /// One named, per-solution working-context snapshot: the open documents and their
    /// document-well arrangement (captured as an opaque VS blob), each document's view state,
    /// and the breakpoint set at the moment of capture.
    /// </summary>
    public sealed class LayoutSnapshot
    {
        public string Id { get; set; }
        public string Name { get; set; }

        /// <summary>ISO-8601 UTC timestamp of when the snapshot was captured.</summary>
        public string Created { get; set; }

        /// <summary>Storage schema version. Current = 1. Entries with a higher value are ignored on load.</summary>
        public int SchemaVersion { get; set; }

        /// <summary>Major.minor of the Visual Studio that wrote the doc-well blob (e.g. "18.0").</summary>
        public string VsVersion { get; set; }

        /// <summary>Base64 of the <c>IVsUIShellDocumentWindowMgr</c> document-position stream. Opaque, VS-version-bound.</summary>
        public string DocWellBlob { get; set; }

        public List<ViewStateEntry> ViewStates { get; set; } = new List<ViewStateEntry>();
        public List<BreakpointEntry> Breakpoints { get; set; } = new List<BreakpointEntry>();
    }
}
