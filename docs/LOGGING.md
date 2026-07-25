# Logging

Centralized logging via Serilog, wrapped in the `Log` static class (`XFiles/Log.cs`).

## Log Levels

| Level | Wrapper | When to use | Examples |
|---|---|---|---|
| **Info** | `Log.Info()` | Significant user-facing actions, service lifecycle, navigation | App start/suspend/resume, directory navigation, file operations (copy/move/delete), controller connected, archive opened, editor save |
| **Dbg** | `Log.Dbg()` | Internal state, intermediate processing, diagnostic data | Cache hit/miss, HTTP status, metadata merge/scoring, dialog close results, "displaying N entries", file encoding/tier |
| **Verb** | `Log.Verb()` | High-volume events, per-input, per-tick data | Gamepad button/DPad presses, keyboard input, pointer coordinates, selection changes |
| **Warn** | `Log.Warn()` | Recoverable failures, degraded operation | File access errors, HTTP failures, parse errors, fallback paths |
| **Err** | `Log.Err()` | Unrecoverable failures, exceptions | Unhandled exceptions, navigation failures, scan failures |

## Debug Flags (`#if`)

High-volume hot paths are guarded by preprocessor flags. **All OFF by default.** Enable by appending to `DefineConstants` in `XFiles.csproj` (Debug config):

```xml
<DefineConstants>DEBUG;TRACE;NETFX_CORE;WINDOWS_UWP;XRAY_ENABLED;GAMEPAD_POLL_DEBUG</DefineConstants>
```

| Flag | File | What it guards |
|---|---|---|
| `GAMEPAD_POLL_DEBUG` | `GamepadInputService.cs` | Raw stick/button state per tick, DPAD state, DPAD repeat events |
| `VUMETER_DEBUG` | `VuMeterBar.xaml.cs` | Per-tick audio sample levels |
| `AUDIO_LEVEL_DEBUG` | `AudioLevelService.cs` | Per-quantum audio processing data |
| `POINTER_DEBUG` | `TextEditorOverlay.xaml.cs` | Pointer/mouse coordinates per move |
| `EDITOR_JS_DEBUG` | `TextEditorOverlay.xaml.cs` | JS log lines pulled from editor |
| `ID3_PARSE_DEBUG` | `Id3Tag.cs` | Per-frame ID3 tag parsing |

When disabled, these log calls are **compiled out entirely** — zero runtime cost.

## Architecture

```
Log.Verb/Dbg/Info/Warn/Err
  → Serilog Logger (file + debug + screen sinks)
```

- **Screen sink**: `ScreenLogger` (in-memory ring buffer, shown in DebugOverlay).
- **File sink**: Rolling daily, keeps last 5 files, stored in `ApplicationData.Current.LocalFolder/logs/`.
- **Output format**: `[Timestamp Level] Message`
- **Caller info**: Embedded in message templates (e.g. `Log.Info("DirectoryScanner.Scan: ...")`). No auto-detection — caller prefix is part of the log message string.

## Rules

1. Never swallow exceptions — always `Log.Warn()` or `Log.Err()` them.
2. Log directory scans, input events, navigation, file operations, app lifecycle.
3. Use structured logging templates: `Log.Info("Loading {Path}", path)` — never string interpolation.
4. Prefix log messages with class/method name: `Log.Dbg("MetadataGuesser.Detect: ...")`.
5. Operation logs (open, close, edit, copy, move, paste, play, stop) use `Log.Info()`.
6. Default level is Information.
