namespace WindowLayoutManager.Models
{
    /// <summary>Per-document scroll/caret/selection. All line and column numbers are 0-based.</summary>
    public sealed class ViewStateEntry
    {
        /// <summary>Absolute document path (the window frame's document moniker).</summary>
        public string Moniker { get; set; }

        /// <summary>Line displayed at the top of the viewport.</summary>
        public int ScrollLine { get; set; }

        public int CaretLine { get; set; }
        public int CaretColumn { get; set; }

        /// <summary>True when the code window had an active Window.Split pane at capture.</summary>
        public bool IsSplit { get; set; }

        public bool HasSelection { get; set; }
        public int SelectionStartLine { get; set; }
        public int SelectionStartColumn { get; set; }
        public int SelectionEndLine { get; set; }
        public int SelectionEndColumn { get; set; }
    }
}
