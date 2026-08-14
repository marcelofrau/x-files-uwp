# BGM — Implementation Checklist

Status legend: `[ ]` pending · `[~]` in progress · `[x]` done.

## Phase 1 — Docs (this folder)

- [x] `README.md` — overview, status, doc index
- [x] `SPEC.md` — user stories + acceptance criteria
- [x] `ARCHITECTURE.md` — service/picker/settings design
- [x] `IMPLEMENTATION.md` — this checklist

## Phase 2 — Pure logic + tests

- [x] `XFiles/FileSystem/MusicFormatClassifier.cs` (standard-audio ∪ chiptune
      exts, volume helpers, no UWP types)
- [x] Link into `tests/XFiles.Tests.csproj` (`Compile Include` + `Link`)
- [x] Unit tests: classifier classification, volume cycle, percent→gain
      (7 tests, 188 total)

## Phase 3 — `BackgroundMusicService`

- [x] `XFiles/Audio/BackgroundMusicService.cs` — own AudioGraph
- [x] `AudioFileInputNode` + `LoopCount = 1` + `FileCompleted` → 2.5s gap →
      `Seek(0)` + `Start()` (generation-guarded)
- [x] `SetTrackAsync` (standard → copy as-is; chiptune → render WAV → copy)
- [x] `Pause()` / `RequestResume()` (10s cooldown) / `Resume()` / `SetEnabledAsync`
- [x] `SetVolume(float)` — `OutgoingGain` + persist
- [x] `InitializeAsync()` — settings-driven startup
- [x] Register in `XFiles.csproj`

## Phase 4 — File picker (generic picker dialog)

- [x] `PickerMode` enum (`Folder`/`File`, renamed from `FolderBrowserDialogMode`)
- [x] `ShowAsync(path, mode, fileExtensions = null)` — null = all files
- [x] File mode: list dirs + files, omit "Move Here", A = select file
- [x] Generic icon fallback (`file-generic-24.png` for unknown extensions)
- [x] BGM passes `MusicFormatClassifier.MusicExtensions` as the filter
- [x] Move (Folder mode) unchanged

## Phase 5 — Settings UI

- [x] 3 menu items: Background Music (toggle), Choose Music File (picker),
      BGM Volume (cycle)
- [x] Spinner overlay during chiptune pick-time render
- [x] `BgmPickerControl` instance on `SettingsPage` + gamepad forwarding
- [x] Settings keys: `BgmEnabled`, `BgmFileName`, `BgmVolume`
- [x] Icons `settingspage-bgm-48.png`, `settingspage-bgm-pick-48.png`,
      `settingspage-volume-48.png` (personal Icons8 set, registered in csproj)

## Phase 6 — Player coexistence + startup

- [x] MillerColumnsPage `UpdateDisplayRequest` edges → Pause / RequestResume
- [x] App.xaml.cs fire-and-forget `InitializeAsync()`

## Phase 7 — Verification

- [x] Build (VS2026 MSBuild, x64 Debug) — **1.8.0.1288**
- [x] `dotnet test` (tests/XFiles.Tests.csproj) — 188 passed
- [x] Package msix — `AppPackages\XFiles_1.8.0.1288_x64_Debug_Test`
- [ ] Desktop smoke: pick mp3 → loop+gap, volume, pause/resume cooldown
- [ ] Xbox test: two graphs, chiptune pick (spinner), loop gap, resume
      cooldown, media regression pass

## Log

| Date | Note |
|---|---|
| 2026-08-14 | Feature planned, docs created. Not implemented yet. |
| 2026-08-14 | Implemented all code (service, picker, settings, ducking, startup). Build 1.8.0.1288, 188 tests green. Awaiting desktop/Xbox validation. |
