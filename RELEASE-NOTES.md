> 🗂️ **The Properties Update.** v1.7.0 adds a full file & folder Properties dialog straight out of your Y-menu — incremental folder size scanning with a live pie chart, a WinRAR-style compression cube, rich media metadata, and a permissions sub-dialog. Plus: remote archive extraction now streams directly (no full download), with a cleaner archive picker and a quieter, more reliable network stack.

---

## 🆕 What's new since v1.6.0

### 🗂️ File / Folder Properties *(NEW)*
- **Y-menu → Properties** for every entry — local files, folders, archive entries, and network files
- **Folder properties** — total size, file & subfolder counts scanned incrementally on a background thread; animated "Reading… N files · size" status with progress bar; **live pie chart** showing the folder's size against the whole drive (real percentage, 2% minimum slice so tiny folders stay visible)
- **WinRAR-style compression view** — for ZIP/7z/RAR archives, an isometric 3D cube shows how much of the file is compressed vs. space saved, with ratio, saved percentage, and uncompressed size
- **Rich file metadata** — audio (MP3/FLAC/WAV/M4A — duration, bitrate, sample rate, channels, codec), images (dimensions), PDF (page count), text files (lines, words, characters) — all parsed locally, no cloud lookups
- **Details** — created / modified / accessed dates and attribute flags (read-only, hidden, system, archive)
- **Permissions sub-dialog** — toggle Read-only / Hidden and optionally apply recursively to subfolders and files
- **Two-column layout** — details on the left, pie chart + compression cube on the right; consistent Oxanium font throughout

### 📦 Remote Archive Extraction *(NEW)*
- **Stream-based extract** — remote archives (SMB/FTP/FTPS/SFTP/WebDAV) extract directly from the network stream; the full-file local download is gone
- **"Extract Here" picker** — destination chooser with the target folder's name on the confirm button, plus visual separators between the action, drives, and folders
- **Smarter pre-scan** — ZIP gets a cheap central-directory size scan for percentage + disk-space check; RAR/7z skip the pre-scan (it costs a seek per entry over FTP) and extract single-pass with filename progress
- **Large-archive deferral card** — remote archives over 128 MB show a "Press A to browse archive contents" card with the A-button hint, downloading to cache with live percentage and cancel-on-navigation

### ⚡ Improvements
- **Quieter FTP logs** — the raw `[FTP VRB]` protocol trace is suppressed at the default verbose level; full trace returns via a debug build flag
- **Chiptune over FTP** — the player now shows the real filename (e.g. `2000AD - Creatures`), not the temporary cache filename
- **Archive drill-in** — shows a spinner immediately; never looks frozen while the remote archive is prepared
- **Documentation refresh** — README, ROADMAP, AGENTS.md and the network docset updated to the shipped v1.7.0 state

---

## 🐛 Bug Fixes
- **Large ZIP preview freeze over network** — hovering large ZIP archives on SMB/FTP/FTPS/SFTP could hang the whole app while the entry listing raced over a slow connection; large-archive previews now defer to a "Press A" card and the listing runs under a 15-second timeout with a local-cache fallback
- **Large archive drill-in hang** — drilling into a large RAR/7z over FTP froze navigation indefinitely (the entry listing had no timeout, and each seek reopened the FTP data connection); drill-in now defers to a background cache download with live percentage, and the listing has the same 15-second timeout with cache fallback
- **0 B archive entries** — files inside archives reported "0 B"; they now fall back to the real stored size
- **Cancel-on-navigate during cache download** — changing selection or navigating while a large archive was downloading no longer breaks the UI (the download cancels cleanly)
- **Properties intermittent blank column** — stale row cache left the details column empty on alternating opens; now cleared correctly
- **Properties crash (RPC_E_WRONG_THREAD)** — folder progress callbacks were dispatched from a threadpool thread and touched XAML; now marshalled through the dispatcher
- **B button / Y button in dialogs** — the dialog handlers compared against `VirtualKey.B` while the input router delivers `GamepadB`; Properties and Permissions dialogs now close and act correctly
- **Video properties access violation** — removed the risky MP4 container read for video; videos show the safe basic properties (progress indicator could not communicate the failure, so keep the file open and retry — but on the next iteration this will be revisited)
- **"Move Here" → "Extract Here"** — the archive picker showed the move label plus a duplicated folder name; extract now shows "Extract Here (folder)"
- **Remote extract stream-at-EOF** — the size pre-scan left the stream at the file tail, so extraction opened garbage and threw; the stream now seeks back to the start
- **Isometric cube depth** — the side face only rendered blue; it now mirrors the green (compressed) / blue (saved) split, and the cube is slimmer
- **Pie caption truncation** — sizes and percentage split across two lines so nothing clips on huge drives

---

## 📦 Installation
1. 📥 Download the zip file below (`xfiles_1.7.0.1520_x64.zip`)
2. 📖 Follow the installation instructions in the README (Developer Mode / Device Portal sideload)

---

> 🕹️ Made with ❤️ for the Xbox homebrew community