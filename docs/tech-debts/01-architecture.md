# Architecture Debt

## CRITICAL: MillerColumnsPage God Object

**File:** `Controls/MillerColumnsPage.xaml.cs` — **4373 lines** (Aug 2026 re-audit;
was 3002 in Jul 2025), cyclomatic complexity ~916 (was 702).

Single class with 20+ responsibilities:

| # | Responsibility | Key methods |
|---|---|---|
| 1 | Column navigation | `DrillIn`, `DrillOut`, `UpdateUIAsync` |
| 2 | Preview rendering | `UpdatePreviewColumnAsync` (162 lines) |
| 3 | Media playback (inline) | `HandlePlayPause`, `HandleStopPlayer` |
| 4 | Fullscreen video | `ShowMediaFullscreenAsync`, `OnFsVideoMediaEnded` |
| 5 | Fullscreen audio | `LoadAudioFullscreenMetadataAsync`, `OnFsAudioTrackChanged` |
| 6 | File operations | `HandlePasteAsync`, `HandleMoveAsync`, `HandleRenameAsync`, `HandleExtractAsync` |
| 7 | Gamepad input routing | `OnConfirm`, `OnBack`, `OnDPadUp/Down/Left/Right`, `OnTriggerHeld` |
| 8 | Dialog management | 8+ overlay show/hide methods |
| 9 | File Action Sheet | `ShowFileActionSheetAsync` (79 lines, 11 action types) |
| 10 | Error overlay | `ShowErrorOverlay`, `CopyErrorReport` |
| 11 | Highlight.js integration | `EnsureHighlightAssetsLoadedAsync`, `BuildHighlightHtml` |
| 12 | Audio visualizer | 29 visualizer modes, `CycleAudioVisualizer` + picker |
| 13 | Preview debouncing | `_previewDebounceTimer`, `_mediaLoadTimer` |
| 14 | OSD system | `ShowOsd`, `HideOsd` with fade + auto-hide |
| 15 | Volume control | `HandleVolumeChange`, `UpdateVolumeUI` |
| 16 | Display request | `RequestDisplayRelease`/`RequestDisplayActivate` |
| 17 | Batch mode | toggle, multi-select, batch ops |
| 18 | Favorites | `FavoritesManager` integration, Y long-press |
| 19 | ROM cover art | gamelist.xml + LibRetro fetch + SQLite cache |
| 20 | Start menu / search / logs / about | overlay routing |

**Suggested decomposition:**
- `MediaPlayerController` — playback + fullscreen + OSD + volume (responsibilities 3-5, 14-15)
- `FileOperationHandler` — paste/move/rename/delete/extract/zip + batch (responsibilities 6, 17)
- `PreviewRenderer` — preview column + highlight.js (responsibilities 2, 11)
- `InputRouter` — gamepad dispatch to overlays + navigation (responsibility 7)
- `DialogManager` — overlay lifecycle (responsibility 8)
- `RomCoverProvider` — gamelist + LibRetro + cache (responsibility 19)

> **Re-audit note (Aug 2026):** grew 45% since the last audit with no decomposition.
> This is now the top remediation priority — any new feature should go into a new
> class, not this file.

## HIGH: Long Methods (>50 lines)

| File | Method | Lines | Nesting |
|---|---|---|---|
| `GamepadInputService.cs` | `OnTick()` | 206 | 4+ |
| `MillerColumnsPage.xaml.cs` | `UpdatePreviewColumnAsync()` | 162 | 3 |
| `MillerColumnsPage.xaml.cs` | `OnConfirm()` | 118 | 4+ |
| `FileOperations.cs` | `ExtractAsync()` | 110 | 4+ |
| `MillerColumnsPage.xaml.cs` | `ShowFileActionSheetAsync()` | 79 | 2 |
| `FilePreviewService.cs` | `LoadImagePreview()` | 82 | 2 |
| `FilePreviewService.cs` | `LoadImagePreviewFromStream()` | 82 | 2 |
| `TextEditorService.cs` | `DetectEncoding()` | 82 | 3 |
| `MillerColumnsPage.xaml.cs` | `HandleExtractAsync()` | 87 | 3 |
| `MillerColumnsPage.xaml.cs` | `HandleMoveAsync()` | 71 | 3 |
| `FileOperations.cs` | `CreateZipAsync()` | 91 | 4+ |
| `FileOperations.cs` | `ExtractFileAsync()` | 83 | 3 |
| `MillerColumnsPage.xaml.cs` | `HandlePasteAsync()` | 62 | 3 |
| `MillerColumnsPage.xaml.cs` | `OnVideoSubtitleSelected()` | 67 | 4+ |
| `MillerColumnsPage.xaml.cs` | `HandleExtractFileAsync()` | 63 | 3 |

## HIGH: Files >400 Lines

| File | Lines | Complexity |
|---|---|---|
| `Controls/MillerColumnsPage.xaml.cs` | 4373 | ~916 |
| `FileSystem/FileOperations.cs` | 966 | 162 |
| `FileSystem/FilePreviewService.cs` | 599 | 65 |
| `Controls/MediaPreviewControl.xaml.cs` | 538 | 110 |
| `Controls/TextEditorOverlay.xaml.cs` | 538 | 99 |
| `Audio/AudioLevelService.cs` | 532 | 90 |
| `FileSystem/TextEditorService.cs` | 422 | 178 |
| `Controls/ColumnListView.xaml.cs` | 415 | 45 |
| `Navigation/GamepadInputService.cs` | 330 | 69 |
| `Navigation/ColumnNavigator.cs` | 324 | 54 |
| `Metadata/MusicBrainzProvider.cs` | 342 | 96 |

## MEDIUM: High Nesting Depth (4+)

| File | Method | Depth |
|---|---|---|
| `GamepadInputService.cs` | `OnTick()` | 4+ |
| `FileOperations.cs` | `ExtractAsync()` | 4+ |
| `FileOperations.cs` | `CreateZipAsync()` | 4+ |
| `MillerColumnsPage.xaml.cs` | `OnConfirm()` | 4+ |
| `MillerColumnsPage.xaml.cs` | `OnVideoSubtitleSelected()` | 4+ |

## MEDIUM: Import Surface

| File | `using` count |
|---|---|
| `MillerColumnsPage.xaml.cs` | 23 |
| `MediaPreviewControl.xaml.cs` | 18 |
| `App.xaml.cs` | 13 |
| `AudioLevelService.cs` | 11 |
| `FileOperations.cs` | 9 |
