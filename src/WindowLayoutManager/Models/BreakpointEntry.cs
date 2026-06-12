namespace WindowLayoutManager.Models
{
    /// <summary>
    /// A file-location breakpoint. <see cref="ConditionType"/> and <see cref="HitCountType"/> are
    /// stored as the raw EnvDTE enum integer values so the persisted model carries no EnvDTE dependency.
    /// </summary>
    public sealed class BreakpointEntry
    {
        public string File { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }

        public string Condition { get; set; }

        /// <summary>EnvDTE.dbgBreakpointConditionType.</summary>
        public int ConditionType { get; set; }

        public int HitCountTarget { get; set; }

        /// <summary>EnvDTE.dbgHitCountType.</summary>
        public int HitCountType { get; set; }

        public bool Enabled { get; set; }

        /// <summary>Breakpoint label, if any.</summary>
        public string Tag { get; set; }

        public string Language { get; set; }
    }
}
