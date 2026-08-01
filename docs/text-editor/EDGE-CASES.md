# Text Editor — Edge Cases and Known Limitations

## WebView EdgeHTML Limitations

The UWP WebView uses the EdgeHTML engine (legacy Edge, not Chromium). Several
behaviors differ from modern browsers and must be handled.

### contentEditable Behavior

1. **Enter key produces `<div>` not `\n`**: When the user presses Enter in a
   contentEditable div, EdgeHTML inserts a `<div>` element, not a newline character.
   The JS editor must normalize these to `\n` for consistent text handling.

   **Solution**: MutationObserver on the contentEditable div, normalizing
   `<div>` → `\n<br>` on insert. Also normalize on `getText()`.

2. **Backspace at start of line**: EdgeHTML may merge with previous line's
   `<div>` in unexpected ways. The JS editor handles this explicitly by
   detecting backspace at position 0 of a line and removing the preceding `\n`.

3. **Paste behavior**: Paste inserts rich HTML by default. The editor must
   intercept paste events and strip HTML, keeping only plain text.

   **Solution**: `document.addEventListener('paste', function(e) {
   e.preventDefault(); var text = e.clipboardData.getData('text/plain');
   document.execCommand('insertText', false, text); });`

4. **Selection across lines**: EdgeHTML selection handling across `<div>`
   boundaries can be inconsistent. The editor uses `window.getSelection()`
   API and normalizes ranges manually.

### Highlight.js Integration

1. **Re-highlighting cost**: Running `hljs.highlightBlock()` on every keystroke
   is expensive for large files. For files > 4MB (read-only tier), highlighting
   doesn't matter — the editor never loads them editable.

2. **Highlight corruption**: After editing inside a highlighted `<code>` block,
   the highlight spans may become misaligned. Solution: re-highlight the
   entire block after each edit (debounced to 150ms).

3. **Language detection**: Highlight.js auto-detects language from content if
   no explicit language is set. The editor should set the language explicitly
   based on file extension (reusing `GetHighlightLang()` from
   `MillerColumnsPage`).

4. **ES5 compatibility**: The inlined highlight.js is v9.18.5 (ES5, EdgeHTML
   compatible). Newer versions use ES6+ and will not work in EdgeHTML.
   Do not update highlight.js without verifying EdgeHTML compatibility.

## TextBox ↔ WebView Sync Issues

### Timing

When the system keyboard types a character:
1. TextBox.Text changes
2. TextChanged event fires
3. C# reads TextBox.Text
4. C# invokes JS on WebView
5. JS updates contentEditable div
6. JS re-highlights (if enabled)

Steps 3-6 introduce latency. For fast typing, this can cause:
- Brief visual lag (text appears in TextBox before WebView updates)
- Lost keystrokes if sync is slower than typing speed

**Mitigation**: Debounce sync to 50ms. Buffer keystrokes. For files without
highlighting, sync is faster (no hljs step).

### Cursor Position Preservation

When syncing text from TextBox to WebView, the cursor position must be
preserved. The flow:

1. Read `TextBox.SelectionStart` (character offset)
2. Push text + offset to JS
3. JS: `setCursorPosition(offset)` using `window.getSelection()` and
   `Range` API

**Edge case**: If the text content changed (characters added/removed),
the cursor offset may need adjustment. The JS editor tracks the delta
and adjusts accordingly.

### Focus Loss

When the system keyboard appears, focus moves to the TextBox. When it
disappears, focus must return to the WebView (or stay on TextBox for
Navigate mode). Focus management:

```
Input mode enter:
  TextBox.Focus() → system keyboard appears

Input mode exit:
  CoreInputView.TryHide() → system keyboard disappears
  WebView.Focus() or TextBox.Focus() depending on mode
```

## Gamepad Input Routing

### Double-Fire Prevention

When the editor is open, `GamepadInputService` still polls at 30fps and
routes through `MillerColumnsPage`. The priority chain must check
`TextEditorOverlay.IsOpen` BEFORE any column navigation logic.

If the check is placed after column checks, button presses could trigger
both editor actions and column navigation simultaneously.

### Button Conflicts

Some gamepad buttons have different meanings in the editor vs columns:
- A: "Select item" in columns → "Toggle selection anchor" in editor
- Y: "Open context menu" in columns → "Insert newline" in editor
- B: "Go back" in columns → "Backspace" in editor

These conflicts are resolved by the priority chain — editor gets first
priority when open.

### Stick Drift

Left stick is used for word-jump in Navigate mode. Stick drift (analog
value not returning to exactly 0.0) could cause unintended cursor movement.

**Mitigation**: Use the same deadzone as `GamepadInputService` (0.5).
Additionally, require a 100ms cooldown between word jumps (same as
existing D-pad repeat logic).

## File Size Tiers

Two tiers, threshold `FullEditMaxBytes = 4MB` (see `TextEditorService.cs`).

### ≤ 4MB — Full Edit

- Syntax highlight: ON
- Undo: unlimited (JS undo stack)
- Sync: immediate (no debounce needed)
- All features available

### > 4MB — Read-Only

- No editing capability
- Content displayed in read-only WebView (no contentEditable)
- Notification bar: "File too large to edit ({size}) — read-only mode"
- Cursor movement still works (for reading), but no text modification
- System keyboard does not open
- Save button disabled (grayed out in footer)

### Threshold Constants

```csharp
// In TextEditorService.cs
public const long FullEditMaxBytes = 4 * 1024 * 1024; // 4MB
public enum FileTier { FullEdit, ReadOnly }
```

## Performance Considerations

### InvokeScriptAsync Overhead

Each `InvokeScriptAsync` call crosses the C# ↔ JS boundary. For frequent
operations (cursor movement on D-pad hold), this can be slow.

**Mitigation**:
- Batch operations where possible (e.g., move cursor 3 positions in one call
  instead of 3 separate calls)
- Use `MoveCursor(n)` with parameter instead of calling `MoveCursor(1)` n times
- For D-pad repeat, increase repeat interval to 100ms (vs 80ms for columns)

### Large File Rendering

A 500KB text file rendered in contentEditable div:
- ~15,000 lines of code
- EdgeHTML can handle this, but initial render may take 200-500ms
- Show loading indicator during initial render

### Memory

Each character in the editor occupies:
- ~1 byte in C# string (UTF-16 internally)
- ~1-4 bytes in the contentEditable div (depending on HTML structure)
- ~1-4 bytes in the JS text buffer

For a 4MB file: ~4MB × 3 copies ≈ 12MB. Acceptable (read-only tier above this).

## Unicode Edge Cases

### Combining Characters

Characters like accented letters (é = e + ◌́) use combining marks.
Moving the cursor by "1 character" must move past the entire grapheme
cluster, not just the base character or the combining mark.

**Mitigation**: Use JavaScript's `String.prototype[@@iterator]` or
`Intl.Segmenter` (if available in EdgeHTML) for grapheme-aware cursor
movement. Fallback: move by code point using `for...of` loop.

### Right-to-Left (RTL) Text

Arabic, Hebrew, and other RTL text is displayed correctly by EdgeHTML
but cursor movement may feel inverted (Left arrow moves right visually).

**Post-MVP**: Detect RTL content and optionally swap D-pad directions.
Not needed for MVP (primary audience: Western European languages).

### Emoji

Emoji characters (including multi-codepoint sequences like 👨‍👩‍👧)
are handled by EdgeHTML's text rendering. Cursor movement over emoji
may be imprecise (moving per code point instead of per grapheme).

**Acceptable for MVP**: Emoji editing is a rare use case for a
file browser text editor.

## Xbox-Specific Issues

### System Keyboard Availability

The system keyboard (`CoreInputView.TryShow`) requires:
- No physical keyboard connected
- Xbox or handheld device with gamepad
- Windows 10 17763+ (for `InputPane`) or Windows 11 26100.3624+ (for `CoreInputViewKind.Gamepad`)

If the API call fails (returns false), the editor falls back to:
1. Try `InputPane.TryShow()` (older API)
2. If both fail: show a message "Connect a keyboard or use gamepad buttons for text input"
   and allow editing via gamepad-only mode (no system keyboard)

### Display Resolution

Xbox outputs at 1080p or 4K. The editor layout must:
- Use relative sizing (percentages, star rows/columns)
- Scale font size based on DPI (currently hardcoded at 12-15px for
  other controls — validate on real Xbox)
- Ensure touch keyboard (if visible) doesn't overlap the editor content

### Performance on Xbox Hardware

Xbox One / Series S|A have different CPU/GPU capabilities. The editor
must be tested on:
- Xbox Series X (target primary)
- Xbox Series S (lower GPU, same CPU)
- Xbox One (if still supported — older CPU, slower JS execution)

EdgeHTML performance on Xbox hardware is a known variable. If JS execution
is too slow, the highlight threshold may need to be lowered (e.g., 256KB
instead of 4MB).
