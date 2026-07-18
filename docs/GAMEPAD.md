# Gamepad — Mapping and Navigation Contract

## Input Source

`Windows.Gaming.Input.Gamepad` (native UWP API, no SDL — unlike `dosbox-pure-uwp`,
which uses SDL_GameController + UWP fallback because it also runs on non-UWP platforms via
the shared libretro core). Here we don't have that cross-platform requirement, so we use
the native API directly, which is simpler:

```csharp
var gamepads = Gamepad.Gamepads;        // IReadOnlyList<Gamepad>
var reading = gamepad.GetCurrentReading(); // GamepadReading { Buttons, LeftThumbstickX/Y, ... }
```

`Gamepad.GamepadAdded` / `Gamepad.GamepadRemoved` events handle hotplug (controller
connected/disconnected at runtime).

## GamepadInputService — Responsibilities

1. Poll every frame/tick (via `CompositionTarget.Rendering` or `DispatcherTimer` at ~16ms).
2. Compare current vs previous `GamepadButtons` (bitwise) → detect "JustPressed" (rising
   edge) and "JustReleased".
3. D-pad: repeat-while-held, with initial delay (e.g. 400ms) then fast repeat
   (e.g. 100ms) — same logic as `dosbox_uwpMain.cpp` from the sibling project.
4. Left Thumbstick: mapped to the same events as D-pad when beyond a deadzone
   (~0.5), allowing navigation with the analog stick as well.
5. Translate raw state into semantic events and pass to the active `INavigable` (the
   `ColumnNavigator`, see `ARCHITECTURE.md`).

## `INavigable` Contract

```csharp
public interface INavigable
{
    void OnDPad(bool up);       // true = up/left (previous), false = down/right (next)
    void OnDPadLeft();          // go up a level (equivalent to Back)
    void OnDPadRight();         // go down a level (equivalent to Confirm on folder)
    void OnConfirm();           // A button
    void OnBack();              // B button
    void OnContextMenu();       // Y button
    void OnPageUp();            // LB
    void OnPageDown();          // RB
}
```

Same "shape" used by `FrontendMenu`/`FileBrowser` in `dosbox-pure-uwp` (see exploration
report in `docs/frontend`/`docs/filebrowser` in that repo) — intentional decision to
keep the proven pattern.

## Button Table (MVP)

| Physical Button | Semantic Event | X-Files Action |
|---|---|---|
| D-pad Up / Left Stick Up | `OnDPad(up: true)` | move selection up in Current column (wrap-around) |
| D-pad Down / Left Stick Down | `OnDPad(up: false)` | move selection down in Current column (wrap-around) |
| D-pad Left / Left Stick Left | `OnDPadLeft()` | go up a level (equivalent to B) |
| D-pad Right / Left Stick Right | `OnDPadRight()` | enter selected folder (equivalent to A on folder) |
| A | `OnConfirm()` | folder → drill-in; file → contextual default action (e.g. open with associated app) |
| B | `OnBack()` | go up a level; if already at root, no effect (or exit app, to be defined) |
| Y | `OnContextMenu()` | opens `FileActionSheet` over selected item |
| X | (reserved) | toggle preview mode (e.g. force hex) — post-MVP |
| LB | `OnPageUp()` | scroll one page up in Current column |
| RB | `OnPageDown()` | scroll one page down in Current column |
| Start/Menu | (reserved) | open settings/theme — post-MVP |

## Navigation Rules (ported from dosbox-pure-uwp FileBrowser.cpp)

- **Wrap-around**: moving down on the last item returns to the first; moving up on
  the first item goes to the last.
- **Scroll-follows-selection**: if the selected index goes outside the visible window (up or
  down), the list scrolls automatically to keep it visible, with a "look ahead" margin
  (e.g. 2-3 items before scrolling at the limit).
- **Empty/separator entry skipping**: if visual separators exist in the
  list in the future (e.g. "Folders" / "Files" headers), navigation must automatically skip
  them (same `do { } while` logic seen in `FileBrowser.cpp:706-733`).

## Input Edge Cases

- No controller connected: display empty-state message ("Connect a controller") instead of
  crashing — `GamepadInputService` must expose observable `IsControllerConnected`.
- Multiple controllers connected: MVP uses only `Gamepad.Gamepads[0]` (first
  detected). Multi-user support is in the backlog.
- Debounce: analog stick deadzone threshold (0.5) prevents unwanted navigation
  "chattering" from stick drift.
