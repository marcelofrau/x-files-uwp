# Logging

Centralized logging via Serilog, wrapped in the `Log` static class (`XFiles/Log.cs`).

## Log Levels

| Level | When to use | Examples |
|---|---|---|
| **Information** | Significant user-facing actions, service lifecycle, navigation | App start/suspend/resume, directory navigation, file operations (copy/move/delete), controller connected, archive opened, editor save |
| **Debug** | Internal state, intermediate processing, diagnostic data | Cache hit/miss, HTTP status, metadata merge/scoring, dialog close results, "displaying N entries", file encoding/tier |
| **Verbose** | High-volume events, per-input, per-tick data | Gamepad button/DPad presses, keyboard input, pointer coordinates, selection changes |
| **Warning** | Recoverable failures, degraded operation | File access errors, HTTP failures, parse errors, fallback paths |
| **Error** | Unrecoverable failures, exceptions | Unhandled exceptions, navigation failures, scan failures |
| **Fatal** | (reserved) | Not currently used |

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
Log.Verbose/Debug/Information/Warning/Error/Fatal
  → LogContext.PushProperty("Caller", ...)
  → Serilog Logger (file + debug + screen sinks)
```

- **Caller info**: `GetCaller()` uses `new StackTrace(2, false)` to skip the wrapper and find the real caller. Much faster than the old `CallerEnricher` which walked the entire stack on every call.
- **Screen sink**: `ScreenLogger` (in-memory ring buffer, shown in DebugOverlay). Reads `logEvent.Properties["Caller"]` to display caller name.
- **File sink**: Rolling daily, keeps last 5 files, stored in `ApplicationData.Current.LocalFolder/logs/`.
- **Output format**: `[Timestamp Level] [Caller] Message`

## Rules

1. Never swallow exceptions — always `Log.Warning` or `Log.Error` them.
2. Log directory scans, input events, navigation, file operations, app lifecycle.
3. Use structured logging templates: `Log.Information("Loading {Path}", path)` — never string interpolation.
4. Default level is Verbose (all levels enabled). File/Debug sinks filter by configured minimum.
