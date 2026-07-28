> 🚀 **ROM lovers, batch power-users, and sharing fans — this one's for you.** Cover art from LibRetro, multi-select batch operations, instant file sharing via QR code, and buttery-smooth copy with real progress bars.

---

## 🆕 What's new since v1.0.0

### 🎮 ROM Preview with Cover Art *(NEW)*
- 🕹️ **Smart ROM detection** — peek inside ZIP files to identify ROMs by extension (NES, SNES, GB, N64, NDS, 3DS, Genesis, Saturn, Dreamcast, PS1, PSP, and more)
- 🖼️ **Cover art from LibRetro Thumbnails** — automatically fetches box art, disc art, and screenshots from the LibRetro CDN
- 💾 **SQLite cache** — cover art cached locally for 30 days (both hits AND misses) to avoid hammering servers
- 📋 **Gamelist.xml parser** — reads No-Intro gamelist metadata for title, developer, genre, year, rating, players, and more
- 🎨 **18 retro console icons** — beautiful system-specific icons for every supported platform
- 📊 **System-specific metadata** — shows ROM format, size, country, and system info in the preview panel

### ✅ Batch Selection Mode *(NEW)*
- 🔘 **Select button** toggles batch mode with visual checkboxes
- 📋 **Multi-select** files and folders for batch operations
- 🗑️ **Batch delete** — select multiple items and delete them all at once
- 📦 **Batch move** — move entire selections to any destination
- 🗜️ **Batch ZIP** — compress selected items into a single archive
- 📤 **Batch share** — share multiple files via QR code in one go

### 📤 File & Folder Sharing *(NEW)*
- ☁️ **Instant upload** to gofile.io — files streamed directly, folders zipped on-the-fly
- 📱 **QR code generation** — scan with your phone to download immediately
- 📊 **Upload progress** — real-time progress bar with byte tracking
- 🎯 **Share action** in the Y-button context menu

### 🎨 Visual & UX Improvements
- 🎨 **Per-file-type icons** from Papirus — every file gets a unique, beautiful icon in the context menu
- 🗑️ **Delete button** now shows red background for clarity
- 📦 **Move button** now shows green background for clarity
- 🖼️ **Y button image** in archive preview for consistent visual language

### ⚡ Performance & Reliability
- 🚀 **Streaming copy** — files now copy in 64KB chunks instead of blocking the entire thread
- ❌ **Cancel mid-copy** — hit B to abort a copy/move operation mid-flight (was impossible before)
- 📊 **Accurate progress bars** — pre-scans files before starting, shows both overall and per-file progress
- 🗜️ **Streaming extract** — archive extraction now shows real progress and supports cancel
- 🔄 **Cross-volume move** — move between drives now works with streaming copy fallback

### 🎵 Audio Visualizer Fixes
- 🔥 **InfernoCoreVisualizer2** — flames always visible with intensity floor, fixes IndexOutOfRangeException from concurrent access
- 🎨 Thread-safe particle system with subtle plasma background

---

## 📸 Screenshots

Check out the [48 screenshots](https://github.com/marcelofrau/x-files-uwp#-screenshots) in the README — from splash screen to ROM preview, audio visualizers, batch operations, and more.

---

## 📦 Installation

1. 📥 Download the zip file below
2. 📖 Follow the installation instructions in the [README](https://github.com/marcelofrau/x-files-uwp#installation)

---

> 🕹️ Made with ❤️ for the Xbox homebrew community
