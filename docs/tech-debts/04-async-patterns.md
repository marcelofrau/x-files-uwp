---
layout: default
title: Async Patterns Debt
---
# Async Patterns Debt

## FIXED: Blocking Call on Win2D Draw Thread

**File:** `Visualizers/Visualizers/PlasmaVisualizer.cs` — was new in Aug 2026 audit.

```csharp
// Old — blocked the draw thread on first draw
var file = task.AsTask().GetAwaiter().GetResult();
var buffer = Windows.Storage.FileIO.ReadBufferAsync(file).AsTask().GetAwaiter().GetResult();
```

The shader was loaded synchronously on the `CanvasAnimatedControl.Draw` thread
(composition thread, not UI). `.GetResult()` blocks that thread; if the underlying
`IAsyncOperation` needs a UI-thread or thread-pool hop that's starved, this deadlocks
or stutters rendering.

**Fixed (Aug 2026):** `EnsureShaderLoading()` loads the embedded shader asynchronously
once per app run (kicked off from `Initialize`). The draw path never blocks — it uses
the GPU shader when `_shaderLoaded` is true (volatile), otherwise falls back to the CPU
renderer until the bytecode is ready. `PixelShaderEffect` creation stays on the draw
thread where the device is valid.

## MEDIUM: `_fftSignal.Wait(100)`

**File:** `Audio/AudioLevelService.cs:927`

SemaphoreSlim wait with a 100ms timeout on the FFT worker. Bounded, but verify the
worker never exceeds its frame budget under load (it could stall the audio pipeline
if the producer outruns the consumer repeatedly).

## FIXED: TaskCompletionSource Without RunContinuationsAsynchronously

All **19** `TaskCompletionSource` instances previously used the default constructor
without `TaskCreationOptions.RunContinuationsAsynchronously`.

**Fixed (Aug 2026):** all 19 now constructed with
`TaskCreationOptions.RunContinuationsAsynchronously` across 14 files (dialogs, page
overlays, `FilePreviewService`, `PdfPreviewService`).

### Affected Files (now fixed)

| File | Line | Type |
|---|---|---|
| `Controls/AlertDialog.xaml.cs` | 107 | `TaskCompletionSource<bool>` |
| `Controls/FileActionSheet.xaml.cs` | 125, 287 | `TaskCompletionSource<FileAction?>` |
| `Controls/FileOperationConfirmDialog.xaml.cs` | 45, 68 | `TaskCompletionSource<bool>` |
| `Controls/FolderBrowserDialog.xaml.cs` | 30 | `TaskCompletionSource<string>` |
| `Controls/InputDialog.xaml.cs` | 24 | `TaskCompletionSource<string>` |
| `Controls/OverwriteDialog.xaml.cs` | 30 | `TaskCompletionSource<int>` |
| `Controls/SettingsPage.xaml.cs` | 36 | `TaskCompletionSource<bool>` |
| `Controls/StartMenu.xaml.cs` | 43 | `TaskCompletionSource<StartMenuItem?>` |
| `Controls/TextEditorOverlay.xaml.cs` | 616 | `TaskCompletionSource<UnsavedDialogResult>` |
| `Controls/MillerColumnsPage.xaml.cs` | 4418, 4482 | `TaskCompletionSource<int>` |
| `Controls/LetterGridOverlay.xaml.cs` | 27 | `TaskCompletionSource<char?>` |
| `FileSystem/FilePreviewService.cs` | 492, 640 | `TaskCompletionSource<bool>` |
| `Services/PdfPreviewService.cs` | 131 | `TaskCompletionSource<bool>` |

(19 total as of Aug 2026 — all now use `RunContinuationsAsynchronously`.)

### Remaining Risk

Original risk was **low** because all `SetResult()` calls happen from the UI thread
(Dispatcher/button handlers) and all `await` sites are also UI-thread. The proactive
sweep removes the footgun if any `SetResult()`/`await` later moves to a background
thread or `ConfigureAwait(false)` is introduced.
