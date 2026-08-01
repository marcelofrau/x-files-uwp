# Roadmap — Implementation Status & Backlog

## Current Status

**Released v1.2.0.** The MVP plus post-MVP features are shipped: 3-column Miller
navigation with live preview, audio player + 29 visualizers, video player with subtitles
and track switching, archive browsing (zip/7z/rar), full file operations (copy/move/
rename/delete/extract/create-zip), text editor, PDF viewer, ROM preview, metadata guesser
(MusicBrainz + SQLite cache), QR file sharing, batch mode, favorites, settings, and log
viewer.

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
`AudioLevelService` exposes magnitudes/waveform/beat; `AudioFullscreenMode` (30 modes);
**29 visualizers** registered in `VisualizerRegistry`; `PostProcessPipeline`; Select
cycling + long-press picker; ADR-009 + `docs/AUDIO-VISUALIZERS.md`. **Done.**

### Phase 11 — Metadata Guesser (MusicBrainz + Cache)
ID3 frames, `FilenameParser`, `MusicBrainzProvider` (1 req/s rate limit), `DeezerProvider`,
SQLite `MetadataCache` (30-day TTL, cover art BLOB), `MetadataGuesser` orchestrator,
Settings page cache management. **Done.**

### Phase 12 — Text Editor
`TextEditorService` (Win32 I/O, encoding detection, 4MB tier), `TextEditorOverlay`
(WebView + contentEditable + hidden TextBox system-keyboard bridge), two-mode input,
dirty-state save confirmation, `Assets/editor.js`. **Done.** See `docs/text-editor/`.

---

## Remaining Backlog

### Features (not yet phased)

- [ ] **Theme selector / JSON config** — `Theming/AppTheme.cs` was planned (Phase 8) but
      never built; theme is hardcoded to `BladeTheme.xaml`. Full plan in
      `docs/SETTINGS-EXPANSION.md`.
- [ ] **Settings expansion** — deadzone presets, D-pad speed, editor tab size, settings
      page section grouping. See `docs/SETTINGS-EXPANSION.md`.
- [ ] **Windows file shares (SMB/UNC)** — feasibility assessed in `docs/FILE-SHARES.md`
      (deferred, not implemented).
- [ ] **Hex dump preview** for binary files.
- [ ] **Deep nested zips** with true streaming (no intermediate `MemoryStream`).
- [ ] **Password-protected archive support**.
- [ ] **Multiple simultaneous users/gamepads**.
- [ ] **Localization (i18n)** — UI is English-only; docs were originally written in
      Portuguese. Decision pending.
- [ ] **Archive perf validation on >100MB files** (Phase 6 open item).

### Tech Debt / Quality

See `docs/tech-debts/` for the full audit and remediation plan. High-level:

- [ ] Decompose `MillerColumnsPage` god object (4373 lines, complexity 916)
- [x] `SubtitleDetector` — `System.IO` replaced with P/Invoke (Aug 2026)
- [x] `PlasmaVisualizer` shader load — blocking `.GetResult()` removed (async load, Aug 2026)
- [x] Dead debug overlay code removed (`DebugOverlay`, `ScreenLogger`, Aug 2026)
- [x] `RunContinuationsAsynchronously` applied to all 19 `TaskCompletionSource` (Aug 2026)
- [x] `VUMETER_DEBUG`/`AUDIO_LEVEL_DEBUG` turned OFF in Debug config (Aug 2026)
- [x] Debug flags/`Prefer32Bit`/PT comments cleaned (Aug 2026)
- [ ] Expand unit test coverage (`tests/`, MSTest, linked-source, net8.0) — **P0 done (45 tests)**, extend to `TextEditorService`/`MetadataCache`

---

## Past "Post-MVP" Notes

- Compression (create zips) was originally out of scope — **shipped** (batch + single-file
  create-zip).
- Text editing was originally out of scope — **shipped** (Phase 12).
- QR sharing was originally out of scope — **shipped** (v1.2.0).
- ROM preview and PDF viewer were unplanned — **shipped** post-MVP.
