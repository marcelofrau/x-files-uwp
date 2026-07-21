# 🎮 X-Files v0.9.8.285 Beta

> **The file browser your Xbox always needed.**

X-Files is a file manager for Xbox that works entirely with a gamepad. Browse folders on your USB drive, listen to music, view images, watch videos — all from the couch, all with the controller.

No keyboard. No mouse. No touch. Just your controller and the TV. 🛋️

---

## ✨ What's in this beta

### 📁 File browsing

- 🗂️ **Three-column layout** (Parent | Current | Preview) — see where you are, what's here, and what's inside, all at once
- 💾 Access all connected drives: internal storage and USB drives
- 📂 Folders first, then files, sorted alphabetically
- 🔍 Hidden and system files filtered automatically
- ⏭️ Page navigation with LB/RB (jump 8 items at a time)
- 🔄 X button to refresh the current directory

### 👀 Live preview as you navigate

Move the cursor over any file and instantly see its contents — no need to open files just to check what they are.

- 📝 **Text files**: instant preview of .txt, .log, .md, .csv, and more
- 🖼️ **Images**: PNG, JPG, BMP, GIF, WebP with size info — plus SVG rendering
- 💻 **Code**: syntax highlighting for 40+ languages (C#, Python, JavaScript, Rust, HTML, CSS, and many more)
- 🎵 **Audio**: ID3 metadata (title, artist, album, album art) + real-time VU meter
- 🎬 **Video**: inline playback with transport controls
- 📄 **PDF**: view with zoom and page navigation
- 📦 **Archives**: browse .zip, .7z, .rar contents as virtual folders

### 🎧 Built-in audio player

Play music directly from the file browser — no need to switch apps.

- 🎶 Supports MP3, FLAC, WMA, OGG, AAC
- 📊 **Real-time VU meter** with 26 bars and 12 segments
- 🌈 Color gradient: green → yellow → red with peak hold indicators
- ▶️⏸️ Play/pause (A button), seek with LB/RB, volume with right analog stick
- 🖥️ **Fullscreen mode** with album art, track metadata, and transport controls
- ⏭️ Next/previous track navigation in fullscreen
- 🔌 Works on external USB drives (stream fallback for drives where standard APIs fail)

### 🎬 Video player

- 👁️ Inline playback in the preview column — see a preview before going fullscreen
- 🖥️ **Fullscreen mode** with play/pause, seek (5 seconds with LB/RB), and volume control
- 🎯 Simple, clean controls that don't obstruct the video

### 📄 PDF viewer

- 📖 Open PDFs directly in the preview pane — no need for a separate app
- 🔍 **Zoom**: 1x, 2x, 4x (press LB/RB to cycle)
- 📃 Navigate pages with LT/RT
- 🖐️ Pan and zoom with the analog stick

### 📦 Archive browsing

- 🗜️ Open .zip, .7z, and .rar files as if they were folders
- 👀 Preview text files and images inside archives without extracting
- 🗂️ Navigate through archive contents with the same three-column layout

### ⚙️ File operations

- ✏️ **Rename**: Y button → Rename, with validation for illegal characters
- 🗑️ **Delete**: Y button → Delete, with confirmation dialog
- 📋 Context menu for all file operations (Y button)
- 📋 Clipboard support: Copy/Cut multiple files, then Paste to a destination
- 📁 Create new folder with location choice (inside current or next to it)
- 📦 Create zip from selected files
- 📤 Extract archive contents
- 🔄 Progress dialog for file operations

### 🎨 Retro dark theme

- 🌙 Custom dark theme inspired by classic dashboard UIs — no generic Fluent Design
- 🔤 Sharp, clean visuals designed for TV viewing distance
- 🔠 Custom Oxanium font throughout the interface
- 🎮 Organized gamepad button icons for every action

---

## 🛠️ How to install

### 📋 Prerequisites

- **Xbox One** or **Xbox Series X|S** with **Developer Mode** enabled

### 📥 Steps

1. **Enable Developer Mode** on your Xbox
   - Install the "Dev Home" app from Microsoft Store on your Xbox (in Retail mode)
   - Follow the registration flow (free Microsoft developer account)
   - Console restarts in Developer Mode

2. **Download the zip** from this release page

3. **Install the certificate** (first time only)
   - On any device on the same network, open: `https://<XBOX-IP>:11443`
   - Go to **Certificates** → upload the `xfiles.cer` file from the zip

4. **Install the app**
   - In the same Device Portal page, go to **Apps** → **Add**
   - Select the `.msix` file from the zip
   - Click Install

5. **Launch** X-Files from your Dev Mode app list

### 🔄 Alternative: xbHomebrewVault

If you have xbHomebrewVault or similar homebrew installer:

1. Copy the `.msix` package to a USB drive
2. On Xbox, open xbHomebrewVault → Install from USB
3. Select the package → Install

---

## 🎮 Controls

| Button | Action |
|---|---|
| 🕹️ D-pad / Left Stick | Navigate up and down |
| ➡️ D-pad Right / A | Enter folder / Open file / Play / Pause |
| ⬅️ D-pad Left / B | Go back / Close fullscreen |
| LB / RB | Page up / Page down (8 items) |
| Y | Context menu (rename, delete, copy, etc.) |
| X | Refresh current directory |
| 🕹️ Right Analog Stick | Scroll preview / Adjust volume |
| Start | Settings (coming soon) |

---

## 🔮 What's next

### 📁 File operations dropdown (coming soon)

A visual dropdown menu when you press Y on a file or folder:

- ✏️ **Rename** — rename files and folders
- 📋 **Copy** — copy to clipboard, paste elsewhere
- 🗑️ **Delete** — delete with confirmation
- 📦 **Move** — move to another location
- 🗜️ **Create zip** — compress selected files
- 📁 **Create folder** — create a new folder

### 💾 Drive operations dropdown (coming soon)

A visual dropdown menu when you press Y on a drive:

- 🔄 **Refresh** — reload drive contents
- 👁️ **Hide** — hide this drive from the list (manage all hidden drives in Settings)

### 🌐 Other planned features

- 🌐 **Network browsing** (SMB/UNC shares)
- 📝 **Simple text editing**
- ⚙️ **Settings panel** with log viewer and drive management
- 🎨 **More file format support**

---

## ⚠️ Known issues

- 📁 Copy/Move operations currently use a path input (folder picker UI coming soon)
- 🗑️ Deleting large folders does not show a progress bar yet
- 🖼️ Some rare image formats may not render correctly
- 🎵 Audio from very slow USB drives may buffer briefly on first play
- ⚙️ Settings menu is a placeholder (logs viewer, not yet functional)

---

## 🔗 Links

- 📚 [Documentation](https://github.com/marcelofrau/x-files-uwp/tree/main/docs)
- 🐛 [Report issues](https://github.com/marcelofrau/x-files-uwp/issues)
- 💻 [Source code](https://github.com/marcelofrau/x-files-uwp)

---

**X-Files** is free and open source under the GPL-3.0 license. 🎉
