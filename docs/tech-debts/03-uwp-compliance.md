# UWP Compliance Debt

Platform constraint: Xbox UWP sandbox. `System.IO.File.*` and `System.IO.Directory.*`
methods silently fail or throw `UnauthorizedAccessException` on external drives.

All filesystem access must use Win32 P/Invoke (`*FromAppW` variants).

## HIGH: Remaining System.IO Usage

### SubtitleDetector.cs

**File:** `FileSystem/SubtitleDetector.cs:31,37` — still open (Aug 2026)

```csharp
// Line 31 — uses Directory.Exists (unreliable in UWP sandbox)
if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return results;

// Line 37 — uses Directory.EnumerateFiles (not P/Invoke)
foreach (string file in Directory.EnumerateFiles(dir))
```

**Fix:** Replace with `CheckPathType(dir)` and `FindFirstFileExFromAppW` enumeration
(already done in all other files). Pattern exists in `FileOperations.cs` and
`ArchiveBrowser.cs` — copy the same P/Invoke approach.

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
| `MillerColumnsPage.xaml.cs` (font) | `File.ReadAllBytes` (:543) | **FIXED** — P/Invoke |
| `TextEditorOverlay.xaml.cs` (font) | `File.ReadAllBytes` (:641) | **FIXED** — P/Invoke |
| `FileOperations.cs` | — | `ReadAllBytesWin32` helper at :1442 (P/Invoke) |

> Re-audit (Aug 2026): the only remaining `System.IO` filesystem *enumeration* is
> `SubtitleDetector`. Everything else is P/Invoke.
