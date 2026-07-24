<p align="center">
  <img src="docs/social-preview.png" alt="X-Files" width="600" />
</p>

<h1 align="center">X-Files</h1>

<p align="center">
  <strong>🎮 The file manager your Xbox deserves.</strong><br/>
  Navigate, preview, play, and manage your files — all from the couch, all with a gamepad.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Platform-Xbox%20One%20%7C%20Series%20X%7CS-107C10?style=flat-square&logo=xbox" alt="Platform" />
  <img src="https://img.shields.io/badge/Stack-C%23%20%2F%20UWP%20%2F%20XAML-512BD4?style=flat-square&logo=dotnet" alt="Stack" />
  <img src="https://img.shields.io/badge/License-GPL--3.0-green?style=flat-square" alt="License" />
  <img src="https://img.shields.io/badge/Status-Active%20Development-yellow?style=flat-square" alt="Status" />
  <img src="https://img.shields.io/badge/Version-0.9.9-orange?style=flat-square" alt="Version" />
</p>

<p align="center">
  <a href="#-features">Features</a> ·
  <a href="#-controls">Controls</a> ·
  <a href="#-screenshots">Screenshots</a> ·
  <a href="#-getting-started">Get Started</a> ·
  <a href="#-documentation">Docs</a>
</p>

---

## 🤔 Why X-Files?

Xbox has no built-in way to browse files on USB drives, preview images, listen to music,
or manage your media library. **X-Files fills that gap** — a full-featured file manager
built specifically for the couch experience.

<p align="center">
  <em>Miller-column navigation · Live preview · Audio player with VU meter · Archive browsing · Retro aesthetic</em>
</p>

---

## ✨ Features

### 📂 Three-Column File Browser
Browse your files like a pro with a Miller-column layout — **Parent | Current | Preview**.
See where you are, what's here, and what's inside — all at once. Drill into folders with
**A** and back out with **B**.

- 🖥️ Browse all connected drives (internal + USB)
- 📁 Folders-first sorting, alphabetical within type
- 🔒 Hidden/system files filtered automatically
- ⚡ Blazing fast — P/Invoke directory scanning, navigates thousands of files without lag

### 👀 Live Preview
Move the cursor over any file and instantly see its contents — no need to open it first.

| Format | Preview |
|--------|---------|
| 📄 Text / Log / Markdown | Plain text with scroll |
| 🖼️ Images (PNG, JPG, BMP, GIF, WebP) | Thumbnail with size info |
| 🎨 SVG | Rendered in WebView |
| 💻 Code (40+ languages) | Syntax highlighting via highlight.js |
| 🎵 Audio (MP3, FLAC, OGG, WAV) | ID3 metadata + album art + VU meter |
| 🎬 Video (MP4, MKV, AVI) | Inline playback with transport controls |
| 📦 Archives (ZIP, 7Z, RAR) | Browse contents as virtual folders |

### 🎵 Built-in Audio Player
Play music directly from the file browser with a real-time **26-bar spectrum analyzer**.
Pause, seek, skip tracks, adjust volume — all from the gamepad. *Winamp vibes on your Xbox.*

- 🔊 Real-time VU meter with green → yellow → red gradient
- 🎨 Fullscreen mode with album art and track metadata
- 📊 Multiple visualizer modes (cycle with **Select**)
- 🔀 Auto-advance to next track
- 🔉 Volume control via analog stick

### 🎬 Video Player
Watch videos directly from the file browser with a clean fullscreen experience.

- ▶️ Play/pause with **A** button
- ⏪ Seek 5 seconds with **LB/RB**
- 🔊 Volume control via left analog stick
- 📝 Subtitle support (external `.srt` files auto-detected)
- 🔀 Audio track switching (multi-language files)
- 🎯 Clean OSD with transport controls

### 📦 Archive Explorer
Navigate inside **ZIP**, **7Z**, and **RAR** files as if they were folders. Preview text
and images inside archives without extracting. When you do extract, X-Files is smart:

- 🧠 **Smart extraction** — single-root archives extract in-place, multi-root create a folder
- 🔀 **Conflict resolution** — overwrite / overwrite all / skip per file
- 📁 **Extract to folder** or **extract here** — your choice

### 🛠️ File Operations
All the essentials, accessible from the **Y button** context menu:

- 📝 Rename with text input dialog
- 🗑️ Delete with file list confirmation
- 📋 Copy / Move (backend ready)
- 📦 Extract archives to any destination
- 🗜️ Create ZIP from files or folders
- 📁 Create new folder
- 🔄 Refresh current directory

---

## 🎮 Controls

| Button | Action |
|--------|--------|
| 🕹️ **D-pad / Left Stick** | Navigate up/down |
| **D-pad Right / A** | Enter folder · Play file · Toggle play-pause |
| **D-pad Left / B** | Go back · Close fullscreen |
| **LB / LT** | Page up (−8 items) · Seek backward |
| **RB / RT** | Page down (+8 items) · Seek forward |
| **Y** | Context menu (rename, delete, create ZIP, extract...) |
| **X** | Refresh current directory |
| **Right Analog Stick** | Scroll preview · Adjust volume (fullscreen) |
| **Select** | Cycle audio visualizer · Open video track menu |
| **Start** | Settings (coming soon) |

---

## 📸 Screenshots

> *Screenshots coming soon — deployed and tested on real Xbox hardware.*

---

## 🚀 Getting Started

### Prerequisites

- **Xbox One** or **Xbox Series X|S** with **Developer Mode** enabled

That's it. No PC, no Visual Studio, no special tools needed.

### Install

1. Enable **Developer Mode** on your Xbox (install "Dev Home" from Microsoft Store).
2. Download the `.appxbundle` package from [Releases](https://github.com/marcelofrau/x-files-uwp/releases).
3. Open **Xbox Device Portal** from any device on your network:
   ```
   https://<XBOX-IP>:11443
   ```
4. Go to **Apps** → **Add** → select the package → **Install**.

See [DEPLOY-XBOX.md](docs/DEPLOY-XBOX.md) for detailed steps.

---

## 🏗️ Tech Highlights

| Feature | How |
|---------|-----|
| ⚡ Fast directory scanning | P/Invoke `FindFirstFileExFromAppW` + `GetLogicalDrives` — bypasses slow `StorageFolder` APIs |
| 🎮 Gamepad input | Native `Windows.Gaming.Input.Gamepad` polling with edge detection and repeat |
| 🎨 Custom theme | Zero Fluent Design chrome — every control uses custom `ControlTemplate` |
| 🎵 Audio playback | AudioGraph with stream fallback for USB drives + real-time FFT spectrum |
| 📦 Archive support | SharpCompress for ZIP/7Z/RAR — browse without extracting |
| 📝 Syntax highlighting | Inlined highlight.js for 40+ languages (Aco theme) |
| 📋 Logging | Serilog — every operation, input event, and exception logged with daily rotation |

---

## 📚 Documentation

| Doc | What it covers |
|-----|----------------|
| [SPEC.md](docs/SPEC.md) | Functional spec, MVP scope, done criteria |
| [ARCHITECTURE.md](docs/ARCHITECTURE.md) | Layered architecture, data flow, column model |
| [GAMEPAD.md](docs/GAMEPAD.md) | Button mapping, INavigable contract |
| [FILEBROWSER.md](docs/FILEBROWSER.md) | FileEntry model, DirectoryScanner, sorting |
| [ARCHIVES.md](docs/ARCHIVES.md) | ZIP/7Z/RAR via SharpCompress |
| [AUDIO-VISUALIZATION.md](docs/AUDIO-VISUALIZATION.md) | VU meter architecture, AudioGraph, FFT |
| [UI-THEMING.md](docs/UI-THEMING.md) | ControlTemplate conventions |
| [ROADMAP.md](docs/ROADMAP.md) | Phased implementation plan |
| [DECISIONS.md](docs/DECISIONS.md) | ADRs — why XAML, why SharpCompress, etc. |
| [DEPLOY-XBOX.md](docs/DEPLOY-XBOX.md) | Developer Mode, Device Portal, sideload steps |

---

## 🤝 Community

Thanks to the **emulationrevival** community for the support and encouragement:

- **alouisious** — feature ideas and active collaboration on X-Files
- **MewLew**, **Caorthann**, **DanP142** — keeping the homebrew community alive and thriving

---

## 📄 License

[GPL-3.0](LICENSE) — free software; you can redistribute it and/or modify it under the
terms of the GNU General Public License as published by the Free Software Foundation,
either version 3 of the License, or (at your option) any later version.

---

<p align="center">
  Made with ❤️ for the Xbox homebrew community
</p>
