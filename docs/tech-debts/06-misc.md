# Miscellaneous Debt

Findings from the Aug 2026 re-audit that don't fit the other categories.

## FIXED: Debug Flags Left ON in Release-Bound Debug Config

**File:** `XFiles/XFiles.csproj`

```xml
<DefineConstants>DEBUG;TRACE;NETFX_CORE;WINDOWS_UWP;AUDIO_ANALYSIS;VUMETER_DEBUG;AUDIO_LEVEL_DEBUG</DefineConstants>
```

`VUMETER_DEBUG` (per-tick audio levels) and `AUDIO_LEVEL_DEBUG` (per-quantum FFT data)
were left enabled after visualizer work. They spam the log at ~60fps on every playback
and mask real log lines.

**Fixed (Aug 2026):** both removed from `DefineConstants` (`AUDIO_ANALYSIS` kept).
`docs/LOGGING.md` already documents the flags as OFF by default — now matches reality.

## MEDIUM: Hardcoded Certificate Password

**File:** `XFiles/XFiles.csproj`

```xml
<PackageCertificatePassword>dev</PackageCertificatePassword>
```

Plaintext password committed to the repo. For a sideloaded Developer Mode app this is
low-risk (the cert is self-signed for the local developer), but it's a smell and the
same key would not be acceptable for any Store/enterprise signing.

**Fix:** drive from environment variable or build config; keep the local dev cert
private key out of any CI packaging if it ever runs in a shared runner.

## FIXED: `Prefer32Bit` in x64 Configs

**File:** `XFiles/XFiles.csproj`

`<Prefer32Bit>true</Prefer32Bit>` appeared in the x64 configuration blocks. For a
x64-targeted UWP app this is inert but misleading.

**Fixed (Aug 2026):** removed from both x64 configs.

## FIXED: Non-English Comments

**Files:** `Visualizers/Visualizers/RetroOscilloscopeVisualizer.cs` — multiple comments
were in Portuguese with mojibake accents (`C�PIA SEGURA...`, etc.).

**Fixed (Aug 2026):** all translated to English (the codebase is English-only).

## FIXED: Dead Debug Overlay Code

- `Controls/DebugOverlay.xaml(.cs)` — control existed but was never instantiated
  (`App.xaml.cs:110` had the instantiation commented out).
- `Log.Screen` (`ScreenLogger`) was still constructed and attached as a Serilog sink
  (`Log.cs:59,64`) but had no consumer — the in-app log viewer reads session files instead.

**Fixed (Aug 2026):** deleted `DebugOverlay.xaml(.cs)`, `ScreenLogger.cs`, the `Log.Screen`
property + `WriteTo.Sink(Screen)` registration, and the commented instantiation in
`App.xaml.cs`. Serilog File/Debug sinks remain. LogsPage reads files, not the screen buffer.
