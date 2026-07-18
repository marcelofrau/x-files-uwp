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

## Phase 5 — PreviewPane (text and image)

- [ ] `DataTemplateSelector` to choose between `FolderPreviewTemplate` (already exists from
      Phase 4), `TextPreviewTemplate`, `ImagePreviewTemplate`, `UnsupportedPreviewTemplate`.
- [ ] Truncated reading of text files (configurable KB limit).
- [ ] Async image loading (don't block navigation while loading).
- [ ] Friendly error state for unreadable/permission-denied files.

**Completion criteria**: navigating over a `.txt` shows truncated content; navigating over
a common image shows thumbnail; rapidly navigating between multiple files doesn't freeze the
UI or produce unhandled exceptions.

---

## Phase 6 — ArchiveBrowser (zip/7z/rar)

- [ ] Integrate `SharpCompress`.
- [ ] `IArchiveBrowser` implemented, `IsArchive` detection in `DirectoryScanner`.
- [ ] Drill-in on compressed archive treated as folder (reusing `ColumnNavigator`).
- [ ] Preview of text/image entries inside archive (via `OpenEntryStream`).
- [ ] Validate performance on large files (> 100MB) — decide if cache/streaming
      needs adjustment (document decision in `DECISIONS.md` if engine needs to change).

**Completion criteria**: open a test `.zip`, `.7z` and `.rar`, navigate through internal
entries, preview working for at least one text file and one image inside each format.

---

## Phase 7 — FileActionSheet + FileOperations

- [ ] `FileActionSheet` (context menu triggered by Y), styled per
      `docs/UI-THEMING.md`.
- [ ] Actions: Open with (`Launcher.LaunchFileAsync`), Copy, Move, Rename, Delete,
      Extract.
- [ ] "Choose destination folder" flow for Copy/Move/Extract reusing column navigation
      (special mode, see `FILEBROWSER.md`).
- [ ] Mandatory confirmation before Delete (gamepad-navigable dialog).

**Completion criteria**: all actions work end-to-end on real Xbox, with no
keyboard/mouse needed, including destination folder selection.

---

## Phase 8 — Theme/Polish

- [ ] `Theming/AppTheme.cs` reading/writing `x-files-theme.json`.
- [ ] `RetroTheme.xaml` with all `Style`/`ControlTemplate` finalized (no default
      Windows chrome visible anywhere).
- [ ] Empty states (no controller connected, empty folder, etc.) with handled
      messages/visuals.
- [ ] UX pass: light column transition animations, consistent loading/error
      visual feedback.

**Completion criteria**: "done" criteria from `docs/SPEC.md` fully met.

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
