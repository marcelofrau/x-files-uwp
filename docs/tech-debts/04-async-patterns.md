# Async Patterns Debt

## MEDIUM: TaskCompletionSource Without RunContinuationsAsynchronously

All 16 `TaskCompletionSource` instances in the codebase use the default constructor
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

### Risk Assessment

Current risk is **low** because all `SetResult()` calls happen from the UI thread
(via `Dispatcher.RunAsync` or button click handlers), and all `await` sites are
also on the UI thread. The continuations run synchronously on the UI thread,
which is correct behavior.

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

Schedule as MEDIUM — not urgent, but apply `RunContinuationsAsynchronously`
proactively during the next dialog refactor pass. Cost is zero, safety is improved.
