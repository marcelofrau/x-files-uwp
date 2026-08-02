> 🚀 **Neon nights, gamepad-first. Meet NightCity — the new animated skyline visualizer — plus a full on-screen Controls Guide, a visualizer picker, video track improvements, and a stability sweep.**

---

## 🆕 What's new since v1.1.0

### 🏙️ NightCity — new visualizer *(NEW)*
- 🌃 **Layered parallax skyline** — three depth layers of procedural buildings drifting at their own speeds
- 🪟 **Baked window masks** — every building gets pre-rendered windows: always-on lights that breathe slowly, plus *reactive* lights that fill bottom-up like a grouped VU meter synced to the music
- 🏗️ **Varied rooflines** — cell towers, water tanks, antenna clusters, and red aviation beacons blinking on skyscrapers
- 🌉 **Full-horizon bridge** — railing, lamp posts with warm reflections, deck trees, support piers
- 🌊 **Murky river** — displacement-map ripple post-process, moon reflection, shimmer streaks
- 🌙 **Fake-3D moon-lit shading** — lit faces track the moon position with a smooth crossfade as buildings scroll past it, plus side shadows, base ambient occlusion, and edge highlights
- 🚣 **Waterline parallax** — shore silhouettes, avenue, railings, lamps, and trees all scroll at their own slower depth so the foreground reads deeper than the skyline
- 🌉 **4 bridge archetypes** — cable-stayed, arch, suspension, and bowstring rotate each time the river completes a pass
- 🎲 **Deterministic city** — the same skyline every time, so resizes never jump

### 🕹️ Controls Guide *(NEW)*
- 📖 **Start menu → Controls Guide** — full on-screen gamepad reference
- 🎮 **9 sections** — File Browser, Batch Mode, Audio/Video/Image/PDF Fullscreen, Text Editor, Visualizer Picker, Media Player Preview
- 🔘 **Real Xbox button icons** with a full gamepad diagram
- 🕹️ **Stick-scroll** through the guide; **B** or tapping the backdrop closes it

### 🎨 Visualizers
- 📀 **Xbox360BootVisualizer** *(NEW)* — glowing sphere, light rays, and waveform ring
- 📊 **SynthwaveVuMeter** *(NEW)* — 32-bar peak VU with segment gradient and peak-hold gravity
- 📻 **ClassicVU** — two-channel analog VU meter
- 🏔️ **Comanche** — reworked voxel terrain engine: adaptive columns, audio-sculpted heights, greener/browner palette, snow caps, sunset gradient, and bass/beat-reactive cloud particles
- 🌀 **FeedbackTrail** & **WaveformTunnel** — major reworks
- 🏙️ **NightCity** — see above *(NEW)*

### 🎛️ Visualizer UX
- 🖼️ **Visualizer picker overlay** — hold **View** (~500 ms) to browse all visualizers with D-pad, **A** to apply, **B** to close
- 🔄 **Tap View** cycles visualizers without opening the picker
- 📜 Picker supports scrolling and shows the currently selected visualizer
- 🐛 Fixed input routing so the picker no longer swallows other gamepad input

### 📹 Video
- 🎵 **Audio track switch now works** — pausing and re-seeking forces the pipeline to re-evaluate with the new track (confirmed on Xbox)
- 🚫 **Subtitle "Off" option** in the track menu
- 🕹️ **B in fullscreen** — first press hides the controls, second press exits
- 🔁 **Fullscreen closes back** to the inline preview at your saved position
- 🛡️ Boot-chime reference held so the GC can't collect it

### ⭐ Favorites & Search
- ⭐ **Favorites as a virtual folder** — drill into it like any directory, no action sheet on the root
- 🏷️ **Favorite icon indicators** on files and folders
- 🔎 **Start menu search** — jump-to-letter index plus filename search

### 🗂️ File Operations
- 🗜️ **Streaming ZIP** — `CreateZipAsync` streams via `Win32FileStream` instead of loading files into memory
- 🧠 **ArrayPool buffers** and infinite-loop guards in file operations
- 💿 **CDI/GDI mapped to the ISO icon**

### 🔊 Audio Engine
- 🧵 **NoGCRegion tuned to 128 MB** — fixes `TryStartNoGCRegion` failures on Xbox; removed a duplicate UI-side region that caused `InvalidOpException`
- 📉 **Audio stutter fixes** — GC region management, bounds-safe FFT copy, `EndGcRegion` on every path
- 📊 **VU meter fixes** — separate `AttachService`/`DetachService` lifecycle and first-band-data diagnostics
- 🔁 **`SwapSourceAsync`** is now cancel-safe and preserves analysis state
- ⚡ **FFT** — cached Hamming window and a `ComputeMagnitudes` overload
- 🔍 **GC diagnostics** — `GcSnapshot` tracking, app-memory/native-mem logging, render-thread allocation rates

### 🛡️ Stability
- 💥 **Root-cause crash fix** — a missing visualizer entry in the mode-order list, plus `.FirstOrDefault()` defense
- 🧩 **Fixed `IndexOutOfRangeException`** in `AudioData`/`AudioVisualizerBase`
- 🏁 **Fixed a thread race in resize** — visualizer and post-process buffers are now disposed only on the render thread, eliminating "Effect source is null" and `E_INVALIDARG` draw errors
- 🛡️ Null-guard on `Window.Current` and try/catch around the render loop

### 🖼️ Icons & Polish
- 🗂️ **File-type icons upgraded to 128 px** with new types — gzip, tar, iso, jpeg, pbm, svg, tga, C source, executables, archives
- 📐 Larger icons used automatically in big contexts (`GetLargeFileIcon`)

### 🧹 Under the Hood
- ✂️ **MillerColumnsPage** split into focused partial files (Navigation, FileOps, Media, Preview, Error, RomCover)
- 🔩 **SubtitleDetector converted to P/Invoke** — the last `StorageFolder` user in the file browser is gone
- ⚡ 19 `TaskCompletionSource` instances switched to `RunContinuationsAsynchronously`
- 🧹 Removed `DebugOverlay`, `ScreenLogger`, and unused visualizers; debug-only logging flags off in Debug builds
- ✅ Four tech-debt items marked **FIXED**
- 🧪 **New unit tests** for `Formatting`, `HighlightRenderer`, `RomCoverProvider`, and `EncodingDetector`
- 📚 Docs re-synced with the shipped v1.2.0 code

---

## 🎥 Video

Watch the full feature tour: [X-Files — everything new in v1.2.0](https://youtu.be/5RKq1WtARtc)

---

## 📦 Installation

1. 📥 Download the zip file below
2. 📖 Follow the installation instructions in the [README](https://github.com/marcelofrau/x-files-uwp#installation)

---

> 🕹️ Made with ❤️ for the Xbox homebrew community
