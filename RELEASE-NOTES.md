> 🎮 **Chiptune playback + background music.** v1.5.0 brings native console-music playback (PSF, USF, SPC, GBS, NSF, VGM/VGZ, SID, trackers) with streaming play-while-render, and a background-music feature with a bundled default track — all gamepad-first, all built for Xbox.

---

## ✨ New Features

### 🎵 Chiptune Player *(NEW)*
- **Native chiptune decoding** — game-music-emu + libopenmpt + aosdk PSF + lazyusf backends play the classics: `.psf/.minipsf` (PS1), `.usf/.miniusf` (N64), `.spc` (SNES), `.gbs` (GB), `.nsf` (NES), `.vgm/.vgz` (Mega Drive/MS), `.sid` (C64), plus tracker formats via libopenmpt
- **Streaming playback** — music starts ~1s after the render begins (play-while-render) instead of waiting for the full decode; the WAV cache fills in the background
- **Fixed white-noise corruption** — a process-wide native session lock serializes emulator sessions, eliminating the -40dB hiss that leaked between concurrent renders
- **Fullscreen player** — fast next/prev via graph reuse, loading spinner, PSF tempo corrected to match the original games
- **Format-consistent WAV cache** — salted cache keys, auto-re-render when the renderer improves

### 🎼 Background Music *(NEW)*
- **Always-on BGM** with its own audio graph — keeps playing alongside browsing; disable anytime in Settings
- **Bundled default track** — "17 Stickerbrush Symphony" (Donkey Kong Country 2) ships with the app and streams from the first boot; no big install payload
- **Pick your own track** — file picker accepts 49 audio/chiptune formats; chiptune tracks render in the background with a spinner
- **Polished playback** — 2-3s gap between loop repeats, volume presets (10/25/50/75/100%), auto-pause while music/video plays, 10s cooldown resume, fade-in synced after the boot chime

### ⚙️ Settings & Drives
- **Hide empty/inaccessible drives** (default ON) — system XVD mounts (S:, Q:, ...) that the UWP sandbox can't read no longer clutter the root list; probes run in the background so browsing stays instant
- **Settings reorganized into submenus** with scroll — Clear Data, Log Level, Background Music and Hide Empty Drives group cleanly
- **Improved controls guide** overlay layout

### 📁 Files / Portal / Editor
- **Transfer metering + failure diagnostics** — live speed chart, smoother progress, clearer errors in transfer dialogs
- **File operation robustness** — Win32 read/write stream fixes, restored `..` parent entry in the move picker
- **Text editor polish** — Consolas font, themed dialogs

---

## 📦 Installation

1. 📥 Download the zip file below
2. 📖 Follow the installation instructions in the [README](https://github.com/marcelofrau/x-files-uwp#installation)

---

> 🕹️ Made with ❤️ for the Xbox homebrew community
