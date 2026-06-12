using System;

namespace WindowLayoutManager.Commands
{
    /// <summary>Command set and IDs — must match the Symbols section of VSCommandTable.vsct.</summary>
    internal static class LayoutCommandIds
    {
        public static readonly Guid CommandSet = new Guid("98410079-8D8A-48EC-8799-0326F293B946");

        public const int LayoutContextMenu = 0x2000;
        public const int ApplyLayout = 0x0101;
        public const int RenameLayout = 0x0102;
        public const int DeleteLayout = 0x0103;
    }
}
