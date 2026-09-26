using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// The services under test (SnapshotStore, SolutionInfo) are internal to the extension.
[assembly: InternalsVisibleTo("WindowLayoutManager.Tests")]

[assembly: AssemblyTitle("WindowLayoutManager")]
[assembly: AssemblyDescription("Save and restore named, per-solution working-context snapshots: open documents, view state, and breakpoints.")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyProduct("WindowLayoutManager")]
[assembly: AssemblyCopyright("MIT License")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: ComVisible(false)]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.3.0")]
