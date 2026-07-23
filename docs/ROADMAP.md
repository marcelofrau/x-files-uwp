# Roadmap — Implementation Phases

Each phase has a testable deliverable and explicit completion criteria. Do not advance to
the next phase without closing the current phase's criteria (or explicitly documenting why
it was deferred, in `DECISIONS.md`).

---

## Phase 0 — Scaffold (THIS COMMIT)

- [x] Folder structure created (`Controls/`, `Navigation/`, `FileSystem/`,
      `ContextMenu/`, `Theming/`, `Assets/`).
- [x] Complete documentation in `docs/`.
- [x] `AGENTS.md` at project root.
- [x] "Bare" UWP C# project (App.xaml, MainPage.xaml placeholder, csproj, manifest with
      correct capabilities and TargetDeviceFamily) — no business logic.
      **Not built/validated** (done in Linux environment) — real build/SDK validation
      happens in Phase 1, on first opening in a Windows machine.

**Completion criteria**: project opens in Visual Studio (Windows) without structural errors,
even if not yet built (real build is only possible on Windows).

---

## Phase 1 — Skeleton + Xbox Deploy Validated

- [x] Open the project on a Windows machine with Visual Studio, resolve any
      `csproj`/SDK version adjustments that couldn't be validated in Linux.
- [x] Local build (desktop) working — `MainPage` shows a simple placeholder (e.g. centered
      "X-Files" text).
- [x] Enable Developer Mode on Xbox (see `docs/DEPLOY-XBOX.md`).
- [x] Deploy "hello world" to Xbox via Visual Studio (Remote Machine) or Device Portal.

**Completion criteria**: app opens on real Xbox, screen appears, no crash. No features
yet — just build/deploy pipeline validation.

---

## Phase 2 — GamepadInputService + INavigable Contract

- [x] Implement `GamepadInputService` (polling, edge-detection, dpad-repeat).
- [x] Implement `INavigable` (interface) + a "mock" implementation (e.g. a simple
      counter on screen reacting to D-pad/A/B) to validate the input pipeline without
      real file UI yet.
- [x] Unit tests (or documented manual tests) for: edge-detection (correct JustPressed
      even while holding button), wrap-around, analog deadzone.
      `docs/PHASE2-TESTS.md` — 8 scenarios documented (edge, hold-repeat, direction
      change, stick deadzone, buttons, phantom inputs, disconnect/reconnect, simultaneous).

**Completion criteria**: on real Xbox, D-pad/analog increments/decrements a counter on
screen, with repeat working when holding a button, no phantom input.
**Status**: code implemented + manual tests documented. Hardware validation
pending (run `docs/PHASE2-TESTS.md` on Xbox).

---

## Phase 3 — DirectoryScanner + Single Functional Column

- [x] `FileEntry` model.
- [x] `DirectoryScanner` with P/Invoke (`FindFirstFileExFromAppW` + `GetLogicalDrives`).
- [x] Single navigable column (`Controls/ColumnListView`) listing drives at root and
      navigating into real folders (no preview, no parent/preview columns yet).
- [x] Sorting (folders before files, alphabetical) implemented and visually
      confirmed.

**Completion criteria**: on real Xbox, navigates any folder on a connected USB drive,
enters/exits subfolders with D-pad/A/B, no crash in empty or permission-denied folders.
**Status**: implemented and validated on real Xbox. Loading indicator added for
USB spin-up latency. XrayLib adapted for UWP (Console sink removed).

---

## Phase 4 — 3 Miller Columns + Transitions

- [x] `ColumnNavigator` implementing `INavigable`, controlling 3-column state
      (Parent/Current/Preview as concept — Preview still shows only folder listing
      in this phase, no text/image).
- [x] XAML layout with 3 `Grid.ColumnDefinition` and reactive binding.
- [x] Transition when entering/exiting folders (content swap of 3 columns, with or without
      simple animation).
- [x] Simplified GamepadInputService: D-pad Up/Down managed natively by
      ListView; GamepadInputService only manages action buttons (A/B/Y/LB/RB/LT/RT)
      and left stick.

**Completion criteria**: complete folder navigation using 3 columns, preview in the right
column always shows the content of the item selected in the middle column,
without waiting for confirmation.
**Status**: implemented and validated on real Xbox. Double-fire bug resolved by
delegating Up/Down navigation to native ListView. RetroListView overrides OnKeyDown
to block native PageUp/PageDown.

---

## Phase 5 — PreviewPane (text, image, syntax highlighting)

- [x] `FilePreviewService` using Win32 P/Invoke (`CreateFileFromAppW` + `ReadFile`).
- [x] Text preview: 256KB limit, plain text (.txt/.log/.out/.err) in TextBlock.
- [x] Image preview: Win32 read bytes → TaskCompletionSource → UI thread BitmapImage.
- [x] Small image cap: images < 256px get max 4x upscale (prevents ICO/tiny PNG stretch).
- [x] Syntax highlighting: highlight.js v9.18.5 (ES5, EdgeHTML compatible) inlined.
- [x] Aco theme CSS (vibrant: pink keywords, green strings, yellow numbers, blue types).
- [x] Inconsolata font (103KB) embedded as base64 in WebView CSS.
- [x] SVG rendering in WebView as `<img src="data:image/svg+xml;base64,...">`.
- [x] WebView DefaultBackgroundColor=#111111 (no white flash).
- [x] Right analog stick scrolls preview (vertical + horizontal).
- [x] ScrollViewer with horizontal scrollbar for plain text.
- [x] No word wrap in syntax highlighted code (horizontal scroll).
- [x] Column widths adjusted: 20*:35*:45*.
- [x] Hidden/system files filtered in DirectoryScanner.
- [x] Full keyboard nav: Enter/Back/Left/Right/Up/Down/PageUp/PageDown/Home/End.
- [x] Mac startup chime on app launch (volume 40%).
- [x] Test images at H:\tests\images\ (41 files: PNG/JPG/BMP/GIF/WebP/SVG, various sizes).

**Completion criteria**: navigating over a `.txt` shows truncated content; navigating over
a common image shows thumbnail; rapidly navigating between multiple files doesn't freeze the
UI or produce unhandled exceptions. Syntax highlighting working for 40+ languages.
**Status**: implemented. SVG rendering and syntax highlighting validated.

---

## Phase 6 — ArchiveBrowser (zip/7z/rar)

- [x] Integrate `SharpCompress`.
- [x] `ArchiveBrowser` implemented, `IsArchive` detection in `DirectoryScanner`.
- [x] Drill-in on compressed archive treated as folder (reusing `ColumnNavigator`).
- [x] Preview of text/image entries inside archive (via `OpenEntryStream`).
- [ ] Validate performance on large files (> 100MB) — `Win32FileStream` provides
      streaming access; full-file-in-memory avoided. Awaiting user confirmation.

**Completion criteria**: open a test `.zip`, `.7z` and `.rar`, navigate through internal
entries, preview working for at least one text file and one image inside each format.

---

## Phase 7 — FileActionSheet + FileOperations

- [x] `FileActionSheet` (context menu triggered by Y), styled per
      `docs/UI-THEMING.md`.
- [x] `ConfirmDialog` (Delete confirmation, gamepad-navigable).
- [x] `InputDialog` (Rename text input, gamepad-navigable).
- [x] `FileOperations` backend (Win32 P/Invoke Copy/Move/Rename/Delete/Extract).
- [x] Rename + Delete wired end-to-end (with confirmation + refresh).
- [ ] Actions: Open with (`Launcher.LaunchFileAsync`), Copy, Move, Extract
      (backend done, UI destination picker pending).
- [ ] "Choose destination folder" flow for Copy/Move/Extract reusing column navigation
      (special mode, see `FILEBROWSER.md`).

**Completion criteria**: all actions work end-to-end on real Xbox, with no
keyboard/mouse needed, including destination folder selection.

---

## Phase 8 — Theme/Polish

- [x] `RetroTheme.xaml` — Blades-inspired dark theme (xb-vault adapted) with orange accent.
- [x] Oxanium font (Regular + Bold) imported from xb-vault.
- [x] MillerColumnsPage layout: header (logo + version), 3 columns, footer (legend + status).
- [x] Footer legend with gamepad button images (A/B/X/Y/Start).
- [x] Gamepad buttons in ConfirmDialog and InputDialog (A=confirm, B=cancel).
- [x] File browser icons using orange folder theme.
- [x] X = refresh current directory.
- [x] Start/Select = settings placeholder (logs, not yet functional).
- [x] A on file = opens FileActionSheet (same as Y).
- [ ] `Theming/AppTheme.cs` reading/writing `x-files-theme.json`.
- [ ] Empty states (no controller connected, empty folder, etc.) with handled
      messages/visuals.
- [ ] UX pass: light column transition animations, consistent loading/error
      visual feedback.

**Completion criteria**: "done" criteria from `docs/SPEC.md` fully met.

---

## Phase 9 — Media (Audio + Video + VU Meter)

- [x] Built-in audio player via `AudioLevelService` (AudioGraph + MediaFoundation).
- [x] VU meter: 26-bar spectrum analyzer with peak hold, green→yellow→red gradient.
- [x] ID3 tag reading (title, artist, album art) from external USB drives.
- [x] Audio playback in preview pane (A to play/pause, LB/RB to seek, Right Analog for volume).
- [x] Fullscreen audio mode with album art, metadata, transport controls.
- [x] Next/previous track navigation in fullscreen.
- [x] Stream-based fallback via `MediaSourceAudioInputNode` for Xbox external drives
      where `StorageFile` APIs fail (`E_ACCESSDENIED`).
- [x] Video playback via MediaPlayer in preview pane and fullscreen.
- [x] Unified AudioGraph for fullscreen audio (no duplicate MediaPlayer).
- [x] OSD icons (play/pause/next/prev) as white-on-transparent PNGs.
- [x] Welcome panel, About overlay, refresh action in Y-menu.

**Completion criteria**: audio plays on Xbox from external USB drives, VU meter animates,
fullscreen works with play/pause/seek/next/volume, no double-play.

**Status**: implemented and validated on real Xbox.

---

## Phase 10 — Audio Visualizers (Win2D + HLSL Shaders)

- [ ] Win2D NuGet package added to csproj.
- [ ] `AudioLevelService`: expose `Magnitudes[512]`, `Waveform[512]`, `Beat` (0–1).
- [ ] Beat detector implemented (energy threshold + exponential decay).
- [ ] `AudioData` snapshot struct.
- [ ] `IAudioVisualizer` interface.
- [ ] `AudioVisualizerBase` (CanvasCustomControl base class, 60fps timer, shader pipeline).
- [ ] `VisualizerRegistry` (registry of available visualizers).
- [ ] RadialSpectrum visualizer + HLSL shader.
- [ ] Waveform visualizer + HLSL shader.
- [ ] Plasma visualizer + HLSL shader.
- [ ] `AudioFullscreenMode` enum + cycling logic in `MillerColumnsPage`.
- [ ] Select button remapped in audio fullscreen → `OnSelectVisualizer()`.
- [ ] Mode OSD overlay (name text, 2s auto-fade).
- [ ] XAML layout: toggle between `FsDefaultContent` and `FsVisualizerCanvas`.
- [ ] `INavigable.OnSelectVisualizer()` contract.
- [ ] Transport controls remain visible over visualizer (overlay).
- [ ] `docs/DECISIONS.md` updated with ADR-009.
- [ ] `docs/AUDIO-VISUALIZERS.md` created (complete documentation).
- [ ] `docs/AUDIO-VISUALIZATION.md` updated with shader visualizers section.

**Phase 10A — Foundation**: Win2D + AudioLevelService data + base framework + 1 visualizer.
**Phase 10B — Integration**: Select cycling + OSD + layout toggle.
**Phase 10C — Remaining Visualizers**: Waveform + Plasma.
**Phase 10D — Polish & Xbox Validation**: Hardware test + performance tuning.

**Completion criteria**: Select cycles through Default + 3 visualizers in audio fullscreen.
Visualizers react to audio in real-time. VU meter (Default) unchanged. No regressions in
audio playback, track navigation, or transport controls. 60fps sustained on Xbox.

---

## Phase 11 — Metadata Guesser (MusicBrainz + Cache)

- [x] `Id3Tag.cs` extended with `TCON` (genre), `TYER`/`TDRC` (year), `TRCK` (track#), `TLEN` (duration) frames.
- [x] `TrackMetadata` model — unified metadata with completeness scoring and merge semantics.
- [x] `FilenameParser` — extracts artist/album/track# from path patterns (`Artist - Album/01 - Title.mp3`).
- [x] `MusicBrainzProvider` — async search via MusicBrainz API with 1 req/s IP rate limit.
- [x] `MusicBrainzProvider.FetchCoverArtAsync()` — album art from Cover Art Archive.
- [x] `MetadataCache` — SQLite database (`metadata.db`) via `sqlite-net-pcl`, 90-day TTL.
  - Cover art stored as BLOB (single I/O per lookup).
  - `ClearAsync()` for cache management from Settings page.
  - Indexed queries on artist/title/album.
- [x] `MetadataCacheDb.cs` — `MetadataCacheEntry` SQLite model.
- [x] `MetadataMatch` — match result with confidence score (0.0–1.0) and source tracking.
- [x] `MetadataGuesser` — orchestrator: ID3 → filename → cache → MusicBrainz → merge → UI.
- [x] Integration in `MediaPreviewControl` and `MillerColumnsPage` (fullscreen audio).
- [x] `SettingsPage` — Clear Metadata Cache with entry count display.
- [x] `XFilesSettings` — wrapper for `ApplicationData.Current.LocalSettings`.
- [x] `XFiles.csproj` updated with all new files.

**Completion criteria**: selecting an audio file shows metadata from ID3 + filename inference.
With internet, MusicBrainz enriches missing fields (genre, year, track#, cover art).
Results cached locally; second access instant. Offline degrades gracefully to local data only.

---

## Assets & Icons

Asset process documented in `docs/ASSETS-GUIDE.md`. Skill available at
`.opencode/skills/assets-icons/SKILL.md`. Summary:

- Always PNG icons, source: `F:\workspace\icons8-personal-set`
- Naming: `{viewname}-{descriptor}-{size}.png` (lowercase, hyphens)
- Organization: `XFiles/Assets/Views/{ViewName}/` per view
- XAML reference: `ms-appx:///Assets/Views/{ViewName}/{filename}`
- Mandatory registration in `XFiles.csproj` as `<Content>`

Each phase that introduces a new view should include its icons in that phase.

---

## Post-MVP Backlog (not yet phased)

- Network browsing (SMB/UNC).
- Hex dump preview for binaries.
- Simple text editing.
- Multiple simultaneous users/gamepads.
- Deep nested zips with real streaming (no intermediate `MemoryStream`).
- Password-protected file support.
- Localization (i18n) — currently docs/specs in Portuguese, but UI may start in English or
  support both — decision to make when the time comes.
