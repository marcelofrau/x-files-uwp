# UWP Compliance Debt

Platform constraint: Xbox UWP sandbox. `System.IO.File.*` and `System.IO.Directory.*`
methods silently fail or throw `UnauthorizedAccessException` on external drives.

All filesystem access must use Win32 P/Invoke (`*FromAppW` variants).

## HIGH: Remaining System.IO Usage

### SubtitleDetector.cs

**File:** `FileSystem/SubtitleDetector.cs:31,37`

```csharp
// Line 31 — uses Directory.Exists (unreliable in UWP sandbox)
if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return results;

// Line 37 — uses Directory.EnumerateFiles (not P/Invoke)
foreach (string file in Directory.EnumerateFiles(dir))
```

**Fix:** Replace with `CheckPathType(dir)` and `FindFirstFileExFromAppW` enumeration
(already done in all other files). Pattern exists in `FileOperations.cs` and
`ArchiveBrowser.cs` — copy the same P/Invoke approach.

### MillerColumnsPage.xaml.cs

**File:** `Controls/MillerColumnsPage.xaml.cs:543`

```csharp
// Uses System.IO.File.ReadAllBytes — fails on external drives
var fontBytes = await Task.Run(() => System.IO.File.ReadAllBytes(fontFile.Path));
```

**Fix:** Use `Win32FileStream.OpenRead()` + manual byte read (same pattern as
`FileOperations.ReadAllBytesWin32`).

### TextEditorOverlay.xaml.cs

**File:** `Controls/TextEditorOverlay.xaml.cs:641`

```csharp
// Same duplicate of the above
var fontBytes = await Task.Run(() => System.IO.File.ReadAllBytes(fontFile.Path));
```

**Fix:** Same as above. Also consider extracting shared helper (see `05-code-duplication.md`).

## Status of Other Files

| File | Previous | Current |
|---|---|---|
| `FileOperations.cs` | Used `File.Exists`, `Directory.*` | All P/Invoke via `CheckPathType` + `FindFirstFileExFromAppW` |
| `ArchiveBrowser.cs` | Used `Directory.CreateDirectory` | All P/Invoke |
| `DeleteAsync` | Used `File.Exists` | Uses `CheckPathType` |
| `DeleteDirectoryAsync` | Used `Directory.*` | All P/Invoke recursive |
| `CopyDirectoryRecursive` | Used `Directory.GetFiles/GetDirectories` | All P/Invoke |
| `MoveDirectory` | Used `Directory.*` | All P/Invoke |
| `ExtractAsync` | Used `Directory.Exists/CreateDirectory` | All P/Invoke |
| `ExtractFileAsync` | Used `Directory.Exists/CreateDirectory` | All P/Invoke |
| `CreateZipAsync` | Used `File.ReadAllBytes` | Uses `Win32FileStream` |
