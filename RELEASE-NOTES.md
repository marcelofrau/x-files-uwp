> 🌐 **The Network Update.** v1.6.0 brings full network file browsing — SMB, FTP/FTPS, SFTP, and WebDAV — with streaming preview, remote file operations, download-from-URL, QR file sharing, and a protocol-agnostic provider layer that made it all possible.

---

## ✨ New Features

### 🌐 Network File Browsing *(NEW)*
- **SMB browsing** — browse, preview, copy, move, rename, delete files on Windows/Samba shares
- **FTP/FTPS** — connect to FTP servers with implicit (port 990) and explicit TLS support
- **SFTP** — secure shell file transfer with host key verification and trust-on-first-use
- **WebDAV** — browse and transfer files from WebDAV servers
- **Protocol-agnostic provider layer** — all network protocols share a common `INetworkFileSystemProvider` interface; adding a new protocol automatically gets full feature support

### 📁 Remote File Operations
- **Streaming preview** — text, image, audio, and video previews stream directly from the remote server without full download
- **Copy/paste between local and remote** — transfer files in either direction with live progress
- **Rename and delete** — remote file management with confirmation dialogs
- **Batch operations** — select multiple files across network locations for bulk copy, move, or delete

### 🔗 Download from URL
- **Browser overlay** — paste a URL, the app opens an in-app WebView; when the page triggers a download, it captures it automatically
- **Provider resolution** — Google Drive, OneDrive, Dropbox, and gofile.io links resolve to direct downloads
- **Filename extraction** — resolves the real filename from Content-Disposition headers and API metadata

### 📱 QR File Sharing
- **Share files via QR code** — upload to gofile.io and display a QR code for easy sharing

### ⚙️ Improvements
- **Breadcrumb protocol icons** — each network location shows its protocol icon in the address bar
- **Write-permission alerts** — clear feedback when a folder is read-only
- **Audio settings bar** — quick access to volume and visualization settings
- **Comprehensive logging audit** — adjusted log levels across the codebase for cleaner diagnostics

---

## 🐛 Bug Fixes
- **FTP navigation freeze** — resolved deadlock when navigating FTP servers with auto-advance cascade
- **Text editor BOM preservation** — save no longer strips BOM from JSON config files
- **Batch delete count** — confirmation dialog now correctly counts remote files
- **Network icon consistency** — left column shows correct protocol icon per location
- **Password field overlap** — removed conflicting placeholder text in network location dialog
- **Preview timeout** — large text files on slow network connections no longer hang the preview pane
- **Create folder on network** — now prompts for folder name instead of silently failing
- **Download overlay layout** — empty WebView row no longer leaves a large gap after download starts
- **ComboBox selection colors** — protocol dropdown no longer persists green highlight on previous selections

---

## 📦 Installation

1. 📥 Download the zip file below
2. 📖 Follow the installation instructions in the [README](https://github.com/marcelofrau/x-files-uwp#installation)

---

> 🕹️ Made with ❤️ for the Xbox homebrew community
