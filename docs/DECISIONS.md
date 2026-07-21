# Architecture Decisions (short ADRs)

Record of decisions made before any line of code, so we don't have to
re-discuss the same trade-offs in the future.

---

## ADR-001: C# + UWP, not C++/CX

**Context**: the sibling project `dosbox-pure-uwp` uses C++/CX because it hosts a legacy C
libretro core and needs tight binary compatibility with DirectX. X-Files has no such
requirement — it's a new app, with no native core to integrate.

**Decision**: C# + pure UWP (no CX, no C++/WinRT).

**Reason**: direct access to `System.IO.Compression`, `SharpCompress`, LINQ, async/await,
much higher productivity for file CRUD and UI. No technical reason to pay the
cost of C++ here.

---

## ADR-002: XAML with custom `ControlTemplate`, not Win2D/D2D

**Context**: `dosbox-pure-uwp` implements all UI (menu, file browser, dialogs) in
imperative Direct2D because it's C++/CX and needs to avoid XAML for compatibility with
legacy C code. We considered replicating this approach for 100% custom visuals
("retro terminal" style, like the `FileBrowser.cpp` from that project).

**Decision**: native XAML, with fully redesigned `ControlTemplate`/`ItemContainerStyle`/
`VisualStateManager` (no default Fluent Design chrome).

**Reason**:
- UWP gives gamepad focus **for free** via `XYFocusUp/Down/Left/Right` and
  `IsFocusEngaged`. Replicating this in D2D means reimplementing hit-test, focus,
  scroll and wrap-around by hand (the `FileBrowser.cpp` from dosbox-pure-uwp has
  ~900 lines just for that).
- A custom `ControlTemplate` achieves visuals identical to D2D (colors, monospaced
  fonts, no Windows borders/chrome) without giving up native focus.
- Less code = less bug surface in an app whose value is in navigation and not in
  the rendering engine.

**Accepted trade-off**: we lose low-level pixel-perfect control (e.g. particle effects,
custom shaders) that D2D would provide. Not needed for a file browser.

---

## ADR-003: Inspired by `yazi` UX, not reusing its code/core

**Context**: the user wanted an experience similar to `yazi` (file manager in Rust,
terminal, Miller columns, live preview).

**Decision**: reimplement the **concept** of Miller columns (Parent | Current | Preview)
and live preview in C#/XAML, without any dependency on yazi's source code (which is Rust,
terminal-oriented, with a Lua plugin system — technology incompatible with UWP/Xbox).

**Project name rationale**: we discarded names like "yazi-uwp" or similar to avoid creating
the expectation of being a real port. The chosen name is **X-Files** (repo: `x-files-uwp`) —
geek reference to the TV series, with no ties to any existing lib/name.

---

## ADR-004: SharpCompress for zip/7z/rar

**Context**: we need to navigate (list entries, "enter" as if it were a folder) inside
`.zip`, `.7z` and `.rar` files without necessarily extracting everything.

**Decision**: use `SharpCompress` (NuGet) as the single library for all 3 formats, with
optional fallback to `System.IO.Compression.ZipFile` (.NET native) for pure zip, if
performance becomes an issue.

**Reason**: a single API covers all 3 formats, avoiding extra native P/Invoke (e.g.
`7z.dll`/`SevenZipSharp`, which depend on per-platform native binaries — problematic in
UWP/Xbox due to sandbox and ARM/x64 architecture).

**Known risk**: `SharpCompress` has **read-only** `.rar` support (doesn't create/edit
rar) — acceptable, since the app is a *browser*, not an archiver.

**CVE-2026-44788 (path traversal in WriteToDirectory)**: SharpCompress <= 0.47.4 has a
zip-slip vulnerability in `IArchive.WriteToDirectory()`. We stay on 0.34.2 (suppressing
NU1902 in csproj) because:
1. We **never call** `WriteToDirectory()` — only `OpenEntryStream()` for in-memory reads.
2. The app runs sandboxed on Xbox with `broadFileSystemAccess`; even if a traversal
   occurred, the UWP sandbox limits write targets to the app's `LocalFolder` and
   explicitly granted directories — not the full filesystem.
3. Upgrading to 0.48.0 pulls `System.Text.Encoding.CodePages >= 8.0.0`, which may
   conflict with UWP's .NET Native toolchain and isn't worth the risk for a
   read-only operation we don't use.
4. Revisiting when SharpCompress 0.49+ drops the extra dependency or when extraction
   support is actually needed (Phase 7 FileOperations/Extract).

---

## ADR-005: No network browsing (SMB/UNC) in MVP

**Context**: `dosbox-pure-uwp` doesn't implement this; it relies only on mapped drives
appearing via `GetLogicalDrives()`.

**Decision**: MVP covers only local/USB drives connected to Xbox and the `LocalFolder`/
`broadFileSystemAccess` sandbox. Explicit `\\server\share` browsing (with
discovery, authentication, etc.) is out of scope, documented as future work in
`ROADMAP.md`.

**Reason**: lean MVP scope; SMB in the UWP Xbox sandbox has additional restrictions (no
easy low-level `Windows.Networking.Sockets`, requires extra `capabilities`) that
deserve dedicated investigation before committing to a timeline.

---

## ADR-006: A button action is contextual, preview is live (no confirmation needed)

**Context**: in `yazi`, moving the selection already updates the preview automatically. We
wanted to keep that fluidity, but also needed an action menu (copy, move, extract,
open with).

**Decision**:
- Moving selection (D-pad/stick) **always** updates the preview column automatically —
  no confirmation needed.
- **A** on a folder = enter it (drill-in, column shift).
- **A** on a file = contextual default action (defined by file type — play audio, open
  fullscreen for video, etc.). For audio files, A toggles play/pause in the preview pane.
- **Y** explicitly opens the `FileActionSheet` (context menu: open with, extract, copy,
  move, rename, delete) — the user doesn't need to "confirm" just to *see* the preview.

**Reason**: clearly separates "looking" (automatic preview, no cost) from "acting" (explicit
context menu), preventing accidental destructive actions with the most-used button (A).

---

## ADR-007: AudioGraph with MediaSourceAudioInputNode for Xbox stream fallback

**Context**: `AudioGraph.CreateFileInputNodeAsync(StorageFile)` requires a `StorageFile`
object. On Xbox, `StorageFile.GetFileFromPathAsync()` and
`StorageFolder.GetFolderFromPathAsync()` both fail with `E_ACCESSDENIED` for files on
external USB drives, even with `broadFileSystemAccess` capability declared in the manifest.
`FileStream` (managed I/O) works fine for reading file bytes (proven by ID3Tag parsing),
but `AudioGraph` has no `CreateFileInputNodeAsync(IInputStream)` overload.

**Decision**: try `StorageFile` first (two methods: folder+GetFileAsync, then direct
GetFileFromPathAsync). If both fail, fall back to `FileStream` → `IRandomAccessStream` →
`MediaSource.CreateFromStream()` → `AudioGraph.CreateMediaSourceAudioInputNodeAsync()`.
Both paths share the same AudioGraph, `AudioDeviceOutputNode`, and `AudioFrameOutputNode`
for playback + VU meter FFT analysis.

**Reason**: `MediaSourceAudioInputNode` is the bridge between stream-based sources and
AudioGraph. Created via `AudioGraph.CreateMediaSourceAudioInputNodeAsync(MediaSource)`,
it accepts any `MediaSource` — including one backed by `IRandomAccessStream`. This means:
- VU meter works regardless of whether the file is accessed via StorageFile or FileStream.
- No `MediaPlayer` fallback needed (which would lose VU meter capability).
- Single audio engine (AudioGraph) handles both playback and analysis.

**Rejected approach**: `MediaPlayer` + `MediaSource.CreateFromStream()` for playback, with
no VU meter in fallback mode. This was the initial implementation but was replaced because
the VU meter is a key UX feature that should work on all storage backends.

**Key API**: `CreateMediaSourceAudioInputNodeResult` has `.Status` and `.Node`
(`MediaSourceAudioInputNode`). The node has `.Start()`, `.Seek(TimeSpan)`, `.Position`,
`.Duration`, and `.AddOutgoingConnection()` — same interface pattern as
`AudioFileInputNode`.

---

## ADR-008: Unified AudioEngine for fullscreen audio (no MediaPlayer duplicate)

**Context**: fullscreen audio originally used two engines simultaneously —
`MediaPlayer` (`FsAudioPlayer`) for playback and `AudioLevelService` (AudioGraph) for
VU meter analysis. This caused:
1. Music playing twice (both engines decoding and outputting the same file).
2. Pause only pausing one engine.
3. Track navigation out of sync between the two engines.

**Decision**: use a single `AudioLevelService` instance for fullscreen audio, handling
both playback (via `LoadAndPlay()`) and VU meter analysis. Remove `MediaPlayer` from the
fullscreen audio path entirely.

**Reason**: one engine = one audio output, one position state, one pause/resume/seek.
The `AudioLevelService.LoadAndPlay()` creates an `AudioGraph` with `AudioDeviceOutputNode`
(for hardware clock + speakers) and `AudioFrameOutputNode` (for FFT/VU meter), all
synchronized by the same graph clock. No possibility of drift or double-play.
