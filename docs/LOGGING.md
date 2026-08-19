---
layout: default
title: Logging
---
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

High-volume hot paths are guarded by preprocessor flags. **Most are OFF by default.**
Enable by appending to `DefineConstants` in `XFiles.csproj` (Debug config):

```xml
<DefineConstants>DEBUG;TRACE;NETFX_CORE;WINDOWS_UWP;AUDIO_ANALYSIS;FTP_CONNECT_DEBUG;BATCH_DEBUG</DefineConstants>
```

| Flag | File | What it guards | Default |
|---|---|---|---|
| `AUDIO_ANALYSIS` | `AudioLevelService.cs` | FFT processing + VU meter | ON |
| `FTP_CONNECT_DEBUG` | `FtpSession.cs` | Connect diagnostics (DNS, timing, capability probe) | ON (dev) |
| `BATCH_DEBUG` | `MillerColumnsPage.FileOps.cs` | Batch operation Verb logs (cancels, no items) | ON (dev) |
| `GAMEPAD_POLL_DEBUG` | `GamepadInputService.cs` | Raw stick/button state per tick, DPAD state, DPAD repeat events | OFF |
| `GAMEPAD_INPUT_DEBUG` | `GamepadInputService.cs` | Button dispatch traces (input routing, Y long-press) | OFF |
| `VUMETER_DEBUG` | `VuMeterBar.xaml.cs` | Per-tick audio sample levels | OFF |
| `AUDIO_LEVEL_DEBUG` | `AudioLevelService.cs` | Per-quantum audio processing data, GC region logs | OFF |
| `POINTER_DEBUG` | `TextEditorOverlay.xaml.cs` | Pointer/mouse coordinates per move | OFF |
| `EDITOR_JS_DEBUG` | `TextEditorOverlay.xaml.cs` | JS log lines pulled from editor | OFF |
| `EDITOR_INPUT_DEBUG` | `TextEditorOverlay.xaml.cs` | Gamepad input tracing in text editor | OFF |
| `ID3_PARSE_DEBUG` | `Id3Tag.cs` | Per-frame ID3 tag parsing | OFF |
| `DEBUG_EDITOR_INPUT` | `TextEditorOverlay.xaml.cs` | Full gamepad input tracing (legacy, prefer `EDITOR_INPUT_DEBUG`) | OFF |

> **Dev flags** (`FTP_CONNECT_DEBUG`, `BATCH_DEBUG`) are ON during active
> development and will be compiled out before release.

When disabled, these log calls are **compiled out entirely** — zero runtime cost.

## Architecture

```
Log.Verb/Dbg/Info/Warn/Err
  → Serilog Logger (file + debug + screen sinks)
```

- **Screen sink**: `ScreenLogger` (in-memory ring buffer). The `DebugOverlay`
  that renders it is currently **disabled** (`App.xaml.cs:110`, commented out) —
  the in-app log viewer (`LogsPage`, Start → Logs) reads archived session files
  instead.
- **File sink**: Rolling, one file per app session, keeps the last **5** archived
  sessions (`MaxArchivedSessions = 5`), stored in
  `ApplicationData.Current.LocalFolder/logs/`. Archived sessions are gzip-compressed
  (`.log.gz`) to save storage. `GetAllSessionsContent()` reads both `.log` and
  `.log.gz` files for the log viewer / sharing (truncated to `MaxShareBytes`).
- **Output format**: `[Timestamp Level] Message`
- **Caller info**: Embedded in message templates (e.g. `Log.Info("DirectoryScanner.Scan: ...")`). No auto-detection — caller prefix is part of the log message string.

## Rules

1. Never swallow exceptions — always `Log.Warn()` or `Log.Err()` them.
2. Log directory scans, input events, navigation, file operations, app lifecycle.
3. Use structured logging templates: `Log.Info("Loading {Path}", path)` — never string interpolation `$"..."`.
4. Prefix log messages with class/method name: `Log.Dbg("MetadataGuesser.Detect: ...")`.
5. Operation logs (open, close, edit, copy, move, paste, play, stop) use `Log.Info()`.
6. Default level is Information.
7. FTP/SFTP/SMB operation errors that are control flow (NotFound, AccessDenied on probe) use `Log.Dbg()`, not `Log.Warn()`. Only unexpected failures use `Warn`.
8. `FtpSession.TraceSink` routes FluentFTP protocol traces to `Log.Dbg` (success) or `Log.Warn` (error). The sink sanitizes passwords from FluentFTP messages automatically.
9. `FtpSession.TraceFilter` scales FluentFTP verbosity with the app level: at Info, only Warn+ traces pass through; at Verbose, all traces pass.

## Log Management

- **Rotation**: On each `Init()`, the previous session's `xfiles.log` is renamed to
  `xfiles-{timestamp}-prev.log` and immediately gzip-compressed to `.log.gz`.
- **Cleanup**: The last 5 archived sessions are kept; older files (`.log` and `.log.gz`)
  are deleted on startup.
- **Compression**: All archived logs are gzip-compressed. The one-time migration
  (`CompressExistingLogs()`) compresses any pre-existing uncompressed archives from
  older versions.
- **Clear Logs**: Available in Settings → Clear Data → Clear Logs. Deletes all
  `xfiles-*.log` and `xfiles-*.log.gz` files (not the active log).

## Settings Migration

Settings schema versioning (`SettingsVersion` in SQLite) tracks one-time upgrade
steps:

| Version | Changes |
|---|---|
| 0 → 1 | Force log level to Info; compress existing uncompressed log archives |

Migration runs in `App.OnLaunched()` after `Log.Init()`. Each step bumps the version
so it only runs once.
