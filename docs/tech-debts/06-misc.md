# Miscellaneous Debt

Findings from the Aug 2026 re-audit that don't fit the other categories.

## HIGH: Debug Flags Left ON in Release-Bound Debug Config

**File:** `XFiles/XFiles.csproj`

```xml
<DefineConstants>DEBUG;TRACE;NETFX_CORE;WINDOWS_UWP;AUDIO_ANALYSIS;VUMETER_DEBUG;AUDIO_LEVEL_DEBUG</DefineConstants>
```

`VUMETER_DEBUG` (per-tick audio levels) and `AUDIO_LEVEL_DEBUG` (per-quantum FFT data)
were left enabled after visualizer work. They spam the log at ~60fps on every playback
and mask real log lines.

**Fix:** remove both from `DefineConstants` (keep `AUDIO_ANALYSIS`). Docs claim all debug
flags are OFF by default (`docs/LOGGING.md`) — the doc has been corrected to flag this.

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

## MEDIUM: `Prefer32Bit` in x64 Configs

**File:** `XFiles/XFiles.csproj`

`<Prefer32Bit>true</Prefer32Bit>` appears in the x64 configuration blocks. For a
x64-targeted UWP app this is inert but misleading.

**Fix:** remove the `Prefer32Bit` lines from x64 configs (or set to `false`).

## LOW: Non-English Comment

**File:** `Visualizers/Visualizers/RetroOscilloscopeVisualizer.cs:50`

One comment is in Portuguese (the rest of the codebase is English). Cosmetic — fix
opportunistically.

## LOW: Dead Debug Overlay Code

- `Controls/DebugOverlay.xaml(.cs)` — control exists but is never instantiated
  (`App.xaml.cs:110` comment: `// rootGrid.Children.Add(new DebugOverlay(Log.Screen));`).
- `Log.Screen` (`ScreenLogger`) is still constructed and attached as a Serilog sink
  (`Log.cs:59,64`) but has no consumer since the overlay is disabled — the in-app log
  viewer reads session files instead.

**Fix:** per `docs/SETTINGS-EXPANSION.md` Part 1 — delete `DebugOverlay.xaml(.cs)`,
`ScreenLogger`, and the `Log.Screen` plumbing. Low risk (LogsPage reads files, not
the screen buffer).
