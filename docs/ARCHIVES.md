# Compressed Archives — zip / 7z / rar

## Library

**SharpCompress** (NuGet), single dependency for all 3 formats. See `DECISIONS.md`
(ADR-004) for rationale.

Optional fallback: `System.IO.Compression.ZipFile` (.NET native) for pure zip, if
`SharpCompress` has noticeable overhead on large zip files — performance decision to
validate in Phase 6 of the roadmap, not blocking for the MVP.

## `ArchiveBrowser` — Archive as Virtual Folder

Conceptual interface (same listing contract as `DirectoryScanner`, for UI reuse):

```csharp
public interface IArchiveBrowser
{
    // Lists entries in the "directory" at internalPath inside the archive at archivePath.
    // internalPath == "" lists the archive root.
    IReadOnlyList<FileEntry> ListEntries(string archivePath, string internalPath);

    // Opens a read stream for a specific entry (used by Preview/Extraction).
    Stream OpenEntryStream(string archivePath, string internalEntryPath);
}
```

- Format detection by file signature (magic bytes) in addition to extension, to avoid
  false negatives on renamed files — `SharpCompress.Common.ArchiveFactory` already does this
  detection automatically when opening (`ArchiveFactory.Open(stream)`), prefer this API over
  direct `ZipArchive`/`SevenZipArchive` when possible.
- Listed entries become `FileEntry` with `ArchiveRootPath` + `ArchiveInternalPath`
  populated, and `IsDirectory` derived from the archive's own folder structure
  (SharpCompress exposes `IsDirectory` per entry).
- Navigation into a `.zip` inside another `.zip` (nested): best effort —
  open the internal entry stream into a temporary `MemoryStream` and repeat the process.
  Defer to backlog if performance/memory becomes an issue with large files (document
  practical limit, e.g. only nest if inner zip is < 50MB).

## Extraction

Explicit action via `FileActionSheet` → "Extract":
1. User chooses destination folder (reusing navigation in "destination selection mode",
   see `FILEBROWSER.md`).
2. `SharpCompress` extracts all entries (or only the selected entry/folder, if the
   user is browsing inside the archive at the time of the action — partial extraction is
   supported per entry).
3. Progress reported via `IProgress<double>`, displayed in UI (simple bar, without blocking
   navigation of other columns).

## Content Preview Inside Compressed Archives

Same preview logic as a normal file (`ARCHITECTURE.md` → "Live Preview"), but reading
the content goes through `IArchiveBrowser.OpenEntryStream` instead of `File.OpenRead`.
Text/images inside zip/7z/rar should work without extracting to disk first.

## Known Limitations (documented, not bugs)

- `.rar`: read-only (SharpCompress doesn't write rar) — extraction works, creation is not
  an app goal.
- Password-protected files: out of MVP; if detected (`SharpCompress` throws exception
  when trying to read entry), display clear message in Preview column ("Password-protected
  file — not supported"), never crash.
- Multi-volume files (`.7z.001`, `.part1.rar`): out of MVP, same friendly error handling.
