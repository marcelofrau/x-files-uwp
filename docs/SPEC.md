# Functional Specification — X-Files

## Goal

File browser for Xbox (UWP), fully operable via gamepad, with Miller column navigation
(yazi-style) and live preview, including navigation inside compressed archives
(zip/7z/rar) without needing to extract first.

> **Status: shipped.** The MVP below was completed (v1.0.0) and the product has since
> shipped post-MVP features (audio player + 31 visualizers, native chiptune player
> (43+ formats), background music, text editor, PDF viewer, ROM preview, QR sharing,
> URL download, batch operations, favorites, settings, logs). This document
> keeps the original MVP scope for reference; current state and backlog live in
> `ROADMAP.md`. Items marked `[x]` are verified done; the unchecked items are known
> gaps — see notes.

## MVP Scope

### Included
1. Local directory and connected drive navigation (USB, Xbox-visible drives via
   `GetLogicalDrives`).
2. 3-column listing (Parent | Current | Preview), with live preview when moving selection.
3. Preview of:
   - Text (truncated)
   - Images (thumbnail)
   - Folders (listing)
   - `.zip`, `.7z`, `.rar` files (internal listing, treated as virtual folders)
4. Navigation **into** compressed archives (drill-in as if it were a folder),
   including multiple levels (zip inside zip, if it exists — best effort).
5. Context menu (Y button) with actions:
   - Extract (compressed archive → chosen destination folder)
   - Copy / Move / Rename / Delete / Create ZIP / Favorite / Share
6. 100% navigable via gamepad (`Windows.Gaming.Input.Gamepad`), without relying on mouse/keyboard
   (but without breaking them — they should work as bonus, not as a requirement).
7. Custom visual theme (no default Fluent Design chrome).
8. Functional deploy on Xbox via Developer Mode + Device Portal (sideload `.appx`/`.msix`).

### Out of scope (MVP) — documented backlog in ROADMAP.md
- Network browsing (SMB/UNC, `\\server\share`) — feasibility in `FILE-SHARES.md`
- Binary/hex dump preview
- Multiple tabs/simultaneous panes
- Password-protected `.rar` or complex multi-volume support (accept whatever `SharpCompress`
  supports natively, no extra features)
- Cloud sync (OneDrive, etc.)

*Note: text editing and zip creation were originally out of scope — both shipped
post-MVP (Phase 12 / v1.2.0).*

## Non-functional Requirements

- **Responsiveness**: navigation (moving selection, switching column) must respond in < 100ms
  perceived, even with image/large file preview loading in background
  (async, does not block the input thread).
- **I/O Robustness**: directories without permission, drives disconnected during navigation,
  corrupted files — must fail with visible message in the Preview column, never crash
  the app.
- **Xbox Compatibility**: `TargetDeviceFamily Name="Windows.Xbox"`, tested via real
  Developer Mode (not just emulator/desktop).
- **No mouse/keyboard dependency**: every flow (including confirmation dialogs,
  destination folder selection in "Move/Copy") must have a 100% gamepad alternative.

## Personas / Expected Use

End user: Xbox owner with Developer Mode active, wants to browse USB flash drives/external HDDs or
local app folders to organize ROMs, ISOs, save backups, etc., without needing a
connected keyboard/mouse — gamepad only.

## MVP "Done" Criteria

- [x] Runs on real Xbox via sideload, with no keyboard/mouse input required in any
      flow. (Deploy flow: `docs/DEPLOY-XBOX.md`.)
- [x] Navigates into at least one file of each format (zip/7z/rar) and shows
      correct listing. (SharpCompress; see `ARCHIVES.md`.)
- [x] Copy, move, rename, delete, and extract work without errors in normal scenarios.
      (P/Invoke `FileOperations`; batch ops extend this.)
- [x] Text and image preview work for the most common formats
      (`.txt`/`.log`/`.md`, `.png`/`.jpg`/`.bmp`). (Plus code highlighting, SVG,
      audio, video, PDF, ROM.)
- [ ] Theme can be changed by editing the JSON, without recompiling.
      **Not shipped** — theme is XAML-only (`BladeTheme.xaml`); the planned
      `AppTheme.cs` JSON loader and theme selector are backlog
      (`SETTINGS-EXPANSION.md`, `ROADMAP.md`).
