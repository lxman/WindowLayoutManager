# Window Layout Manager

Save and restore **named, per-solution working-context snapshots** — and switch between them with a double-click.

Visual Studio keeps exactly one live working context per solution: which documents are open, how they're arranged, where the caret and scroll sit, and your breakpoints. It can't hold several *named* contexts and flip between them. This fills that gap — especially for debugging stages you set up repeatedly (e.g. five conditional breakpoints across three files, each scrolled to the right spot).

## What a snapshot captures

- **Open documents + arrangement** — tab groups and which document is active.
- **Per-document view state** — scroll position, caret, selection, and whether the editor was split.
- **Breakpoints** — location, condition + type, hit count + type, enabled state, and label.

Double-click a saved layout → the files reopen arranged, each view jumps to its position, and the breakpoints reappear.

## Using it

- **View ▸ Other Windows ▸ Window Layouts** opens the tool window.
- Type a name and press **Enter** to capture the current state. Saving under an existing name asks, then **replaces** that layout — so re-saving after adding a breakpoint just works.
- **Double-click** a layout (or select it and press Enter) to restore it.
- **Right-click** a layout for **Apply**, **Rename**, and **Delete**.
- No keyboard shortcuts ship by default, but the commands are rebindable: in **Tools ▸ Options ▸ Environment ▸ Keyboard**, search for `WindowLayouts` and assign whatever keys you like. The bindings only fire while the tool window has focus.
- Apply is always **manual** — nothing is auto-applied when a solution opens. Unsaved changes prompt before documents close, and applying is blocked while debugging.

## Where layouts live

`<solution dir>\.vs\WindowLayoutManager\<solution name>\layouts.json` — per-solution, git-ignored, never touches your repo. Repos with several `.sln`/`.slnf` files in one folder get a separate layout list per solution.

## Scope and limitations

- **Tool-window docking is not captured.** There's no supported public API for arbitrary named tool-window docking layouts, so this targets the document area — where the real value is. Use VS's built-in *Save Window Layout* for chrome.
- **Snapshots are point-in-time.** Edit a file afterward and a restored caret/breakpoint lands on the shifted line; positions are clamped, missing files are skipped.
- **Editor splits restore as 50/50.** The snapshot records *that* a document was split and restores scroll/caret, but the splitter proportion resets.
- **Paths are absolute.** Moving the repo (or using a git worktree) doesn't break anything, but old layouts restore nothing useful at the new path.
- Layouts are local to your machine — not shared, not committed (no export in v1).

## Requirements

Visual Studio 2026 (version 18), any edition.

Source and issues: https://github.com/lxman/WindowLayoutManager
