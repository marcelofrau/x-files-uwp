> 🔐 **Encrypted Archives, Drive Compatibility & Performance Update.** v1.8.0 adds full password-protected archive support (ZIP, 7z, RAR — browse, preview, and extract), expands Xbox external/system drive compatibility with a multi-backend filesystem layer (`FromApp` → `PlainWin32` → `WinRT` + `removableStorage`), and speeds up navigation across large folders and archives.

---

## 🆕 What's new since v1.7.0

### 🔐 Password-Protected Archives *(NEW)*
- **Browse encrypted archives** — drill into password-protected `.zip`, `.7z`, and `.rar` files (both encrypted headers and encrypted entries) as virtual folders, locally and over network shares
- **On-demand password prompt** — gamepad-friendly masked `PasswordBox` dialog appears automatically when an archive or entry requires a password
- **Session password memory** — once unlocked, the password is kept in memory for the session (hashed in cache keys, never logged or written to disk) so previewing and extracting entries inside the same archive never prompts twice
- **Extract with password** — full-archive and single-file extraction (local, SMB/FTP/SFTP/WebDAV, and Device Portal) prompt for the password on demand and retry seamlessly

### 💾 Xbox Drive Access & Multi-Backend Filesystem *(NEW)*
- **`removableStorage` + `expandedResources` capabilities** — matches UWP homebrew emulators (XBSX2, RetroArch) so external USB drives (`E:\`, etc.) without manual `ALL APPLICATION PACKAGES` ACLs remain accessible
- **Automatic backend fallback (`NativeFs`)** — every directory scan, drive probe, file stream, preview read, folder size scan, and file operation now tries `*FromAppW` first, falls back to direct `kernel32.dll` Win32 (`FindFirstFileExW` / `CreateFileW` under the `runFullTrust` token), and finally `Windows.Storage` (`WinRT`), caching the winning backend per volume root
- **Per-backend drive diagnostics** — startup drive probes log the exact backend that opened each volume (or the last Win32 error code when inaccessible)

### ⚡ Performance Improvements
- **$O(1)$ Archive Entry Index** — `ArchiveBrowser` now builds and caches an in-memory index (`EntriesByPath`, `AllEntryPaths`, `AllDirs`) per open archive instead of rescanning `archive.Entries` on every subfolder navigation or file preview
- **Lock-free $O(1)$ Favorites lookup** — `FavoritesManager.IsFavorite` now reads a volatile case-insensitive `HashSet<string>` instead of locking and scanning the list on every row of all three columns
- **Single-pass refresh preview** — eliminated duplicate `UpdatePreviewColumnAsync` execution during column refresh
- **Bounded local image/SVG preview & immediate native buffer disposal** — local image and SVG previews now enforce `FilePreviewLimits.MaxImageBytes` before reading into memory and dispose intermediate `SoftwareBitmap` buffers immediately in `finally` blocks
- **Reduced XAML image churn** — header protocol icons and ROM system icons reuse existing `BitmapImage` instances when the URI is unchanged

---

## 📦 Installation
1. 📥 Download the zip file below (`xfiles_1.8.0.1527_x64.zip`)
2. 📖 Follow the installation instructions in the README (Developer Mode / Device Portal sideload)

---

> 🕹️ Made with ❤️ for the Xbox homebrew community
