# Window Layout Manager

A Visual Studio 2026 (v18) extension that saves and restores **named, per-solution working-context snapshots** — and lets you switch between them with a double-click.

Visual Studio keeps exactly one live working context per solution (in the `.suo`): which documents are open, how they're arranged, where the caret/scroll sits, and your breakpoints. It can't hold several *named* contexts and flip between them. This fills that gap — especially for debugging stages you set up repeatedly (e.g. five conditional breakpoints across three files, each scrolled to the right spot).

## What a snapshot captures

- **Open documents + document-well arrangement** — splits and tab groups (via `IVsUIShellDocumentWindowMgr`).
- **Per-document view state** — scroll position, caret, and selection (via `IWpfTextView`).
- **Breakpoints** — location, condition + type, hit-count + type, enabled state, and label (via `DTE.Debugger`).

Double-click a saved layout → the files reopen arranged, each view jumps to its position, and the breakpoints reappear.

## Using it

- **View ▸ Other Windows ▸ Window Layouts** opens the tool window.
- Type a name and press **Enter** to capture the current state.
- **Double-click** a layout (or select it and press Enter) to restore it.
- **Right-click** a layout for **Apply**, **Rename**, and **Delete**.
- No keyboard shortcuts ship by default, but the commands are rebindable: in **Tools ▸ Options ▸ Environment ▸ Keyboard**, search for `WindowLayouts` and assign whatever keys you like (e.g. F2 to `WindowLayouts.RenameLayout`). The bindings only fire while the tool window has focus.
- Apply is always **manual** — nothing is auto-applied when a solution opens.
- If documents have unsaved changes, applying a layout asks before closing them.

## Where layouts live

`<solution>\.vs\WindowLayoutManager\layouts.json`. The `.vs` folder is per-solution and git-ignored, so layouts are local to your machine and never touch the repo. Deleting `.vs` (a common troubleshooting step) also clears them.

## Scope and limitations

- **Tool-window docking is not captured.** There's no supported public API for arbitrary named tool-window docking layouts in the VS SDK, so this targets the document area — where the real value is. Use VS's built-in *Save Window Layout* for chrome.
- **Snapshots are point-in-time.** Line numbers are stored as captured; if you edit a file afterward, a restored caret/breakpoint lands on the shifted line (positions are clamped to the file, missing files are skipped).
- The document-well blob is **VS-version-bound** — a layout saved on a different major VS version is skipped on apply (breakpoints and view state still restore).
- Not shared across machines or committed (no export in v1).

## Building

Requires Visual Studio 2026 (v18) with the Visual Studio extension development workload.

```
msbuild src\WindowLayoutManager\WindowLayoutManager.csproj /restore /t:Build
```

Or open `WindowLayoutManager.slnx` and press **F5** — it launches the VS Experimental instance (`/rootsuffix Exp`) with the extension loaded.

## License

MIT.
