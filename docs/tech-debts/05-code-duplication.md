# Code Duplication Debt

## LOW: Highlight.js Font Loading

Duplicated in two files with identical logic:

### MillerColumnsPage.xaml.cs (lines 540-546)

```csharp
var fontFile = await StorageFile.GetFileFromApplicationUriAsync(
    new Uri("ms-appx:///Assets/Inconsolata-Regular.ttf"));
var fontBytes = await Task.Run(() => System.IO.File.ReadAllBytes(fontFile.Path));
_fontBase64 = Convert.ToBase64String(fontBytes);
```

### TextEditorOverlay.xaml.cs (lines 639-642)

```csharp
var fontFile = await StorageFile.GetFileFromApplicationUriAsync(
    new Uri("ms-appx:///Assets/Inconsolata-Regular.ttf"));
var fontBytes = await Task.Run(() => System.IO.File.ReadAllBytes(fontFile.Path));
_fontBase64 = Convert.ToBase64String(fontBytes);
```

**Fix:** Extract to shared helper (also fixes UWP compliance — both use `File.ReadAllBytes`):

```csharp
// In a shared utility class
public static async Task<string> LoadHighlightAssetsAsync()
{
    var fontFile = await StorageFile.GetFileFromApplicationUriAsync(
        new Uri("ms-appx:///Assets/Inconsolata-Regular.ttf"));
    var fontBytes = await Task.Run(() => /* Win32FileStream read */);
    return Convert.ToBase64String(fontBytes);
}
```

Note: Highlight.js JS and CSS loading is also similar but differs between the two
files (MillerColumnsPage loads JS+CSS, TextEditorOverlay loads JS+CSS+editor.js).
Only the font loading is truly identical.

## LOW: Dispatcher.RunAsync Pattern

22 instances of `Dispatcher.RunAsync(CoreDispatcherPriority.Normal, ...)` across
4 files. All follow the same pattern:

```csharp
await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
{
    // UI update
});
```

**Not worth extracting** — the lambda bodies are all different, and the pattern
is idiomatic UWP. Would add complexity without reducing code. Accept as-is.

## Clean Areas (no duplication found)

- Gamepad button handling — consistent pattern across all dialogs
- P/Invoke declarations — centralized in `FileOperations.cs`
- `CheckPathType()` — single implementation, used everywhere
- Dialog show/hide lifecycle — consistent across all overlay controls
