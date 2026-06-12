# Window Layout Manager

[![Visual Studio Marketplace](https://img.shields.io/badge/Visual%20Studio%20Marketplace-install-5C2D91)](https://marketplace.visualstudio.com/items?itemName=michaeljordanlxman.WindowLayoutManager)

A Visual Studio 2026 (v18) extension that saves and restores **named, per-solution working-context snapshots** — and lets you switch between them with a double-click.

Visual Studio keeps exactly one live working context per solution (in the `.suo`): which documents are open, how they're arranged, where the caret/scroll sits, and your breakpoints. It can't hold several *named* contexts and flip between them. This fills that gap — especially for debugging stages you set up repeatedly (e.g. five conditional breakpoints across three files, each scrolled to the right spot).

## What a snapshot captures

- **Open documents + document-well arrangement** — tab groups (via `IVsUIShellDocumentWindowMgr`).
- **Per-document view state** — scroll position, caret, selection, and whether the editor was split (via `IWpfTextView` / `IVsCodeWindow`).
- **Breakpoints** — location, condition + type, hit-count + type, enabled state, and label (via `DTE.Debugger`).

Double-click a saved layout → the files reopen arranged, each view jumps to its position, and the breakpoints reappear.

## Using it

- **View ▸ Other Windows ▸ Window Layouts** opens the tool window.
- Type a name and press **Enter** to capture the current state. Saving under an existing name asks, then **replaces** that layout — so re-saving "debugging-auth" after adding a breakpoint just works.
- **Double-click** a layout (or select it and press Enter) to restore it.
- **Right-click** a layout for **Apply**, **Rename**, and **Delete**.
- No keyboard shortcuts ship by default, but the commands are rebindable: in **Tools ▸ Options ▸ Environment ▸ Keyboard**, search for `WindowLayouts` and assign whatever keys you like (e.g. F2 to `WindowLayouts.RenameLayout`). The bindings only fire while the tool window has focus.
- Apply is always **manual** — nothing is auto-applied when a solution opens.
- If documents have unsaved changes, applying a layout asks before closing them.
- Applying is blocked **while debugging** — closing your documents and replacing the live breakpoint set mid-session is never what you meant. Stop debugging first.

## Where layouts live

`<solution dir>\.vs\WindowLayoutManager\<solution name>\layouts.json`. The `.vs` folder is git-ignored, so layouts are local to your machine and never touch the repo. Deleting `.vs` (a common troubleshooting step) also clears them.

Layouts are keyed by **solution name as well as directory** (the same convention VS uses for `.suo` files), so repos that keep several `.sln` or `.slnf` files in one folder — common in microservices setups — get a separate layout list per solution.

## Scope and limitations

- **Tool-window docking is not captured.** There's no supported public API for arbitrary named tool-window docking layouts in the VS SDK, so this targets the document area — where the real value is. Use VS's built-in *Save Window Layout* for chrome.
- **Snapshots are point-in-time.** Line numbers are stored as captured; if you edit a file afterward, a restored caret/breakpoint lands on the shifted line (positions are clamped to the file, missing files are skipped).
- The document-well blob is **VS-version-bound** — a layout saved on a different major VS version is skipped on apply (breakpoints and view state still restore).
- **Editor splits restore as a 50/50 split.** The in-editor split (Window ▸ Split) lives outside the public capture APIs, so the snapshot records only *that* a document was split. Apply re-splits it and the scroll/caret position restores, but the splitter proportion resets to the default 50/50.
- **Paths are absolute.** Move the repo (or use a git worktree) and existing layouts won't break anything, but they restore nothing useful — files and breakpoints at the old path are silently skipped. Re-save your layouts at the new location.
- **Two VS instances on the same solution** can race: both write `layouts.json` last-writer-wins, so a layout saved in one instance can be lost to a save in the other. Corrupt files are quarantined as `layouts.json.bad`, never silently overwritten.
- Not shared across machines or committed (no export in v1).

## Building

Requires Visual Studio 2026 (v18) with the Visual Studio extension development workload.

```
msbuild src\WindowLayoutManager\WindowLayoutManager.csproj /restore /t:Build
```

Or open `WindowLayoutManager.slnx` and press **F5** — it launches the VS Experimental instance (`/rootsuffix Exp`) with the extension loaded.

## License

MIT.
