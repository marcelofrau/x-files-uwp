# File Browser — Data Model and Disk Access

Reference: `dosbox-pure-uwp` (`Content/FileBrowser.cpp`, `vfs_implementation_uwp.cpp`,
`dosbox_uwpMain.cpp`). C++ patterns validated on real Xbox, adapted to C#/XAML.

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

Required header: `#include <fileapifromapp.h>` (C++). In C#, declare via P/Invoke
with `[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]` pointing to the
`FromApp` version.

---

## P/Invoke — Required C# Declarations

```csharp
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct WIN32_FIND_DATA
{
    public uint dwFileAttributes;
    public FILETIME ftCreationTime;
    public FILETIME ftLastAccessTime;
    public FILETIME ftLastWriteTime;
    public uint nFileSizeHigh;
    public uint nFileSizeLow;
    public uint dwReserved0;
    public uint dwReserved1;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
    public string cFileName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
    public string cAlternateFileName;
}

private const uint FIND_FIRST_EX_LARGE_FETCH = 0x00000002;
private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
private const int INVALID_HANDLE_VALUE = -1;

[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
private static extern IntPtr FindFirstFileExFromAppW(
    string lpFileName,
    FINDEX_INFO_LEVELS fInfoLevelId,
    out WIN32_FIND_DATA lpFindFileData,
    FINDEX_SEARCH_OPS fSearchOp,
    IntPtr lpSearchFilter,
    uint dwAdditionalFlags);

[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
private static extern bool FindNextFileW(IntPtr hFindFile, out WIN32_FIND_DATA lpFindFileData);

[DllImport("kernel32.dll", SetLastError = true)]
private static extern bool FindClose(IntPtr hFindFile);

[DllImport("kernel32.dll")]
private static extern uint GetLogicalDrives();

public enum FINDEX_INFO_LEVELS { FindExInfoStandard = 0 }
public enum FINDEX_SEARCH_OPS { FindExSearchNameMatch = 0 }
```

---

## `FileEntry` (Base Model)

```csharp
public class FileEntry
{
    public string Name { get; set; }
    public string FullPath { get; set; }
    public bool IsDirectory { get; set; }
    public bool IsArchive { get; set; }      // .zip/.7z/.rar — treated as "virtual folder"
    public long SizeBytes { get; set; }      // 0 for directories
    public DateTimeOffset? LastModified { get; set; }

    // Only present when the entry lives INSIDE a compressed file:
    public string ArchiveRootPath { get; set; }     // path to the .zip/.7z/.rar on real disk
    public string ArchiveInternalPath { get; set; } // relative path inside the archive
}
```

`IsArchive` is derived from the extension (`.zip`, `.7z`, `.rar`) and makes the item behave like
a folder in navigation (drill-in with A/D-pad right), even though it's physically a file.

---

## `DirectoryScanner`

Responsible for listing the contents of a **real** path (outside compressed files —
that case is handled by `ArchiveBrowser`, see `ARCHIVES.md`).

### Root Level (`path == null`/empty)

```csharp
uint drives = GetLogicalDrives(); // bitmask: bit 0 = A:, bit 1 = B:, etc.
for (int i = 0; i < 26; i++)
{
    if ((drives & (1 << i)) != 0)
    {
        string driveLetter = $"{(char)('A' + i)}:\\";
        entries.Add(new FileEntry { Name = driveLetter, IsDirectory = true });
    }
}
```

- Synthetic `[App Data]` entry pointing to
  `ApplicationData.Current.LocalFolder.Path` (the app's own sandbox, always accessible
  even without `broadFileSystemAccess`).
- No specific USB detection — all drives are listed identically.

### Non-Root Level — Directory Scan

**Do NOT use `StorageFolder.GetFoldersAsync()`/`GetFilesAsync()`** as the primary method —
these APIs require the path to be in the `FutureAccessList` or inside declared
folders, which doesn't cover "any USB drive connected to Xbox".

Pattern validated in `FileBrowser.cpp:207-271`:

```csharp
string searchPath = Path.Combine(path, "*");
IntPtr hFind = FindFirstFileExFromAppW(
    searchPath,
    FINDEX_INFO_LEVELS.FindExInfoStandard,
    out WIN32_FIND_DATA findData,
    FINDEX_SEARCH_OPS.FindExSearchNameMatch,
    IntPtr.Zero,
    FIND_FIRST_EX_LARGE_FETCH);

if (hFind == new IntPtr(INVALID_HANDLE_VALUE))
{
    // Failed (AccessDenied, etc.) — add ".." so user can go back
    entries.Add(new FileEntry { Name = "..", IsDirectory = true });
    return;
}

var dirs = new List<FileEntry>();
var files = new List<FileEntry>();

do
{
    string name = findData.cFileName;
    if (name == "." ) continue;
    if (name == "..")
    {
        dirs.Insert(0, new FileEntry { Name = "..", IsDirectory = true });
        continue;
    }
    bool isDir = (findData.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
    var entry = new FileEntry
    {
        Name = name,
        FullPath = Path.Combine(path, name),
        IsDirectory = isDir,
        SizeBytes = isDir ? 0 : (findData.nFileSizeHigh << 32) | findData.nFileSizeLow
    };
    if (isDir) dirs.Add(entry);
    else files.Add(entry);
}
while (FindNextFileW(hFind, out findData));

FindClose(hFind);
```

### Sorting
1. `..` (go back) always first, when applicable.
2. Directories in alphabetical order (case-insensitive).
3. Files in alphabetical order (case-insensitive).
4. Compressed archives (`IsArchive == true`) are mixed with normal files in
   alphabetical sorting (not in a separate group) — keeps the list predictable.

---

## Error Handling — dosbox-pure-uwp Pattern

Precedent shows graceful/degraded handling:

- **Scan failed** (`INVALID_HANDLE_VALUE`): adds `".."` without throwing exception → user
  can go back. No error dialog shown.
- **`ApplicationData.Current.LocalFolder`** fails: try/catch, log warning, continues without
  `[App Data]` entry.
- **`CreateFile2FromAppW` fails** (`INVALID_HANDLE_VALUE`): returns null/error without crashing.
- **Drive without permission**: scan returns only `".."`, list stays nearly empty.

Philosophy: **never crash** — always have a navigation exit available.

---

## USB Drive Spin-Up Latency

External USB drives on Xbox may enter sleep/spin-down after a period of inactivity.
First access to the drive can take 5-15 seconds while the mechanical disk wakes up or
USB power management responds. Subsequent navigations are normal (drive already active).

**Expected behavior:** visible loading indicator during spin-up, scan returns normally
after drive responds. Not a bug — natural hardware latency.

**Future (post-MVP):** consider background warm-up when detecting drive via `GetLogicalDrives()`,
or pre-loading listing from most recently accessed drive.

---

## ACL — Post-Move Permission

Files moved with `MoveFileFromAppW` **lose ACL inheritance** in UWP. They need
`SetSecurityInfo` to grant access to `S-1-15-2-1` (ALL_APPLICATION_PACKAGES):

```csharp
// Only needed after MoveFileFromAppW. DeleteFileFromAppW doesn't need this.
// Details in vfs_implementation_uwp.cpp:909-972 (function uwp_set_acl).
```

In practice: `Copy + Delete` can avoid this problem (or handle it in `FileOperations`).

---

## Path Normalization

UWP is sensitive to forward slashes. Always normalize to backslash:

```csharp
path = path.Replace('/', '\\');
```

---

## `System.IO` vs `StorageFile` — When to Use Each

| Context | Use |
|---|---|
| List directory (DirectoryScanner) | P/Invoke `FindFirstFileExFromAppW` |
| Open file for reading/writing | `CreateFile2FromAppW` or `System.IO.FileStream` |
| Copy/Move/Delete | `System.IO.File.Copy/Move/Delete` (works with broadFileSystemAccess) |
| Get app's LocalFolder | `ApplicationData.Current.LocalFolder` (standard UWP API) |
| Read file content for preview | `System.IO.StreamReader` / `StorageFile` (both work) |

---

## File Actions (`FileOperations`)

Implemented as simple async operations on real paths (outside compressed files —
inside compressed files, only read/extract makes sense):

```csharp
Task CopyAsync(string sourcePath, string destDir, IProgress<double> progress);
Task MoveAsync(string sourcePath, string destDir);
Task RenameAsync(string path, string newName);
Task DeleteAsync(string path); // mandatory confirmation via dialog before calling
```

- Uses `System.IO` directly (not `StorageFile`) for the same reason as `DirectoryScanner`:
  coverage of paths outside the declared sandbox, with `broadFileSystemAccess`.
- "Copy/Move" operations require a second navigation step (choose destination
  folder) — reuses the same `Current` column component, in a "destination selection
  mode" (flag on the ViewModel, not a new screen).

---

## Whitelisted Extensions vs Show All

Unlike `dosbox-pure-uwp` (which filters by extension because only formats the
emulator can load are relevant), X-Files is a generic file browser: **shows all
files**, no extension whitelist. `IsArchive` just enables extra behavior
(drill-in), doesn't filter visibility.
