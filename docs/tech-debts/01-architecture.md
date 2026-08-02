# Architecture Debt

## CRITICAL (RESOLVED): MillerColumnsPage God Object

**Before (Aug 2026):** `Controls/MillerColumnsPage.xaml.cs` — **4960 lines** (4372
non-blank), cyclomatic complexity ~916 (was 3002/702 in Jul 2025). Single class with
20+ responsibilities, no `#region`, fields interleaved between methods.

**After (Aug 2026):** mechanical split into **8 partial files** + **3 extracted pure
classes**. The page is now `partial` across files; no behavior change (build + 75 unit
tests green, XAML untouched).

### Split (partial files, `XFiles/Controls/`)

| File | Lines | Content |
|---|---|---|
| `MillerColumnsPage.xaml.cs` | 381 | Core: fields, ctor, lifecycle, nav events, `UpdateUIAsync` |
| `MillerColumnsPage.Preview.cs` | 668 | Preview column, highlight.js wiring, debounce |
| `MillerColumnsPage.Navigation.cs` | 1151 | Input handlers + `INavigable` contract |
| `MillerColumnsPage.FileOps.cs` | 1117 | Batch mode + action sheet + copy/move/rename/delete/extract/zip |
| `MillerColumnsPage.Media.cs` | 1316 | Fullscreen video/audio, OSD, visualizer, tracks, seek |
| `MillerColumnsPage.RomCover.cs` | 142 | ROM cover fetch (local file + LibRetro) |
| `MillerColumnsPage.Error.cs` | 178 | Error overlay + share report |
| `Converters.cs` | 63 | `BooleanToColumnWidthConverter` + `BooleanToBrushConverter` |

### Extracted pure classes (testable, `XFiles/FileSystem/`)

| File | Content | Tests |
|---|---|---|
| `Formatting.cs` | `FormatSize`/`FormatBytes` (deduped duplicates), `FormatFsTime`, `FormatCount` | `FormattingTests` (13) |
| `HighlightRenderer.cs` | `GetHighlightLang`, `HtmlEncode`, `BuildSvgHtml`, `BuildHighlightHtml` | `HighlightRendererTests` (9) |
| `RomCoverProvider.cs` | `BuildTitleVariations`, `LibRetroSystemNames` | `RomCoverProviderTests` (8) |

### Remaining (method-level, not file-level)

- Long methods below (`UpdatePreviewColumnAsync`, `OnConfirm`, ...) still need
  refactoring — the split changed file layout, not method complexity.
- Optional next step (deferred, high risk): extract real controllers
  (`MediaPlayerController`, `FileOperationHandler`) that own XAML named elements.
- Each partial carries the original 30-using block; unused usings per file are a
  cosmetic cleanup.

> **Re-audit note (Aug 2026):** grew 45% since the last audit. God object decomposition
> (partial split + pure-class extraction) is now **done** — this entry tracked as
> resolved. Any new feature should still go into a new class, not the page file.

## HIGH: Long Methods (>50 lines)

| File | Method | Lines | Nesting |
|---|---|---|---|
| `GamepadInputService.cs` | `OnTick()` | 206 | 4+ |
| `MillerColumnsPage.Preview.cs` | `UpdatePreviewColumnAsync()` | 162 | 3 |
| `MillerColumnsPage.Navigation.cs` | `OnConfirm()` | 118 | 4+ |
| `FileOperations.cs` | `ExtractAsync()` | 110 | 4+ |
| `MillerColumnsPage.FileOps.cs` | `ShowFileActionSheetAsync()` | 79 | 2 |
| `FilePreviewService.cs` | `LoadImagePreview()` | 82 | 2 |
| `FilePreviewService.cs` | `LoadImagePreviewFromStream()` | 82 | 2 |
| `TextEditorService.cs` | `DetectEncoding()` | 82 | 3 |
| `MillerColumnsPage.FileOps.cs` | `HandleExtractAsync()` | 87 | 3 |
| `MillerColumnsPage.FileOps.cs` | `HandleMoveAsync()` | 71 | 3 |
| `FileOperations.cs` | `CreateZipAsync()` | 91 | 4+ |
| `FileOperations.cs` | `ExtractFileAsync()` | 83 | 3 |
| `MillerColumnsPage.FileOps.cs` | `HandlePasteAsync()` | 62 | 3 |
| `MillerColumnsPage.Media.cs` | `OnVideoSubtitleSelected()` | 67 | 4+ |
| `MillerColumnsPage.FileOps.cs` | `HandleExtractFileAsync()` | 63 | 3 |

## HIGH: Files >400 Lines

| File | Lines | Complexity |
|---|---|---|
| `Controls/MillerColumnsPage.Media.cs` | 1316 | partial (org.) |
| `Controls/MillerColumnsPage.Navigation.cs` | 1151 | partial (org.) |
| `Controls/MillerColumnsPage.FileOps.cs` | 1117 | partial (org.) |
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
| `MillerColumnsPage.Preview.cs` | 30 (partials share the original using block; some unused) |
| `MediaPreviewControl.xaml.cs` | 18 |
| `App.xaml.cs` | 13 |
| `AudioLevelService.cs` | 11 |
| `FileOperations.cs` | 9 |
