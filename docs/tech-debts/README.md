# Technical Debt Audit

Audit performed: Jul 2025. **Re-audit: Aug 2026** (see "Re-audit Notes" below).
Scope: `XFiles/**/*.cs` + `XFiles/**/*.xaml`.

## Severity Matrix

| Severity | Description | Action |
|---|---|---|
| **CRITICAL** | Blocks features, causes crashes, or violates platform rules | Fix before next phase |
| **HIGH** | Degrades maintainability or risks subtle bugs | Fix within current phase |
| **MEDIUM** | Code smell, potential future issue | Schedule for cleanup sprint |
| **LOW** | Cosmetic, minor duplication | Accept or fix opportunistically |

## Summary

| Category | CRITICAL | HIGH | MEDIUM | LOW |
|---|---|---|---|---|
| [Architecture](01-architecture.md) | 1 | 2 | 3 | — |
| [Error Handling](02-error-handling.md) | — | — | 4 | 1 |
| [UWP Compliance](03-uwp-compliance.md) | — | 1 | — | — |
| [Async Patterns](04-async-patterns.md) | — | 1 | 1 | — |
| [Code Duplication](05-code-duplication.md) | — | — | — | 2 |
| [Miscellaneous](06-misc.md) | — | 1 | 2 | 1 |
| **Total** | **1** | **5** | **10** | **4** |

## Priority Order

1. **Architecture** — `MillerColumnsPage` god object is the #1 maintainability blocker
   (4373 lines, grew ~45% since Jul 2025 audit).
2. **Async Patterns** — `PlasmaVisualizer` blocking `.GetResult()` on the Win2D draw
   thread (deadlock/sync risk on the rendering path).
3. **UWP Compliance** — `SubtitleDetector` still uses `System.IO.Directory` (silent
   failures on Xbox external drives).
4. **Error Handling** — Logged exceptions help debugging, low effort.
5. **Miscellaneous** — debug flags left ON, hardcoded cert password, `Prefer32Bit` in
   x64 configs.
6. **Code Duplication** — Cosmetic, fix opportunistically.

## Clean Areas (no issues found)

- `OperationCanceledException` re-throw patterns are correct (DeezerProvider, MusicBrainzProvider)
- `DisplayRequest` catch blocks are acceptable (log + continue)
- The two `File.ReadAllBytes` font loads flagged in the Jul 2025 audit
  (`MillerColumnsPage.xaml.cs:543`, `TextEditorOverlay.xaml.cs:641`) are **fixed** —
  both now use `Win32FileStream` / P/Invoke.

## Re-audit Notes (Aug 2026)

Delta vs the Jul 2025 audit:

- `MillerColumnsPage.xaml.cs` grew 3002 → **4373 lines**; complexity ~916 (was 702).
  Batch mode, favorites, ROM covers, and 29 visualizers were added to the same class.
  Decomposition is now the top remediation priority.
- `TaskCompletionSource` instances: 16 → **19** (new dialogs added more; all still
  default-constructed without `RunContinuationsAsynchronously`).
- **New:** `PlasmaVisualizer.cs:113-114` calls `.GetAwaiter().GetResult()` on the Win2D
  draw thread when loading the embedded shader — the only blocking call in the
  visualizer/render path.
- **New:** `AudioLevelService.cs:927` uses `_fftSignal.Wait(100)` (semaphore wait with
  timeout on the FFT worker). Bounded, but verify it can't exceed budget.
- **New:** Debug config ships with `VUMETER_DEBUG` + `AUDIO_LEVEL_DEBUG` **enabled** in
  `XFiles.csproj`; `PackageCertificatePassword=dev` is hardcoded; `Prefer32Bit=true`
  appears in x64 configs.
- **Fixed since Jul 2025:** both font `File.ReadAllBytes` calls (UWP compliance).
- **New:** `tests/XFiles.Tests.csproj` (MSTest, net8.0, linked-source) — P0 coverage for
  `FftHelper`, `EncodingDetector` (extracted from `TextEditorService`), `RomHeaderParser`,
  `Id3Tag`, `FilenameParser`; wired into CI. `TextEditorService`/`Id3Tag` still lack
  direct coverage (Log/`FromApp` deps).
