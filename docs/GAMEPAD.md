---
layout: default
title: Gamepad — Mapping and Navigation Contract
---
# Gamepad — Mapping and Navigation Contract

## Input Source

`Windows.Gaming.Input.Gamepad` (native UWP API, no SDL — unlike `dosbox-pure-uwp`,
which uses SDL_GameController + UWP fallback because it also runs on non-UWP platforms via
the shared libretro core). Here we don't have that cross-platform requirement, so we use
the native API directly.

`Gamepad.GamepadAdded` / `Gamepad.GamepadRemoved` handle hotplug. **Known pitfall**:
a gamepad connected before app start does not fire `GamepadAdded` — `GamepadInputService`
also enumerates `Gamepad.Gamepads` on startup.

## GamepadInputService — Responsibilities

1. Poll every tick via `DispatcherTimer` (~33ms).
2. Compare current vs previous `GamepadButtons` bitmask → detect "JustPressed" (rising
   edge) and "JustReleased".
3. D-pad: repeat-while-held, initial delay (~300ms) then fast repeat (~80ms).
4. Left thumbstick: mapped to D-pad events beyond a deadzone (~0.5). Right thumbstick:
   scroll events for preview pane / editor viewport.
5. Translate raw state into semantic events and forward to `InputRouter` (which dispatches
   to the active `INavigable`).

Long-press / hold detection lives in the service:

| Hold | Detection | Event |
|---|---|---|
| **Y** long-press | `_yHeld` while pressed, fires once | `OnContextMenuLongPress()` → favorite toggle |
| **View** short-press | released before 15 ticks (~500ms) | fullscreen → `OnSelectVisualizer()`; browser → `OnToggleBatch()` |
| **View** long-press | ≥15 ticks while held | fullscreen → `OnSelectVisualizerMenu()` (picker) |
| **LB/RB** held | continuous seek | `OnSeekRepeat(±5)` at ~60ms cooldown after initial `OnSeekBack/Forward()` |

## InputRouter — Overlay Dispatch

`Navigation/InputRouter.cs` routes raw `VirtualKey`/button events to the active overlay
handler instead of the main page:

```csharp
public interface IInputHandler
{
    int Priority { get; }   // higher = wins when multiple active
    bool IsActive { get; }
    bool OnDPad(VirtualKey key, bool isRepeat);
    bool OnButton(VirtualKey key);
}
```

- `InputRouter.Add(handler)` / `Remove(handler)` — overlays register/unregister on show/hide
  (e.g. `TextEditorOverlay`, `StartMenu`, dialogs, fullscreen modes).
- `RouteDPad(...)` / `RouteButton(...)` — walks handlers by priority, first `IsActive`
  handler consumes the event; returns `false` if nothing active (main page handles it).
- `OverlayHandler` — convenience wrapper around `Func<VirtualKey,bool,bool>` delegates so
  overlays don't need to implement the interface by hand.

If no overlay is active, the input falls through to the page itself, which implements
`INavigable`.

## `INavigable` Contract

`Navigation/INavigable.cs` — the page (and each fullscreen surface) implements this to
receive semantic events:

```csharp
public interface INavigable
{
    bool IsMediaFullscreen { get; }
    bool IsMediaPlayerActive { get; }
    void OnDPadUp(bool isRepeat = false);
    void OnDPadDown(bool isRepeat = false);
    void OnDPadLeft();
    void OnDPadRight();
    void OnConfirm();            // A
    void OnBack();               // B
    void OnContextMenu();        // Y
    void OnContextMenuLongPress(); // Y held → favorites
    void OnRefresh();            // X
    void OnPaste();              // paste clipboard (batch/file ops)
    void OnSettings();           // Start
    void OnPageUp();             // LB (browser)
    void OnPageDown();           // RB (browser)
    void OnSeekBack();           // LB (media)
    void OnSeekForward();        // RB (media)
    void OnSeekRepeat(int seconds); // LB/RB held
    void OnTriggerHeld(float leftTrigger, float rightTrigger);
    void OnLeftStickMove(float x, float y);
    void OnRightStickMove(float x, float y);
    void OnScrollHorizontal(double delta);
    void OnScrollVertical(double delta);
    void OnSelectVisualizer();      // View short (media)
    void OnSelectVisualizerMenu();  // View long (media)
    void OnToggleBatch();           // View short (browser)
}
```

## Button Table

| Physical Button | Semantic Event | X-Files Action |
|---|---|---|
| D-pad Up / Left Stick Up | `OnDPadUp` | move selection up in Current column |
| D-pad Down / Left Stick Down | `OnDPadDown` | move selection down |
| D-pad Left / Left Stick Left | `OnDPadLeft` | go up a level (equivalent to B) |
| D-pad Right / Left Stick Right | `OnDPadRight` | enter selected folder |
| A | `OnConfirm` | folder → drill-in; file → contextual default action (play/toggle) |
| B | `OnBack` | go up a level; close fullscreen; exit overlay |
| Y | `OnContextMenu` | open `FileActionSheet` over selected item |
| Y (hold ~500ms) | `OnContextMenuLongPress` | add/remove favorite |
| X | `OnRefresh` | refresh current directory |
| Start/Menu | `OnSettings` | open Start menu (settings, logs, favorites, search, about) |
| View (short) | `OnSelectVisualizer` / `OnToggleBatch` | media: cycle visualizer; browser: toggle batch mode |
| View (long ~500ms) | `OnSelectVisualizerMenu` | media: open visualizer picker |
| LB / RB | `OnPageUp`/`OnPageDown` (browser), `OnSeekBack`/`OnSeekForward` (media) | page up/down; seek 5s |
| LB/RB held | `OnSeekRepeat(±5)` | continuous seek |
| LT / RT | `OnTriggerHeld` | secondary action (see page impl) |
| Right Stick | `OnRightStickMove`/`OnScroll*` | scroll preview / editor / adjust volume (media) |

## In-App Controls Guide

A fullscreen controller mapping reference is built into the app — **Start → Controls
Guide**. It shows the gamepad reference image plus a section per screen
(File Browser, Batch Mode, Audio/Video/Image/PDF Fullscreen, Text Editor, Visualizer
Picker, Media Player). Source: `Controls/ControlsGuideOverlay.xaml(.cs)`. The mappings
rendered there must stay in sync with this table.


## Navigation Rules

- **Wrap-around**: moving down on the last item returns to the first; moving up on the
  first item goes to the last.
- **Scroll-follows-selection**: if the selected index leaves the visible window, the list
  scrolls to keep it visible.
- **Folder-first sorting** with `..` always at top when applicable (see `FILEBROWSER.md`).

## Input Edge Cases

- **No controller connected**: app stays usable, shows a "connect a controller" hint;
  no crash. `GamepadInputService` exposes `IsControllerConnected`.
- **Multiple controllers**: MVP uses `Gamepad.Gamepads[0]` (first detected). Multi-user is
  in the backlog.
- **Analog chattering**: deadzones (main 0.5, stick 0.18, scroll 0.15) prevent drift inputs.
- **Hotplug**: enumerated on startup + `GamepadAdded`/`GamepadRemoved` handled; no phantom
  inputs on connect/disconnect (validated in `docs/PHASE2-TESTS.md`).
