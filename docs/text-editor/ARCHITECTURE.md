# Text Editor — Architecture

## Component Overview

```
┌──────────────────────────────────────────────────────────────────┐
│                      TextEditorOverlay                          │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  WebView (EdgeHTML)                                      │   │
│  │  ┌──────────────────────────────────────────────────┐    │   │
│  │  │  contentEditable div                             │    │   │
│  │  │  ┌──────┬─────────────────────────────────────┐  │    │   │
│  │  │  │ Line │  <pre><code> (highlight.js output)  │  │    │   │
│  │  │  │ nums │  with visible cursor (caret)         │  │    │   │
│  │  │  └──────┴─────────────────────────────────────┘  │    │   │
│  │  └──────────────────────────────────────────────────┘    │   │
│  │  Cursor position read/written via JS functions            │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                  │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  TextBox (hidden, Height=0, Opacity=0)                   │   │
│  │  Receives system keyboard input when visible              │   │
│  │  TextChanged → sync to WebView via JS                     │   │
│  │  KeyDown → intercept gamepad buttons                       │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                  │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  Notification Bar (TextBlock)                             │   │
│  │  "Syntax highlighting disabled — file too large"          │   │
│  │  "File too large to edit — read-only mode"                │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                  │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  Footer Legend (StackPanel)                               │   │
│  │  A=Select  B=Backspace  X=Delete  Y=Enter  Start=Save    │   │
│  └──────────────────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────────────────┘
```

## Data Flow — Navigate Mode

```
GamepadInputService (33ms poll)
  │
  ├─ detects button press
  │
  ▼
MillerColumnsPage.OnDPadUp/Down/Left/Right()
  │
  ├─ priority check: TextEditorOverlay.IsOpen? → YES
  │
  ▼
TextEditorOverlay.HandleDPadUp/Down/Left/Right()
  │
  ├─ translates gamepad direction to cursor action
  │   D-pad Up    → JS: moveCursorLeft(1)
  │   D-pad Down  → JS: moveCursorRight(1)
  │   D-pad Left  → JS: moveCursorUp(1)
  │   D-pad Right → JS: moveCursorDown(1)
  │
  ▼
WebView.InvokeScriptAsync("editor", ["moveCursor", ...])
  │
  ▼
JavaScript: editor.js manipulates contentEditable cursor
```

## Data Flow — Input Mode (System Keyboard)

```
User presses Select/View button
  │
  ▼
TextEditorOverlay.EnterInputMode()
  │
  ├─ TextBox.Focus(FocusState.Programmatic)
  ├─ CoreInputView.TryShow(CoreInputViewKind.Gamepad)
  │   (fallback: InputPane.TryShow())
  │
  ▼
System keyboard appears on screen
  │
  User types on virtual keyboard
  │
  ▼
TextBox.TextChanged event fires
  │
  ├─ TextEditorOverlay.SyncTextBoxToWebView()
  │   ├─ reads TextBox.Text (new content)
  │   ├─ reads cursor position from TextBox.SelectionStart
  │   ├─ invokes JS: editor.setText(newText, cursorPos)
  │   └─ JS: updates contentEditable div, re-highlights
  │
  ▼
WebView updates with highlighted text
```

## Data Flow — Save

```
User presses Start button
  │
  ▼
TextEditorOverlay.HandleSave()
  │
  ├─ reads full text from WebView via JS: editor.getText()
  ├─ TextEditorService.SaveAsync(path, content, encoding)
  │   ├─ CreateFile2FromAppW → WriteFile (Win2 P/Invoke)
  │   ├─ always saves as UTF-8 with BOM
  │   └─ reports success/failure
  │
  ├─ success: dirty = false, close editor, refresh preview
  └─ failure: show error dialog with retry/cancel
```

## Key Components

### TextEditorOverlay (XAML + code-behind)

- Fullscreen overlay, same pattern as `AudioFullScreenPanel`
- `Visibility=Collapsed` by default, toggled via `Show()`/`Close()`
- Exposes `IsOpen` property for priority chain routing
- Owns WebView, TextBox, NotificationBar, FooterLegend
- Handles all input routing from MillerColumnsPage
- Manages mode state (Navigate vs Input)

### TextEditorService (C#, static)

- File I/O via Win2 P/Invoke (`CreateFile2FromAppW`, `WriteFile`)
- Encoding detection (BOM sniffing + fallback)
- File size tier determination
- No UI logic — pure service

### editor.html (WebView template)

```html
<!DOCTYPE html>
<html>
<head>
  <meta charset="utf-8">
  <style>
    /* Editor layout: line numbers + code area */
    /* Monospace font (Inconsolata, embedded as base64) */
    /* Cursor styling (blinking caret) */
    /* Selection highlighting */
    /* Theme matching BladeTheme.xaml colors */
  </style>
  <style>{highlight-aco.css}</style>
</head>
<body>
  <div id="editor" contenteditable="true" spellcheck="false">
    <div id="line-numbers"></div>
    <pre><code id="code"></code></pre>
  </div>
  <script>{highlight.min.js}</script>
  <script>{editor.js}</script>
</body>
</html>
```

### editor.js (JavaScript)

Core functions exposed to C# via `InvokeScriptAsync`:

```javascript
var editor = {
  // Content
  getText(),                    // returns full text content
  setText(text, cursorPos),     // replaces content, sets cursor
  insertText(text),             // inserts at current cursor
  deleteSelection(),            // removes selected text
  backspace(),                  // delete char before cursor
  deleteChar(),                 // delete char after cursor
  insertNewline(),              // insert \n + re-indent

  // Cursor
  moveCursorLeft(n),
  moveCursorRight(n),
  moveCursorUp(n),
  moveCursorDown(n),
  jumpWordLeft(),
  jumpWordRight(),
  jumpParagraphUp(),
  jumpParagraphDown(),
  jumpPageUp(),
  jumpPageDown(),
  getCursorPosition(),          // returns {line, col, offset}
  setCursorPosition(offset),

  // Selection
  toggleSelectionAnchor(),      // start/extend selection
  hasSelection(),
  getSelectionRange(),

  // Syntax
  setLanguage(lang),            // e.g. "javascript", "csharp"
  refreshHighlight(),           // re-run hljs on content
  setHighlightEnabled(bool),    // toggle for large files

  // Undo
  undo(),
  redo(),
  canUndo(),
  canRedo(),

  // Word wrap
  toggleWordWrap(),
  isWordWrapEnabled(),

  // Line numbers
  updateLineNumbers(),
  getLineCount(),

  // State
  isDirty(),
  setDirty(bool),
  getCharCount(),
  getLineCount(),
};
```

## System Keyboard Integration

### API Selection

Priority order (compile-time + runtime check):

```csharp
// Try newer API first (gamepad-optimized keyboard)
if (ApiInformation.IsTypePresent(
    "Windows.UI.ViewManagement.Core.CoreInputView"))
{
    var inputView = CoreInputView.GetForCurrentView();
    if (inputView.IsKindSupported(CoreInputViewKind.Gamepad))
        inputView.TryShow(CoreInputViewKind.Gamepad);
    else
        inputView.TryShow();  // generic fallback
}
else
{
    // Fallback: InputPane API (10.0.17763.0+)
    InputPane.GetForCurrentView().TryShow();
}
```

### TextBox ↔ WebView Sync

The hidden TextBox serves as a bridge between the system keyboard and the WebView:

1. System keyboard sends key events to the focused TextBox
2. TextBox.Text changes are detected via `TextChanged` event
3. Full text is read from TextBox, pushed to WebView via `InvokeScriptAsync`
4. WebView's `editor.setText()` updates contentEditable div + re-highlights
5. Cursor position is preserved via `editor.setCursorPosition()`

**Performance optimization**: for files > 100KB, debounce sync to 100ms
(with visual indicator that sync is pending).

**Keyboard interception**: TextBox.KeyDown handles gamepad buttons:
- `GamepadA` → mapped to current TextBox key action (or ignored)
- `GamepadB` → close keyboard, return to Navigate mode
- `GamepadX` → backspace
- `GamepadY` → enter/newline
- `GamepadMenu` (Start) → save
- `GamepadView` (Select) → close keyboard, return to Navigate mode

## Integration with MillerColumnsPage

### Priority Chain Addition

Following the existing pattern (audio fullscreen, video fullscreen):

```csharp
// In OnDPadUp():
if (TextEditorOverlay.IsOpen) { TextEditorOverlay.HandleDPadUp(); return; }
// ... existing checks ...

// In OnConfirm():
if (TextEditorOverlay.IsOpen) { TextEditorOverlay.HandleConfirm(); return; }

// In OnBack():
if (TextEditorOverlay.IsOpen) { TextEditorOverlay.HandleBack(); return; }

// In OnContextMenu():
if (TextEditorOverlay.IsOpen) return; // Y does nothing in editor

// In OnSettings():
if (TextEditorOverlay.IsOpen) return; // Start handled by editor
```

### IsMediaFullscreen Extension

```csharp
public bool IsMediaFullscreen => ImageFullScreen.IsOpen || PdfFullScreen.IsOpen
    || VideoFullScreenPanel.Visibility == Visibility.Visible
    || AudioFullScreenPanel.Visibility == Visibility.Visible
    || TextEditorOverlay.IsOpen;  // NEW: editor blocks column nav
```

### Footer Legend Update

When editor opens, footer labels change:
- `A` → "Select" (in Input mode) / "Anchor" (in Navigate mode)
- `B` → "Backspace" (Navigate) / "Close KB" (Input)
- `X` → "Delete"
- `Y` → "Enter"
- `Start` → "Save"
- `Select` → "Toggle KB" (shown in Navigate mode only)

## File I/O

### Reading

```csharp
public static async Task<(string text, Encoding encoding, long size)>
    LoadAsync(string filePath)
```

- Read via `CreateFile2FromAppW` + `ReadFile` (Win2 P/Invoke, same pattern as
  `FilePreviewService`)
- Detect encoding from BOM bytes (see `ENCODING.md`)
- Return text content, detected encoding, and file size
- If file is > 4MB, return read-only flag (`FileTier.ReadOnly`)

### Writing

```csharp
public static async Task<bool> SaveAsync(
    string filePath, string content, Encoding originalEncoding)
```

- Always save as UTF-8 with BOM (`EF BB BF` prefix)
- Use `CreateFile2FromAppW` + `WriteFile` (Win2 P/Invoke)
- Preserve original encoding only if user explicitly chooses "Save As Original"
  (post-MVP — MVP always saves UTF-8)
- Report success/failure via return value

### BOM Handling

UTF-8 BOM: `0xEF 0xBB 0xBF`
UTF-16 LE BOM: `0xFF 0xFE`
UTF-16 BE BOM: `0xFE 0xFF`

On load: detect BOM, use corresponding encoder.
On save: always write UTF-8 BOM (Windows Notepad compatible).
