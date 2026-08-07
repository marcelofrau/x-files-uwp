> 🛰️ **Your Xbox, file-abled.** Meet Device Portal — browse the Xbox's own file system straight from X-Files: copy, paste, download, upload, and manage storage across drives, all with the gamepad. Plus a text-editor keyboard overhaul, a Favorites how-to guide, and much faster archive/file operations.

---

## 🆕 What's new since v1.2.0

### 🛰️ Device Portal — Xbox file system access *(NEW)*
- **Probe & connect** — one-shot connection check from About, with saved credentials and re-probe support
- **User folders browser** — drill into known folders, app data, and root containers right from the columns
- **Bidirectional clipboard** — copy/paste between your Xbox file system and local folders
- **Download & upload** — transfer files to and from the console with live progress in the gamepad UI
- **Full entry support** — Y context menu, media/PDF playback, create folder, and file diagnostics on portal entries
- **Multi-volume disk space dialog** — see free space across all mounted drives on portal roots
- **Loopback setup tools** — one-click liberate scripts and quick-start docs to get portal access working
- **Navigation hardening** — drill-in guard on portal entries and stale-preview fixes after refresh

### 📝 Text Editor
- **Fixed the virtual keyboard dance** — the Xbox OSK no longer pops open and immediately hides on the first try (focus is now handed to the input bridge before it slides in)
- **Fixed stuck characters** — typing no longer leaves a stale trailing character; the editor now locates the real insertion point when the OSK caret drifts (e.g. key-page navigation)
- **Caret pinned** — the bridge caret snaps back to the end after each insert, so subsequent keystrokes always append
- **Saving to portal now streams** — the editor no longer loads the whole file into memory before uploading

### ⭐ Favorites
- **How-to guide in the preview column** — the Favorites root now shows a quick reference panel (hold Y to add/remove a favorite, press A to open) instead of a plain folder listing
- **Smarter navigation** — no unnecessary folder scan while you're sitting in the Favorites list

### 📦 Archives & File Operations
- **Streaming ZIP** — archives are written through a 1 MB buffered stream instead of byte arrays, with real progress reporting
- **Cancel is clean** — cancelling a ZIP build throws immediately and deletes the partial file; no half-written archives left behind
- **Chunked copy & extract** — large transfers run in bounded chunks with throttled progress updates (less CPU chatter, smoother UI)
- **Progress without double-dispatch** — removed redundant `Dispatcher.RunAsync` hops on every progress handler
- **Empty archives handled** — a ZIP/7z with zero entries now reads as an empty folder instead of stalling

### ⚡ Performance
- **Streaming portal upload** — multipart bodies stream from disk with a progress stream, so multi-gigabyte uploads never sit in RAM
- **Bigger download buffer** — portal downloads moved from 64 KB to 1 MB reads

### 🎨 UI Polish
- **Oxanium everywhere in the progress dialog** — speed, ETA, and current-file stats now use the app font (no more font soup)
- **Badge baseline alignment** — the Modified/Saved status in the text editor and logs view is vertically centered
- **Crisp favorites icon** — the guide panel now renders from a 128 px source

### 🧹 Under the Hood
- **`GAMEPAD_INPUT_DEBUG` flag** — replaces the old editor-only debug switch, so input tracing is available everywhere behind one compile-time gate
- **Extra ZIP/Win32 logging** to make archive and transfer debugging easier

---

## 📦 Installation

1. 📥 Download the zip file below
2. 📖 Follow the installation instructions in the [README](https://github.com/marcelofrau/x-files-uwp#installation)

---

> 🕹️ Made with ❤️ for the Xbox homebrew community
