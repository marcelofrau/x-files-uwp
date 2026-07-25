> **First stable release.** Full-featured file browser, text editor, audio/video player, archive explorer, log viewer with cloud sharing — all controlled by gamepad on your Xbox.

---

## What's new since v0.9.9.513

### Text Editor (NEW)
- Full text editor with WebView + contentEditable + highlight.js syntax highlighting
- 40+ languages with Aco theme — edit code directly on your Xbox
- Block cursor with green highlight, word wrap, dirty state indicator
- System virtual keyboard integration via InputPane
- Gamepad controls: Y=Enter, X=Backspace, A=Virtual Keyboard, Start=Save, B=Close
- Left/right stick for scrolling, DPad for cursor navigation
- Unsaved changes dialog with Save/Discard/Cancel
- Works on files up to 4MB with full editing; larger files open read-only

### Log Viewer with Cloud Sharing (NEW)
- Fullscreen log viewer accessible from Start Menu
- Real-time log level filtering (Verbose/Debug/Info/Warning/Error) persisted to SQLite
- Per-session log rotation — previous sessions archived automatically
- Y button uploads all session logs to gofile.io (permanent hosting)
- QR code generation for instant mobile sharing (ZXing.Net)
- In-memory ZIP compression before upload (~3MB → ~293KB)
- Scrollable log content with DPad, left stick, and right stick

### Navigation
- Breadcrumb path display in header — shows full navigation path with smart truncation for long paths
- Drill-out on ".." selection in parent column
- Preview column no longer shows stale "Loading..." text at root level

### Media Player
- Auto-advance to next/previous track in preview pane (video and audio)
- End-of-playback detection via seekbar position polling
- Windowed player layout with optimized album art and metadata sizing

### Gamepad Input
- DPad and A button now suppress XAML focus navigation sounds
- WebView context menu suppressed (was triggered by Start button on Xbox)

### File Operations
- Copy, Move, Extract, and Create ZIP all working with progress dialogs
- CancellationToken support — operations can be cancelled mid-flight

### Settings
- SQLite-backed settings infrastructure with migration system
- Log level persistence across sessions

### Logging
- Per-session log files — each app start gets its own `xfiles.log`
- Previous sessions archived as `xfiles-{timestamp}-prev.log`
- Maximum 10 archived log files with automatic cleanup
- Win32 I/O for fast log file reading
- Caller method name in all log output

### Bug Fixes
- Fixed cursor jump when highlight.js re-renders text
- Fixed EdgeHTML boolean return in InvokeScriptAsync
- Fixed badge text alignment across all overlays
- Fixed unsaved dialog visual styling to match AlertDialog pattern
- Fixed WebView focus prevention (`AllowFocusOnInteraction=False`)
- Fixed AUDIO_ANALYSIS flag now included in Release builds

---

## All Features

### Three-Column File Browser
Miller-column layout — **Parent | Current | Preview**. Browse all connected drives (internal + USB), folders-first sorting, hidden/system files filtered, blazing fast P/Invoke directory scanning.

### Live Preview
| Format | Preview |
|--------|---------|
| Text / Log / Markdown | Plain text with scroll |
| Images (PNG, JPG, BMP, GIF, WebP) | Thumbnail with size info |
| SVG | Rendered in WebView |
| Code (40+ languages) | Syntax highlighting via highlight.js |
| Audio (MP3, FLAC, OGG, WAV) | ID3 metadata + album art + VU meter |
| Video (MP4, MKV, AVI) | Inline playback with transport controls |
| Archives (ZIP, 7Z, RAR) | Browse contents as virtual folders |

### Built-in Audio Player
Real-time **26-bar spectrum analyzer** with green → yellow → red gradient. Fullscreen mode with album art and track metadata. Multiple visualizer modes (cycle with Select). Auto-advance to next track. Volume control via analog stick.

### Video Player
Play/pause with A, seek with LB/RB, volume via left analog stick. Subtitle support, audio track switching, clean OSD with transport controls.

### Archive Explorer
Navigate inside ZIP, 7Z, and RAR files as if they were folders. Preview text and images inside archives without extracting. Smart extraction with conflict resolution.

### Text Editor
Syntax-highlighted editing for 40+ languages. Block cursor, word wrap, dirty state tracking. System keyboard integration. Gamepad-optimized controls.

### File Operations
Rename, Delete, Copy, Move, Extract archives, Create ZIP, Create new folder, Refresh — all from the Y button context menu.

### Log Viewer
Fullscreen overlay with real-time log level filtering. Share logs via QR code or URL to gofile.io.

### Controls

| Button | Action |
|--------|--------|
| D-pad / Left Stick | Navigate up/down |
| D-pad Right / A | Enter folder / Play / Toggle play-pause |
| D-pad Left / B | Go back / Close fullscreen |
| LB / LT | Page up (−8 items) / Seek backward |
| RB / RT | Page down (+8 items) / Seek forward |
| Y | Context menu / Enter (in text editor) |
| X | Refresh / Backspace (in text editor) |
| Right Analog Stick | Scroll preview / Adjust volume |
| Select | Cycle audio visualizer / Video track menu |
| Start | Save (in text editor) / Open virtual keyboard (A in editor) |

---

## Installation

1. Download the zip file below
2. Follow the installation instructions in the [README](https://github.com/marcelofrau/x-files-uwp#installation)
