---
layout: default
title: Technical Debt Audit
---
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
| [Architecture](01-architecture.md) | — | 2 | 3 | — |
| [Error Handling](02-error-handling.md) | — | — | 2 | 1 |
| [UWP Compliance](03-uwp-compliance.md) | — | — | — | — |
| [Async Patterns](04-async-patterns.md) | — | — | 1 | — |
| [Code Duplication](05-code-duplication.md) | — | — | — | 2 |
| [Miscellaneous](06-misc.md) | — | — | 1 | — |
| **Total** | **0** | **2** | **4** | **3** |

> Aug 2026 quick-win sweep closed: debug flags, `Prefer32Bit`, PT comments, dead
> `DebugOverlay`/`ScreenLogger`, `SubtitleDetector` P/Invoke, `PlasmaVisualizer`
> blocking shader load, all 19 TCS, 2 of 3 empty catches. See per-file docs.
> **Aug 2026 god object sweep closed the CRITICAL:** `MillerColumnsPage` split into 8
> partial files + 3 extracted pure classes (see `01-architecture.md`).

## Priority Order

1. **Architecture** — `MillerColumnsPage` god object (4960 lines) was the #1
   maintainability blocker. **Resolved Aug 2026**: split into 8 partial files + 3
   extracted pure classes (test coverage 45 → 75). Remaining: method-level refactors
   (long methods) + optional controller extraction — see `01-architecture.md`.
2. **Error Handling** — two remaining `catch { }` are accepted by design (pure class +
   infinite-recursion guard).
3. **Miscellaneous** — hardcoded cert password (`dev`) in csproj; drive from env var
   before any shared-runner packaging.
4. **Code Duplication** — Cosmetic, fix opportunistically.

## Clean Areas (no issues found)

- `OperationCanceledException` re-throw patterns are correct (DeezerProvider, MusicBrainzProvider)
- `DisplayRequest` catch blocks are acceptable (log + continue)
- The two `File.ReadAllBytes` font loads flagged in the Jul 2025 audit
  (`MillerColumnsPage.xaml.cs:543`, `TextEditorOverlay.xaml.cs:641`) are **fixed** —
  both now use `Win32FileStream` / P/Invoke.
- **UWP compliance is now fully clean** — `SubtitleDetector` was the last `System.IO`
  filesystem caller; converted to P/Invoke (Aug 2026).

## Re-audit Notes (Aug 2026)

Delta vs the Jul 2025 audit:

- `MillerColumnsPage.xaml.cs` grew 3002 → **4960 lines**; complexity ~916 (was 702).
  Batch mode, favorites, ROM covers, and 29 visualizers were added to the same class.
  **Resolved Aug 2026**: decomposed into 8 partial files + 3 pure classes.
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
  direct coverage (Log/`FromApp` deps). **Aug 2026:** + `Formatting`,
  `HighlightRenderer`, `RomCoverProvider` (pure classes extracted from
  `MillerColumnsPage`) — 75 tests total.
- **Quick-win sweep (Aug 2026):** debug flags OFF, `Prefer32Bit` removed, PT comments
  fixed (EN), dead `DebugOverlay`+`ScreenLogger` deleted, `SubtitleDetector` → P/Invoke,
  `PlasmaVisualizer` shader load async (no draw-thread block), all 19 `TaskCompletionSource`
  → `RunContinuationsAsynchronously`, empty catches logged at Verbose. **God object
  decomposition done** (8 partials + 3 pure classes). Remaining:
  cert password env var, `AudioLevelService._fftSignal.Wait(100)`
  budget check.
