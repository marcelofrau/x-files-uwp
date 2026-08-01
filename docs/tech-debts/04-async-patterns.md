# Async Patterns Debt

## HIGH: Blocking Call on Win2D Draw Thread

**File:** `Visualizers/Visualizers/PlasmaVisualizer.cs:113-114` — **new in Aug 2026 audit**

```csharp
var file = task.AsTask().GetAwaiter().GetResult();
var buffer = Windows.Storage.FileIO.ReadBufferAsync(file).AsTask().GetAwaiter().GetResult();
```

The shader is loaded synchronously on the `CanvasAnimatedControl.Draw` thread
(composition thread, not UI). `.GetResult()` blocks that thread; if the underlying
`IAsyncOperation` needs a UI-thread or thread-pool hop that's starved, this deadlocks
or stutters rendering.

**Fix:** load the embedded shader bytes once at `Initialize(CanvasDevice)` (or cache
the `StorageFile`), with no `GetResult()` on the draw path.

## MEDIUM: `_fftSignal.Wait(100)`

**File:** `Audio/AudioLevelService.cs:927`

SemaphoreSlim wait with a 100ms timeout on the FFT worker. Bounded, but verify the
worker never exceeds its frame budget under load (it could stall the audio pipeline
if the producer outruns the consumer repeatedly).

## MEDIUM: TaskCompletionSource Without RunContinuationsAsynchronously

All **19** `TaskCompletionSource` instances in the codebase use the default constructor
without `TaskCreationOptions.RunContinuationsAsynchronously`.

When `SetResult()` is called, the continuation runs inline on the calling thread.
If that thread holds a lock or is the UI thread, this can cause deadlocks or
reentrancy issues.

### Affected Files

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
| `Controls/TextEditorOverlay.xaml.cs` | 487 | `TaskCompletionSource<UnsavedDialogResult>` |
| `Controls/MillerColumnsPage.xaml.cs` | 3183, 3247 | `TaskCompletionSource<int>` |
| `FileSystem/FilePreviewService.cs` | 399, 544 | `TaskCompletionSource<bool>` |
| `Services/PdfPreviewService.cs` | 131 | `TaskCompletionSource<bool>` |

(19 total as of Aug 2026 — new dialogs since Jul 2025 added 3 more; all still
default-constructed.)

### Risk Assessment

Current risk is **low** because all `SetResult()` calls happen from the UI thread
(via `Dispatcher.RunAsync` or button click handlers), and all `await` sites are
also on the UI thread. The continuations run synchronously on the UI thread,
which is correct behavior. **Exception:** the new `PlasmaVisualizer` blocking call
above is a real risk and is treated as HIGH.

Risk increases if:
- Any `SetResult()` moves to a background thread
- Any `await` moves to a non-UI context
- Code is refactored to use `ConfigureAwait(false)`

### Fix (when needed)

```csharp
// Before
_tcs = new TaskCompletionSource<bool>();

// After
_tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
```

### Recommendation

Apply `RunContinuationsAsynchronously` proactively during the next dialog refactor
pass (cost zero, safety improved). Fix `PlasmaVisualizer` first.
