# Text Editor — Implementation Plan

Step-by-step execution plan for Phase 12. Each step has a test gate — do not advance
until the gate passes. See `docs/text-editor/` for specs, architecture, input mapping,
encoding, and edge cases.

## Dependency Graph

```
Step 1 (Service)  ──┐
                    ├──→ Step 3 (Overlay) ──→ Step 4 (Integration) ──→ Step 5 (Edit action) ──→ Step 6 (Features) ──→ Step 7 (Polish)
Step 2 (HTML/JS) ──┘
```

## Conventions

- All new C# files go in existing folders: `Controls/`, `FileSystem/`
- WebView assets go in `Assets/`: `editor.html`, `editor.js`
- Reuse existing: `highlight.min.js`, `highlight-aco.css` (monospace = system Consolas)
- Follow existing code style: no MVVM, all code-behind, `Log.Information/Warning` everywhere
- System keyboard API: `CoreInputView.TryShow(CoreInputViewKind.Gamepad)` with
  `InputPane.TryShow()` fallback (compile-time check via `ApiInformation`)

---

## Step 1 — TextEditorService.cs

**File**: `FileSystem/TextEditorService.cs`
**Depends on**: nothing
**Estimated scope**: ~300 lines

### What to build

1. **File I/O** via Win2 P/Invoke (same pattern as `FilePreviewService`):
   - `LoadAsync(string filePath)` → returns `(string text, Encoding encoding, long size, bool isReadOnly)`
   - `SaveAsync(string filePath, string content)` → always UTF-8 with BOM
   - Uses `CreateFile2FromAppW` + `ReadFile` / `WriteFile` (same P/Invoke as `FilePreviewService`)

2. **Encoding detection** (priority order):
   - BOM sniffing: `EF BB BF` → UTF-8, `FF FE` → UTF-16 LE, `FE FF` → UTF-16 BE,
     `FF FE 00 00` → UTF-32 LE, `00 00 FE FF` → UTF-32 BE
   - Null-byte heuristic: null in first 512 bytes → try UTF-16 LE
   - UTF-8 validation: strict byte sequence check
   - Fallback: Windows-1252
   - Binary detection: null byte in first 8KB (non-UTF-16) → read-only

3. **File size tiers**:
   ```csharp
   public const long FullEditMaxBytes = 512 * 1024;        // 512KB
   public const long DegradedEditMaxBytes = 2 * 1024 * 1024; // 2MB
   public const int LimitedUndoCount = 50;
   public const int SyncDebounceMs = 50;

   public enum FileTier { FullEdit, DegradedEdit, ReadOnly }
   public static FileTier GetFileTier(long size)
   ```

4. **Line ending detection**:
   - Count `\r\n`, `\n`, `\r` occurrences
   - Return dominant style
   - On save: convert `\n` back to detected style

### Test gate
- Call `LoadAsync` on a UTF-8 .txt and a .cs file → verify encoding detected + content correct
- Call `SaveAsync` → reopen → verify UTF-8 BOM prefix (`EF BB BF`)
- Test binary detection: open a .exe → verify `isReadOnly = true`
- Build: `MSBuild /p:Configuration=Debug /p:Platform=x64` succeeds

---

## Step 2 — editor.html + editor.js

**Files**: `Assets/editor.html`, `Assets/editor.js`
**Depends on**: nothing (uses existing `highlight.min.js` + system Consolas font)
**Estimated scope**: editor.html ~100 lines, editor.js ~600 lines

### editor.html structure

```html
<!DOCTYPE html>
<html>
<head>
  <meta charset="utf-8">
  <style>
    /* Reset + body: full viewport, dark background #0F1318 */
    /* Editor container: flex row (line-numbers | code area) */
    /* Line numbers gutter: fixed width, #5A5C60 color, right-aligned */
    /* Code area: contentEditable, Consolas 13px, #D4D4D4, line-height 1.5 */
    /* Cursor: CSS caret-color or custom blinking div */
    /* Selection: background #264F78 */
    /* Word wrap: white-space: pre-wrap vs pre */
    /* Scrollbar: thin, dark theme */
  </style>
  <style>{highlight-aco.css inlined}</style>
</head>
<body>
  <div id="editor" contenteditable="true" spellcheck="false">
    <div id="line-numbers"></div>
    <pre><code id="code"></code></pre>
  </div>
  <script>{highlight.min.js inlined}</script>
  <script src="ms-appx:///Assets/editor.js"></script>
</body>
</html>
```

### editor.js — global `editor` object

**Content functions**:
- `getText()` — reads from contentEditable, normalizes `<div>` → `\n`, strips HTML
- `setText(text, cursorPos)` — sets content, re-highlights, positions cursor
- `insertText(text)` — insert at cursor via `document.execCommand('insertText')`
- `deleteSelection()` — delete selected range
- `backspace()` — delete char before cursor (handles line boundary)
- `deleteChar()` — delete char after cursor
- `insertNewline()` — insert `\n` + auto-indent (match previous line indent)

**Cursor functions**:
- `moveCursorLeft(n)`, `moveCursorRight(n)`, `moveCursorUp(n)`, `moveCursorDown(n)`
- `jumpWordLeft()`, `jumpWordRight()` — word boundaries
- `jumpParagraphUp()`, `jumpParagraphDown()` — blank line boundaries
- `jumpPageUp()`, `jumpPageDown()` — viewport height
- `getCursorPosition()` → `{line, col, offset}`
- `setCursorPosition(offset)` — via `window.getSelection()` + Range API

**Selection functions**:
- `toggleSelectionAnchor()` — set/extend selection
- `hasSelection()`, `getSelectionRange()`

**Syntax functions**:
- `setLanguage(lang)` — set code class for hljs
- `refreshHighlight()` — re-run `hljs.highlightBlock()`, debounced 150ms
- `setHighlightEnabled(bool)` — toggle for large files

**Undo/redo**:
- `undo()`, `redo()`, `canUndo()`, `canRedo()` — JS undo stack
- Bounded by file size tier (unlimited for <512KB, 50 ops for 512KB–2MB)

**Other**:
- `toggleWordWrap()`, `isWordWrapEnabled()`
- `updateLineNumbers()` — rebuild line-number gutter
- `getLineCount()`, `getCharCount()`
- `isDirty()`, `setDirty(bool)`

**Edge case handling**:
- MutationObserver: normalize `<div>` → `\n` (EdgeHTML Enter behavior)
- Paste interception: `e.preventDefault()`, plain text only
- Grapheme-aware cursor: `for...of` for code point iteration

### Test gate
- Temporarily load `editor.html` in the existing `PreviewCodeView` WebView (hack)
- Verify: text renders with syntax highlighting, line numbers visible
- Invoke JS: `editor.getText()` returns content, `moveCursorRight(5)` works
- Build succeeds

---

## Step 3 — TextEditorOverlay.xaml(.cs)

**Files**: `Controls/TextEditorOverlay.xaml`, `Controls/TextEditorOverlay.xaml.cs`
**Depends on**: Step 1 + Step 2
**Estimated scope**: XAML ~120 lines, code-behind ~500 lines

### XAML structure

```xml
<UserControl Visibility="Collapsed">
  <Grid Background="#E60D1117">
    <!-- WebView: editor surface -->
    <WebView x:Name="EditorWebView" />

    <!-- Hidden TextBox: system keyboard bridge -->
    <TextBox x:Name="HiddenTextBox"
             Height="0" Opacity="0"
             Visibility="Collapsed" />

    <!-- Notification bar: file size warnings -->
    <Border x:Name="NotificationBar" Visibility="Collapsed"
            Background="#FF4444" ...>
      <TextBlock x:Name="NotificationText" ... />
    </Border>

    <!-- Footer legend: button labels -->
    <StackPanel x:Name="FooterLegend" Orientation="Horizontal"
                HorizontalAlignment="Center" VerticalAlignment="Bottom">
      <!-- Dynamic labels based on mode -->
    </StackPanel>
  </Grid>
</UserControl>
```

### Code-behind key members

```csharp
public sealed partial class TextEditorOverlay : UserControl
{
    // State
    public bool IsOpen => Visibility == Visibility.Visible;
    public bool IsInputMode { get; private set; } // Navigate vs Input
    private bool _isDirty;
    private string _filePath;
    private Encoding _detectedEncoding;
    private FileTier _fileTier;
    private string _lineEnding;

    // Lifecycle
    public void Show(string filePath)  // Load file, build HTML, show overlay
    public void Close()                // Confirm dirty, hide

    // Input routing (called by MillerColumnsPage)
    public void HandleDPadUp/Down/Left/Right()
    public void HandleButton(VirtualKey key)  // A/B/X/Y/Start/Select
    public void HandleStick(float x, float y) // scroll viewport

    // Mode switching
    private void EnterInputMode()  // TextBox.Focus + CoreInputView.TryShow
    private void ExitInputMode()   // CoreInputView.TryHide + WebView focus

    // Sync
    private void SyncTextBoxToWebView()  // TextChanged → InvokeScriptAsync
    private void SyncWebViewToTextBox()  // read from JS, update TextBox

    // Save
    private async Task HandleSave()
}
```

### Build HTML dynamically

In `Show(filePath)`:
1. Call `TextEditorService.LoadAsync(filePath)` → get text, encoding, size, tier
2. Build HTML string: inline highlight.min.js + editor.js + highlight-aco.css (Consolas mono)
3. Set language via `GetHighlightLang(extension)`
4. Call `EditorWebView.NavigateToString(html)`
5. After navigation completes, invoke `editor.setText(text, 0)` to load content
6. Configure tier: `editor.setHighlightEnabled(tier == FullEdit)`

### System keyboard integration

```
CoreInputView.TryShow(CoreInputViewKind.Gamepad)  // primary
InputPane.GetForCurrentView().TryShow()            // fallback
```

TextBox ↔ WebView sync:
1. TextBox.TextChanged fires
2. Read TextBox.Text + TextBox.SelectionStart
3. InvokeScriptAsync: `editor.setText(text, cursorPos)`
4. Debounce to 50ms for fast typing

TextBox.KeyDown intercepts:
- GamepadB → ExitInputMode()
- GamepadX → backspace
- GamepadY → insert newline
- GamepadMenu (Start) → HandleSave()
- GamepadView (Select) → ExitInputMode()

### Test gate
- Open overlay with hardcoded text content
- D-pad moves cursor (verify via `editor.getCursorPosition()`)
- Select toggles Input mode (TextBox gets focus)
- B/X/Y work in Navigate mode
- Start saves (verify file written)
- B with changes → confirmation dialog appears
- Build succeeds

---

## Step 4 — MillerColumnsPage integration

**File**: `Controls/MillerColumnsPage.xaml.cs` (edits)
**Depends on**: Step 3
**Estimated scope**: ~50 lines of edits

### Changes

1. **XAML**: Add `<controls:TextEditorOverlay x:Name="TextEditorOverlayControl" />`

2. **Priority chain** — add `TextEditorOverlayControl.IsOpen` checks BEFORE all existing
   checks in:
   - `OnDPadUp()` → `if (TextEditorOverlayControl.IsOpen) { TextEditorOverlayControl.HandleDPadUp(); return; }`
   - `OnDPadDown()` → same pattern
   - `OnDPadLeft()` → same pattern
   - `OnDPadRight()` → same pattern
   - `OnConfirm()` → `TextEditorOverlayControl.HandleButton(GamepadA)`
   - `OnBack()` → `TextEditorOverlayControl.HandleButton(GamepadB)`
   - `OnContextMenu()` → `if (TextEditorOverlayControl.IsOpen) return;`
   - `OnSettings()` → `if (TextEditorOverlayControl.IsOpen) return;`
   - `OnLeftStickMove()` → `TextEditorOverlayControl.HandleStick(x, y)`
   - `OnRightStickMove()` → scroll viewport

3. **IsMediaFullscreen** property → include `TextEditorOverlayControl.IsOpen`

4. **New handler**: `HandleEditAsync(FileEntry entry)` — calls
   `TextEditorOverlayControl.Show(entry.FullPath)`

### Test gate
- Call `HandleEditAsync` from debug menu or hardcoded path
- Editor opens, D-pad navigates, B closes
- No regression in column navigation when editor is closed
- Build succeeds

---

## Step 5 — FileActionSheet "Edit" action

**File**: `Controls/FileActionSheet.xaml.cs` (edits)
**Depends on**: Step 4
**Estimated scope**: ~20 lines of edits

### Changes

1. **Enum**: Add `Edit` to `FileAction`

2. **Icon**: Add `private static readonly string ActionEdit = "fileactionsheet-edit-48.png";`
   - Reuse existing generic text icon or create new asset

3. **ShowAsync()**: In the `else` block (non-drive, non-archive, non-archive-root),
   add "Edit" action for text files:
   ```csharp
   if (FilePreviewService.IsTextFile(System.IO.Path.GetExtension(entry.Name)))
   {
       actions.Add(new ActionItem
       {
           Action = FileAction.Edit,
           Label = "Edit",
           IconPath = IconBase + ActionEdit,
           LabelBrush = accent
       });
   }
   ```

4. **MillerColumnsPage.ShowFileActionSheetAsync()**: Add case:
   ```csharp
   case FileAction.Edit:
       await HandleEditAsync(entry);
       break;
   ```

### Test gate
- Open Y-menu on .txt file → "Edit" appears
- Open Y-menu on .zip file → "Edit" does NOT appear
- Select "Edit" → editor opens with file content
- Build succeeds

---

## Step 6 — Core editing features

**Files**: `TextEditorOverlay.xaml.cs`, `editor.js` (edits)
**Depends on**: Step 5
**Estimated scope**: ~150 lines of edits

### Dirty state tracking

- In editor.js: every mutation (insertText, backspace, deleteChar, insertNewline)
  calls `editor.setDirty(true)`
- In TextEditorOverlay: `editor.isDirty()` checked on B press
- Dirty → show confirmation dialog ("Save changes? Yes/No/Cancel")
- Save resets dirty: `editor.setDirty(false)`

### Save flow

1. Start button → `HandleSave()`
2. `editor.getText()` via InvokeScriptAsync
3. `TextEditorService.SaveAsync(filePath, text)`
4. Show "Saved" toast (200ms)
5. `editor.setDirty(false)`

### File size tiers

- On `Show(filePath)`: determine tier from file size
- **<512KB**: full edit + highlight + unlimited undo
- **512KB–2MB**: no highlight (`editor.setHighlightEnabled(false)`), undo limited to 50,
  notification bar: "Syntax highlighting disabled — file too large"
- **>2MB**: read-only (`contentEditable=false`), notification bar: "File too large to edit ({size})",
  Save disabled, keyboard doesn't open

### Unsaved changes confirmation

- B with dirty=true → show AlertDialogControl: "Save changes?"
- "Yes" → save + close
- "No" → discard + close
- "Cancel" → stay in editor

### Language detection

- Extract `GetHighlightLang()` from MillerColumnsPage to `TextEditorService` (or duplicate)
- Pass to `editor.setLanguage(lang)` on load

### Test gate
- Open .txt, type text, Start saves → file updated on disk
- Open .txt, type, B → "Save changes?" dialog
- Open .txt, type, B, "No" → closes without saving
- Open 600KB .cs file → no highlight, notification bar visible
- Open 3MB file → read-only, can't type
- Full workflow: open → edit → save → reopen → verify content
- Build succeeds

---

## Step 7 — Polish

**Files**: various (small edits)
**Depends on**: Step 6
**Estimated scope**: ~100 lines total

### Word wrap toggle

- LB + RB pressed within 200ms → `editor.toggleWordWrap()`
- Brief toast: "Word wrap: ON/OFF"
- Track in TextEditorOverlay: `_wrapToggleTimestamp` for both bumpers

### Right stick scroll

- In `HandleStick(float x, float y)`: same deadzone + speed as DeleteConfirmDialog
- Scroll the WebView's content via JS: `window.scrollBy(dx, dy)`
- Does NOT move cursor

### Footer legend update

- **Navigate mode**: `[A] Anchor  [B] Backspace  [X] Delete  [Y] Enter  [LB+RB] Wrap  [Start] Save  [Select] Keyboard`
- **Input mode**: `[Virtual Keyboard Active]  [B] Close KB  [X] Backspace  [Y] Enter  [Start] Save`
- Update labels on mode switch

### Status bar

- Show encoding, cursor line/col, line count, file size
- Read from JS: `editor.getCursorPosition()`, `editor.getLineCount()`
- Update on cursor move (debounced)

### Line endings

- Detect on load: `TextEditorService.DetectLineEnding(text)`
- On save: convert `\n` back to detected style
- Edge case: mixed endings → use most common, log warning

### Test gate (final)

- Full workflow: open → edit with keyboard → word wrap toggle → save → exit
- Full workflow: open → edit → B → cancel → still editing
- Full workflow: open → edit → B → discard → back to columns
- Regression: browse files, preview images, play audio/video, navigate archives
- Build succeeds
- No new warnings beyond pre-existing ones

---

## Asset Requirements

| Asset | When | Notes |
|---|---|---|
| `Assets/editor.html` | Step 2 | WebView template |
| `Assets/editor.js` | Step 2 | JS editor core |
| `Assets/Views/FileActionSheet/fileactionsheet-edit-48.png` | Step 5 | Edit action icon |
| `Controls/TextEditorOverlay.xaml(.cs)` | Step 3 | New overlay control |

---

## Risk Areas

1. **EdgeHTML contentEditable quirks**: Enter produces `<div>` not `\n`, backspace
   at line boundary behaves unexpectedly. Mitigated by MutationObserver + normalization.

2. **InvokeScriptAsync overhead**: Each C#→JS call crosses process boundary.
   Batch operations, use parameters instead of repeated single calls.

3. **TextBox ↔ WebView sync latency**: Fast typing can outpace sync.
   Debounced at 50ms. Visual lag acceptable for MVP.

4. **System keyboard API availability**: `CoreInputViewKind.Gamepad` requires
   Windows SDK 26100.3624+. Fallback to `InputPane.TryShow()`.

5. **highlight.js performance on Xbox**: EdgeHTML JS engine slower than Chrome.
   512KB threshold may need lowering if testing shows lag.
