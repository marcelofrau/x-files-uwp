# Text Editor — Functional Specification

## Goal

Gamepad-oriented text file editor built into X-Files, using the UWP system virtual
keyboard for text input and D-pad/buttons for cursor navigation and editing actions.
Syntax highlighting for code files. Non-invasive — feels like a natural extension of
the file browser, not a separate app.

## User Stories

1. **Browse and edit**: user navigates to a `.txt` file, opens Y-menu, selects "Edit",
   editor opens fullscreen with file content and syntax highlighting.
2. **Navigate cursor**: user moves cursor freely with D-pad (char/line) and left stick
   (word jump) without entering text input mode.
3. **Type text**: user presses Select to open system keyboard, types with D-pad on
   virtual keyboard, presses Select again to close keyboard and return to navigation.
4. **Quick edits**: user presses B/X/Y for backspace/delete/enter without opening
   keyboard — common operations available directly on gamepad.
5. **Save and exit**: user presses Start to save, or B to exit with unsaved-changes
   confirmation.
6. **Large file warning**: user opens a file > 512KB, sees notification bar explaining
   no syntax highlight; file > 2MB opens read-only with warning.

## Scope — MVP

### Included
- Edit existing text files (any extension in `FilePreviewService.TextExtensions`)
- Two-mode input: Navigate mode (gamepad cursor/actions) + Input mode (system keyboard)
- Syntax highlighting via highlight.js (reuse existing preview infrastructure)
- Line numbers in editor gutter
- Word wrap toggle (LB + RB simultaneously)
- Undo / redo (JavaScript undo stack, bounded by file size)
- Dirty state tracking with unsaved-changes confirmation on exit
- File size tiers:
  - **< 512KB**: full edit + syntax highlight + full undo
  - **512KB–2MB**: edit enabled, syntax highlight OFF, notification bar warning,
    undo limited to 50 operations
  - **> 2MB**: read-only preview, notification bar explaining file too large to edit
- Encoding: UTF-8 with BOM detection (see `ENCODING.md`)
- Entry point: "Edit" action in `FileActionSheet` for text files
- Footer legend updates when editor is open (new button labels)

### Out of scope (Post-MVP)
- Create new file from scratch
- Find and replace
- Go to line number
- Multi-cursor editing
- Multiple tabs / split view
- Macro recording
- External keyboard shortcuts (Ctrl+S, Ctrl+Z, etc.) — gamepad only in MVP
- Remote/network file editing

## File Size Behavior

| Size | Mode | Syntax Highlight | Undo | Notification |
|---|---|---|---|---|
| < 512KB | Full edit | Yes | Unlimited (JS stack) | None |
| 512KB–2MB | Edit (degraded) | No | Limited (50 ops) | Bar: "Syntax highlighting disabled — file too large" |
| > 2MB | Read-only | No | N/A | Bar: "File too large to edit (>{size})" |

Thresholds are configurable constants in `TextEditorService.cs`.

## Entry Points

1. **FileActionSheet**: Y button on a text file → "Edit" action → opens editor
2. **A button on text file**: contextual — opens editor (same as audio A = play/pause)

Both entry points read the file, detect encoding, and open the `TextEditorOverlay`.

## Exit Points

1. **Start button**: save file → close editor → refresh preview column
2. **B button (no changes)**: close editor → return to column navigation
3. **B button (unsaved changes)**: confirmation dialog ("Save changes? Yes/No/Cancel")
   → save/discard/cancel
4. **File system error on save**: error dialog with retry/cancel options

## Completion Criteria

- [ ] Editor opens for any file in `TextExtensions` without crash
- [ ] Syntax highlighting visible for code files (JS, C#, Python, etc.)
- [ ] D-pad moves cursor correctly in all 4 directions
- [ ] Left stick jumps by word
- [ ] System keyboard appears on Select press, closes on second Select
- [ ] Text typed on system keyboard appears in editor with correct syntax highlighting
- [ ] Backspace (B), Delete (X), Enter (Y) work in Navigate mode
- [ ] Save writes file correctly (UTF-8 with BOM)
- [ ] Dirty state tracked — exit with unsaved changes shows confirmation
- [ ] File > 512KB opens without syntax highlight, notification bar visible
- [ ] File > 2MB opens read-only, warning visible
- [ ] Word wrap toggle works (LB+RB)
- [ ] Undo/redo works (Ctrl+Z / Ctrl+Y via JS)
- [ ] Footer legend updates when editor is open
- [ ] No regression in existing file browsing, preview, or media playback
