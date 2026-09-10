---
layout: default
title: Roadmap — Implementation Status & Backlog
---
# Roadmap — Implementation Status & Backlog

## Current Status

**Released v1.6.0** (tag `v1.6.0.1485`). Dev line: **1.7.0** (Properties dialog, stream-based
network extraction). The MVP plus post-MVP features are shipped: 3-column Miller
navigation with live preview, **network file browsing** (SMB/FTP/FTPS/SFTP/WebDAV) with
streaming preview and remote operations, audio player + 31 visualizers, video player with
subtitles and track switching, archive browsing (zip/7z/rar), full file operations (copy/
move/rename/delete/extract/create-zip), text editor, PDF viewer, ROM preview,
chiptune/tracker playback, metadata guesser (MusicBrainz + SQLite cache), QR file sharing,
file/folder **Properties dialog** (stats, compression visual, permissions), download-from-URL,
batch mode, favorites, settings, and log viewer.

Below: which phases are closed, what's still open, and the remaining backlog.

---

## Completed Phases

### Phase 0 — Scaffold
Project skeleton, docs, capabilities manifest. **Done.**

### Phase 1 — Skeleton + Xbox Deploy Validated
Build + deploy "hello world" pipeline validated on real Xbox. **Done.**

### Phase 2 — GamepadInputService + INavigable Contract
Polling, edge-detection, dpad-repeat, `INavigable` contract. Manual test procedures in
`docs/PHASE2-TESTS.md`. **Done** (hardware-validated).

### Phase 3 — DirectoryScanner + Single Functional Column
`FileEntry`, P/Invoke scanning (`FindFirstFileExFromAppW` + `GetLogicalDrives`), sorting.
**Done** (hardware-validated, USB spin-up loading indicator added).

### Phase 4 — 3 Miller Columns + Transitions
`ColumnNavigator`, 3-column layout, Up/Down delegated to native ListView. **Done.**

### Phase 5 — PreviewPane (text, image, syntax highlighting)
`FilePreviewService`, 256KB text cap, image thumbnails, highlight.js v9.18.5, SVG in
WebView, right-stick scrolling. **Done.**

### Phase 6 — ArchiveBrowser (zip/7z/rar)
SharpCompress, drill-in as virtual folder, text/image preview inside archives.
**Done.** *(Open item: validate perf on >100MB archives — streaming via `Win32FileStream`
is in place, awaiting real-world confirmation.)*

### Phase 7 — FileActionSheet + FileOperations
Y-button context menu, `FileOperations` (Win32 P/Invoke), rename/delete wired, plus
copy/move/extract/zip/favorite/share/paste with destination picker and batch operations.
**Done.**

### Phase 8 — Theme/Polish
`BladeTheme.xaml` (custom ControlTemplate/Style resource dictionary, green accent),
Oxanium font, footer legend, gamepad button icons, refresh, welcome/about overlays.
**Done.** *(Open items: `AppTheme.cs` JSON theme loader never implemented — theme is
XAML-only; empty states and column transition animation polish deferred.)*

### Phase 9 — Media (Audio + Video + VU Meter)
AudioGraph playback, stream fallback for USB drives, 26-bar VU meter, ID3 tags,
fullscreen audio with transport, video playback, subtitle support, audio track switching.
**Done** (hardware-validated).

### Phase 10 — Audio Visualizers (Win2D + HLSL Shaders)
`AudioLevelService` exposes magnitudes/waveform/beat; `AudioFullscreenMode` (31 modes);
**31 visualizers** registered in `VisualizerRegistry`; `PostProcessPipeline`; Select
cycling + long-press picker; ADR-009 + `docs/AUDIO-VISUALIZERS.md`. **Done.**

### Phase 11 — Metadata Guesser (MusicBrainz + Cache)
ID3 frames, `FilenameParser`, `MusicBrainzProvider` (1 req/s rate limit), `DeezerProvider`,
SQLite `MetadataCache` (30-day TTL, cover art BLOB), `MetadataGuesser` orchestrator,
Settings page cache management. **Done.**

### Phase 12 — Text Editor
`TextEditorService` (Win32 I/O, encoding detection, 4MB tier), `TextEditorOverlay`
(WebView + contentEditable + hidden TextBox system-keyboard bridge), two-mode input,
dirty-state save confirmation, `Assets/editor.js`. **Done.** See `docs/text-editor/`.

### Phase 13 — Chiptune/Tracker Playback
RetroAudio native DLL (static game-music-emu 0.6.5 + libopenmpt 0.8.7 + aosdk engine_psf
+ lazyusf 1.2 + zlib), 44+ extensions (console chips nsf/spc/vgm/sid/gbs + tracker
mod/xm/s3m/it + PSF/USF), multi-subsong drill-in, archive-embedded playback (.rsn→.spc),
renewal
render-to-cached-WAV feeding the existing AudioGraph path, next/prev/seek/mute in the
audio player, Papirus `audio-x-generic` icons. **Done.** See ADR-011.

### Phase 14 — Network File Browsing (v1.6.0)
`INetworkFileSystemProvider` abstraction with four protocol implementations — **SMB**, **FTP/FTPS**
(implicit + explicit TLS), **SFTP**, **WebDAV**. Network root + locations column, PasswordVault
credentials, streaming preview (text/image/audio/video/chiptune) without full download, remote
file operations (copy/move/rename/delete/batch/create-folder), write-permission detection,
breadcrumb protocol icons, refresh + download-from-URL overlay. **Done** (v1.6.0). See
`docs/network-files/`. Archive drill-in on large remote archives defers to a one-time cache
download with progress + cancel (v1.6.1 refinements: quiet FTP trace, stream-based remote
extract).

### Phase 15 — Properties Dialog (v1.7.0)
Y-menu **Properties** for files, folders, archives, and network entries. Pure helpers linkable
into tests: `DirectoryStatsCalculator` (incremental folder walk with progress + cancel),
`MediaMetadataProbe` (MP3/FLAC/WAV/MP4 container parsing, image dimensions, PDF page count),
`ArchiveBrowser.ComputeSubtreeStats` (in-memory). UI: live pie chart of folder vs drive, 
WinRAR-style **isometric 3D compression cube**, timestamps, attributes, read-only/hidden 
permissions sub-dialog with recursive apply, text statistics, compression ratio rows.
**Done** (dev 1.7.0, not yet released).

---

## Remaining Backlog

### Features (not yet phased)

- [ ] **Theme selector / JSON config** — `Theming/AppTheme.cs` was planned (Phase 8) but
      never built; theme is hardcoded to `BladeTheme.xaml`. Full plan in
      `docs/SETTINGS-EXPANSION.md`.
- [ ] **Settings expansion** — deadzone presets, D-pad speed, editor tab size, settings
      page section grouping. See `docs/SETTINGS-EXPANSION.md`.
- [x] **Windows file shares (SMB/UNC)** — **shipped** (network browsing via
      SMB/FTP/FTPS/SFTP/WebDAV, `docs/network-files/`, v1.6.0).
- [x] **Properties dialog** — **shipped** (dev 1.7.0): folder stats + pie, archive
      compression cube, media/image/pdf/text metadata, permissions.
- [x] **Download from URL** — **shipped** (v1.6.0): overlay WebView + provider rewrites.
- [ ] **Hex dump preview** for binary files.
- [ ] **Deep nested zips** with true streaming (no intermediate `MemoryStream`).
- [ ] **Password-protected archive support**.
- [ ] **Multiple simultaneous users/gamepads**.
- [ ] **Localization (i18n)** — UI is English-only; docs were originally written in
      Portuguese. Decision pending.
- [ ] **Archive perf validation on >100MB files** (Phase 6 open item).

### Tech Debt / Quality

See `docs/tech-debts/` for the full audit and remediation plan. High-level:

- [x] Decompose `MillerColumnsPage` god object (4960 → 8 partial files + 3 pure classes,
      Aug 2026)
- [x] `SubtitleDetector` — `System.IO` replaced with P/Invoke (Aug 2026)
- [x] `PlasmaVisualizer` shader load — blocking `.GetResult()` removed (async load, Aug 2026)
- [x] Dead debug overlay code removed (`DebugOverlay`, `ScreenLogger`, Aug 2026)
- [x] `RunContinuationsAsynchronously` applied to all 19 `TaskCompletionSource` (Aug 2026)
- [x] `VUMETER_DEBUG`/`AUDIO_LEVEL_DEBUG` turned OFF in Debug config (Aug 2026)
- [x] Debug flags/`Prefer32Bit`/PT comments cleaned (Aug 2026)
- [x] Extract pure helpers from `MillerColumnsPage` (`Formatting`, `HighlightRenderer`,
      `RomCoverProvider`) + unit tests (Aug 2026)
- [ ] Expand unit test coverage (`tests/`, MSTest, linked-source, net8.0) — **P0 done
      (516 passing, 12 skipped)**, extend to `TextEditorService`/`MetadataCache`

---

## Past "Post-MVP" Notes

- Compression (create zips) was originally out of scope — **shipped** (batch + single-file
  create-zip).
- Text editing was originally out of scope — **shipped** (Phase 12).
- QR sharing was originally out of scope — **shipped** (v1.2.0).
- ROM preview and PDF viewer were unplanned — **shipped** post-MVP.
