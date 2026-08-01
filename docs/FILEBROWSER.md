# File Browser — Data Model and Disk Access

Reference patterns validated on real Xbox in `dosbox-pure-uwp` (C++ UWP),
adapted to C#/XAML.

---

## Manifest — Required Capabilities

```xml
<rescap:Capability Name="broadFileSystemAccess" />
<rescap:Capability Name="runFullTrust" />
```

Both are `rescap:` (restricted) — must be in `Package.appxmanifest`. Without these
two, `*FromApp` APIs fail silently on Xbox. Do not use `musicLibrary`,
`picturesLibrary`, etc. — `broadFileSystemAccess` covers everything.

---

## Golden Rule: Win32 APIs Must Use `*FromApp` Variants

On Xbox UWP, even with `broadFileSystemAccess`, standard CRT APIs are **blocked**:

| Standard CRT/Win32 (BLOCKED) | UWP Variant (USE) |
|---|---|
| `FindFirstFile` / `FindNextFile` | `FindFirstFileExFromAppW` / `FindNextFileW` |
| `_wfopen` / `fopen` | `CreateFile2FromAppW` |
| `_wstat64` / `_waccess` | `GetFileAttributesExFromAppW` |
| `CreateFileW` | `CreateFile2FromAppW` |
| `DeleteFileW` | `DeleteFileFromAppW` |
| `MoveFileW` | `MoveFileFromAppW` |
| `CreateDirectoryW` | `CreateDirectoryFromAppW` |

Declare via P/Invoke with `[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]`
pointing to the `FromApp` version. These `*FromApp` variants **also work on desktop
Windows 10+** — which is why the net8.0 unit tests (`tests/`) exercise real temp
files/dirs without any UWP shim.

`FileOperations.cs` and `DirectoryScanner.cs` contain the working P/Invoke
declarations — copy from there rather than re-typing structs.

---

## `FileEntry` (Base Model)

`FileSystem/FileEntry.cs`:

```csharp
public class FileEntry
{
    public string Name { get; set; }
    public string FullPath { get; set; }
    public bool IsDirectory { get; set; }
    public bool IsDrive { get; set; }        // root drive entry (e.g. "C:\")
    public bool IsArchive { get; set; }      // .zip/.7z/.rar — behaves as "virtual folder"
    public long SizeBytes { get; set; }      // 0 for directories
    public DateTimeOffset? LastModified { get; set; }

    // Only present when the entry lives INSIDE a compressed file:
    public string ArchiveRootPath { get; set; }     // path to the .zip/.7z/.rar on real disk
    public string ArchiveInternalPath { get; set; } // relative path inside the archive
    public bool IsVirtual { get; set; }             // archive/favorites virtual entry
}
```

`IsArchive` is derived from the extension (`.zip`, `.7z`, `.rar`) and makes the item
behave like a folder in navigation (drill-in with A / D-pad right), even though it's
physically a file.

---

## `DirectoryScanner`

Lists the contents of a **real** path (compressed files → `ArchiveBrowser`, see
`ARCHIVES.md`).

### Root Level (`path == null`/empty)

`GetLogicalDrives()` bitmask (bit 0 = A:, bit 1 = B:, ...) → one `FileEntry` per
available drive (`IsDrive = true`), plus a synthetic `[App Data]` entry pointing to
`ApplicationData.Current.LocalFolder.Path` (the app sandbox, always accessible).
No specific USB detection — all drives listed identically.

### Non-Root — Directory Scan

**Do NOT use `StorageFolder.GetFoldersAsync()`/`GetFilesAsync()`** as the primary
method — those APIs require `FutureAccessList` membership or declared folders, which
doesn't cover "any USB drive connected to Xbox".

Pattern (`DirectoryScanner.cs`):

```csharp
string searchPath = Path.Combine(path, "*");
IntPtr hFind = FindFirstFileExFromAppW(
    searchPath,
    FINDEX_INFO_LEVELS.FindExInfoStandard,
    out WIN32_FIND_DATA findData,
    FINDEX_SEARCH_OPS.FindExSearchNameMatch,
    IntPtr.Zero,
    FIND_FIRST_EX_LARGE_FETCH);
```

- `..` entry added up front (parent path via `Directory.GetParent` — a pure path
  computation, not enumeration).
- `FindFirstFileExFromAppW` failure (`INVALID_HANDLE_VALUE`): log warning, return
  just the `..` entry — never throw.
- Runs on a background thread (`await Task.Run`) with a `CancellationToken` checked
  per entry.
- Directories and files collected separately, then merged by sort (below).

### Sorting

Applied in `ColumnListView.LoadAsync` (`Controls/ColumnListView.xaml.cs:340`):

1. Directories first (`OrderBy(IsDirectory ? 0 : 1)`).
2. `..` naturally lands at top (it's a directory; `".."` sorts before letters).
3. Within each group, `StringComparer.OrdinalIgnoreCase` on `Name`.

Archives are not special-cased in sorting — they sort with normal files.

---

## Error Handling

- **Scan failed** → returns `[".."]` only; no exception, no dialog → user can go back.
- **`ApplicationData.Current.LocalFolder` fails** → try/catch, log warning, continue
  without `[App Data]` entry.
- **Drive without permission** → scan returns only `..`, list stays nearly empty.

Philosophy: **never crash** — always have a navigation exit available.

---

## USB Drive Spin-Up Latency

External USB drives on Xbox may sleep/spin-down after inactivity. First access can
take 5–15s while the disk wakes. Loading indicator shown during spin-up; subsequent
navigations are normal. Not a bug — hardware latency.

---

## ACL — Post-Move Permission

Files moved with `MoveFileFromAppW` **lose ACL inheritance** in UWP. They need
`SetSecurityInfo` to grant access to `S-1-15-2-1` (ALL_APPLICATION_PACKAGES) —
handled by the move path in `FileOperations`. (Details in
`dosbox-pure-uwp` `vfs_implementation_uwp.cpp:909-972`.)

---

## Path Normalization

UWP is sensitive to forward slashes. Normalize to backslash:

```csharp
path = path.Replace('/', '\\');
```

---

## `System.IO` vs P/Invoke — When to Use Each

| Context | Use |
|---|---|
| List directory (DirectoryScanner) | P/Invoke `FindFirstFileExFromAppW` |
| Open file for reading/writing | P/Invoke `CreateFile2FromAppW` (`Win32FileStream`) |
| Copy/Move/Delete/Extract/Zip | P/Invoke (`FileOperations`) |
| Pure path math (`GetParent`, `Combine`) | `System.IO` (no enumeration) |
| Get app's LocalFolder | `ApplicationData.Current.LocalFolder` |
| Read file content for preview | `FilePreviewService` (streams via `Win32FileStream`) |

**Known violation pending fix:** `SubtitleDetector.cs:31,37` still uses
`System.IO.Directory.Exists`/`EnumerateFiles` — see `docs/tech-debts/`.

---

## File Actions (`FileOperations`)

`FileSystem/FileOperations.cs` — P/Invoke based (no `StorageFile`), with
`OperationResult` + `OperationProgress` (per-file index, bytes, percent):

- `CopyAsync(source, destDir, progress, token)` + `CopyDirectoryAsync`
- `MoveAsync(source, destDir, progress, token)` + `MoveDirectoryAsync`
- `RenameAsync(path, newName)`
- `DeleteAsync(path)` / `DeleteDirectoryAsync(path)`
- `ExtractAsync` / `ExtractFileAsync` (archives, SharpCompress)
- `CreateFolderAsync(folderPath)`
- `CreateZipAsync(sourcePath | List<string>, zipPath, progress, token)`
- `ScanPathsAsync(paths)` — pre-flight size/count for batch progress
- `ListRecursiveAsync(path)` / `GetSingleRootFolder` / `GetCopyName` — helpers

Operations run on background threads; the UI reports progress via
`IProgress<OperationProgress>` and supports `CancellationToken`. Destination
selection uses `FolderBrowserDialog` (a full directory browser modal), not a
"mode" on the column. Deletes always require explicit confirmation dialog.

---

## Whitelisted Extensions vs Show All

X-Files is a generic file browser: **shows all files**, no extension whitelist
(unlike `dosbox-pure-uwp`, which filters because only emulator-loadable formats
matter). `IsArchive` just enables extra behavior (drill-in), doesn't filter
visibility.
