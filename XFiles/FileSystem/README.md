# FileSystem/

Disk access layer, no UI/navigation dependency:

- `FileEntry.cs` — data model (name, path, IsDirectory, IsArchive, size, etc). See
  docs/FILEBROWSER.md. Introduced in Phase 3.
- `DirectoryScanner.cs` — P/Invoke-based directory listing (FindFirstFileExFromAppW +
  GetLogicalDrives), required for browsing drives outside the app sandbox on Xbox. See
  docs/FILEBROWSER.md. Introduced in Phase 3.
- `ArchiveBrowser.cs` — SharpCompress-based zip/7z/rar "virtual folder" browsing. See
  docs/ARCHIVES.md. Introduced in Phase 6.
- `FileOperations.cs` — Copy/Move/Rename/Delete/Extract. See docs/FILEBROWSER.md and
  docs/ARCHIVES.md. Introduced in Phase 7.

Nothing implemented yet — see docs/ROADMAP.md.
