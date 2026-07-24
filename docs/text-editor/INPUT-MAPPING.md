# Text Editor — Input Mapping

## Mode Overview

The editor has two mutually exclusive input modes:

| Mode | Trigger | Input Source | Gamepad Role |
|---|---|---|---|
| **Navigate** | Default / Select closes keyboard | GamepadInputService polling | D-pad, stick, buttons → cursor + editing |
| **Input** | Select opens system keyboard | UWP system virtual keyboard | D-pad → navigate keyboard grid; buttons → shortcuts |

Mode state is managed by `TextEditorOverlay.IsInputMode` (boolean).

## Navigate Mode

Cursor and text manipulation via gamepad only. System keyboard is hidden.

### D-Pad / Left Stick

| Input | Action | JS Function | Repeat |
|---|---|---|---|
| D-pad Up | Move cursor left 1 char | `moveCursorLeft(1)` | Yes (initial 300ms, repeat 80ms) |
| D-pad Down | Move cursor right 1 char | `moveCursorRight(1)` | Yes |
| D-pad Left | Move cursor up 1 line | `moveCursorUp(1)` | No (single fire) |
| D-pad Right | Move cursor down 1 line | `moveCursorDown(1)` | No |
| Left Stick Left | Jump word left | `jumpWordLeft()` | Yes (with repeat) |
| Left Stick Right | Jump word right | `jumpWordRight()` | Yes |

**Design note**: D-pad directions feel inverted (Up = left, Down = right) because
the Miller column navigation already uses Up/Down for list navigation, and the user
expects Up = "go back in text" (leftward) and Down = "go forward" (rightward).
This matches the horizontal reading direction of text files. If testing reveals this
feels wrong, swap the mapping.

### Face Buttons

| Button | Action | JS Function | Notes |
|---|---|---|---|
| A | Toggle selection anchor | `toggleSelectionAnchor()` | First press = set anchor, second = extend selection to current cursor. Visual highlight shows selected range. If text is selected, subsequent B/X/Y operate on selection. |
| B | Backspace | `backspace()` | If selection active → delete selection instead |
| X | Delete (forward) | `deleteChar()` | If selection active → delete selection instead |
| Y | Enter (newline) | `insertNewline()` | Inserts newline + auto-indents (matches previous line indent) |

### Bumpers & Triggers

| Input | Action | JS Function | Notes |
|---|---|---|---|
| LB | Jump paragraph up | `jumpParagraphUp()` | Move to previous blank line / paragraph boundary |
| RB | Jump paragraph down | `jumpParagraphDown()` | Move to next blank line / paragraph boundary |
| LT | Page up | `jumpPageUp()` | Move up one viewport height |
| RT | Page down | `jumpPageDown()` | Move down one viewport height |
| LB + RB (simultaneous) | Toggle word wrap | `toggleWordWrap()` | Both must be pressed within 200ms. Shows brief toast notification. |

### Menu Buttons

| Button | Action | Notes |
|---|---|---|
| Start | Save file | Save with current encoding, show "Saved" toast. If no changes, do nothing. |
| Select/View | Enter Input mode | Opens system keyboard, switches to Input mode |

## Input Mode

System keyboard is visible. Gamepad D-pad navigates the keyboard grid.
TextBox has focus and receives all keyboard events.

### TextBox Key Interception

The hidden TextBox intercepts gamepad buttons via `KeyDown`:

| Button | Action | Notes |
|---|---|---|
| A | (handled by system keyboard) | Character selection on keyboard grid |
| B | Exit Input mode | Close system keyboard, return to Navigate mode |
| X | Backspace | `TextBox.SelectedText = ""` or `TextBox.Text = ...RemoveAt(...)` |
| Y | Enter/Newline | Insert newline at cursor position |
| Start | Save | Same as Navigate mode Start |
| Select/View | Exit Input mode | Close system keyboard, return to Navigate mode |
| LB | Move cursor left in TextBox | `TextBox.SelectionStart--` |
| RB | Move cursor right in TextBox | `TextBox.SelectionStart++` |
| LT | Jump word left | Find previous word boundary |
| RT | Jump word right | Find next word boundary |

### System Keyboard Behavior

The UWP system keyboard handles:
- Character input (letters, numbers, symbols)
- Shift/caps via keyboard UI
- Backspace/delete via keyboard UI
- Arrow keys via keyboard UI

The system keyboard's own D-pad navigation is separate from our gamepad D-pad.
On Xbox, the system keyboard uses the gamepad natively (D-pad to navigate keys,
A to select character). Our TextBox intercepts the resulting key events.

### Mode Transition

```
Navigate mode ──[Select]──→ Input mode
                               │
                               ├─ TextBox.Focus()
                               ├─ CoreInputView.TryShow(Gamepad)
                               └─ footer: "B=Close KB"

Input mode ───[Select/B]──→ Navigate mode
                               │
                               ├─ CoreInputView.TryHide()
                               ├─ WebView.Focus()
                               └─ footer: "A=Anchor B=Bksp X=Del Y=Enter"
```

## Cursor Direction Mapping (Navigate Mode)

The D-pad-to-cursor mapping follows text reading direction, not spatial direction:

```
Physical D-pad        Text cursor action
    ↑                 ← (left, previous char)
    ↓                 → (right, next char)
    ←                 ↑ (up, previous line)
    →                 ↓ (down, next line)
```

**Rationale**: In horizontal text, "forward" is rightward. D-pad Down = "go forward"
= rightward in text. D-pad Up = "go backward" = leftward. D-pad Left/Right map to
vertical movement (up/down lines) because that's the remaining direction.

**Alternative (spatial)**: If users prefer spatial mapping (Up = up in text,
Right = right in text), this can be made configurable in a future version.
The current mapping should be tested on real hardware before committing.

## Footer Legend

The footer at the bottom of the editor updates based on mode:

**Navigate mode footer:**
```
[A] Anchor   [B] Backspace   [X] Delete   [Y] Enter
[LB+RB] Wrap   [Start] Save   [Select] Keyboard
```

**Input mode footer:**
```
[Virtual Keyboard Active]
[B] Close KB   [X] Backspace   [Y] Enter   [Start] Save
```

## Right Stick

| Input | Action | Notes |
|---|---|---|
| Right Stick Up/Down | Scroll editor vertically | Same speed as preview pane scroll (40.0) |
| Right Stick Left/Right | Scroll editor horizontally | Same speed as preview pane scroll |

Right stick scrolls the viewport without moving the cursor. Useful for reviewing
content away from the current cursor position.
