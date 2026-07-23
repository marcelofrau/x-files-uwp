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

---

## ADR-009: Win2D for Audio Visualizers (extends ADR-002)

**Context**: ADR-002 decides XAML with custom `ControlTemplate` for the file browser UI.
This remains correct — no D2D for buttons, columns, dialogs. Audio visualizers are a
different case: pixel-perfect rendering, custom HLSL shaders, per-frame control — exactly
what ADR-002 noted "not needed for a file browser."

**Decision**: use Win2D (`CanvasCustomControl` + `PixelShaderEffect`) exclusively for
audio visualizers. File browser UI stays 100% XAML.

**Reason**:
- Win2D is a lightweight D3D11 wrapper, already a transitive dependency (via
  UWPAudioVisualizer)
- HLSL pixel shaders via `PixelShaderEffect` (ShaderModel 4.0, level 9.1+)
- Xbox One supports D3D11 feature level 11.0+ — compatible
- Zero impact on file browser UI — visualizers are isolated in their own
  `CanvasCustomControl`
- `CanvasCustomControl` is a native UWP `FrameworkElement` — integrates with XAML layout,
  gamepad focus still works via XAML's `XYFocus` system

**Accepted risk**: Win2D on Xbox needs hardware validation. Shader compilation is cached
after first frame — first frame may stutter.

---

## ADR-010: Metadata lookup via MusicBrainz (online enrichment)

**Context**: ID3 tags on USB drives are often incomplete — missing genre, year, track#,
album art. The user wanted a "metadata guesser" that fills gaps using online databases
when internet is available, without blocking playback or requiring manual action.

**Decision**: async metadata enrichment via MusicBrainz API + Cover Art Archive, with:
- `FilenameParser` for path-based inference (artist/album from parent folder, track#
  from filename prefix).
- `MetadataCache` (JSON in `LocalFolder/metadata-cache/`, 90-day TTL) to avoid
  repeated network calls.
- Confidence scoring (0.0–1.0) with threshold of 0.5 to accept a match.
- Graceful offline fallback — uses only local ID3 + filename data.

**Rate limiting**: MusicBrainz enforces 1 req/s per IP (hard limit — 100% rejection on
exceed). Implemented via `SemaphoreSlim(1,1)` + 1.1s delay between requests. User-Agent
set to `XFiles/1.0 (contact-url)` to avoid anonymous UA throttling.

**Cover art**: fetched from Cover Art Archive (`coverartarchive.org/release/{mbid}/front-250`)
as a separate async step after recording match. Not rate-limited by MusicBrainz (different
service), but conservatively fetched only when album art is missing from ID3.

**Reason**:
- MusicBrainz is free, no API key, rich data (genre, year, track#, duration, cover art).
- Filename parsing adds value for files with poor/missing ID3 but well-organized folder
  structure (e.g. `Artist - Album/01 - Song.mp3`).
- Cache avoids re-fetching — second access is instant.
- Confidence scoring prevents bad matches from overwriting good local data.

**Rejected alternatives**:
- Last.fm: requires API key, less raw metadata than MusicBrainz.
- Spotify: requires OAuth, more complex integration.
- No cache: wasteful for repeated access to same music library.

**Known limitation**: MusicBrainz doesn't have all obscure recordings. Confidence < 0.5
means "no usable match" — falls back to local data only.

---

## ADR-011: Metadata cache backed by SQLite

**Context**: Phase 11 introduced `MetadataCache` as individual JSON + `.bin` files per song
in `LocalFolder/metadata-cache/`, named by SHA256 hash. Problems:
- Many small files cause filesystem overhead on Xbox.
- SHA256 filenames are not human-debuggable.
- No way to query by partial artist/album.
- No "Clear Cache" option in the UI.
- Cover art stored as separate `.bin` file = two I/O operations per lookup.

**Decision**: Replace file-per-entry with a single SQLite database (`metadata.db`) using
`sqlite-net-pcl` (NuGet). Cover art stored as BLOB in the same table. Single DB file at
`ApplicationData.Current.LocalFolder/metadata.db`.

Schema:
```sql
CREATE TABLE entries (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    cache_key   TEXT UNIQUE NOT NULL,
    artist TEXT, title TEXT, album TEXT,
    genre TEXT, year TEXT, track_num TEXT,
    duration INTEGER, confidence REAL, source TEXT,
    mb_release TEXT, mb_recording TEXT,
    cover_art BLOB, cover_mime TEXT,
    timestamp INTEGER NOT NULL
);
CREATE INDEX idx_artist ON entries(artist);
CREATE INDEX idx_title  ON entries(title);
CREATE INDEX idx_album  ON entries(album);
```

Added `ClearAsync()` method and a Settings page (Start Menu → Settings → Clear Metadata
Cache) with entry count display.

**Reason**:
- SQLite is the standard embedded database for mobile/UWP apps.
- Single file eliminates filesystem overhead.
- Indexed queries enable future features (search, recently played, favorites).
- Cover art BLOB = single I/O per lookup.
- `ClearAsync()` uses `DELETE FROM entries` — simple and atomic.
- `sqlite-net-pcl` by praeclarum is the most proven SQLite library for UWP.
- `SQLiteAsyncConnection` avoids blocking the UI thread.

**Rejected alternatives**:
- Single JSON index file: no dependency but loads entire index into memory for lookups;
  cover art as base64 inflates file size.
- `ApplicationData` composite values: limited to ~8KB per value, no cover art support.
- LiteDB: simpler API but less tested on UWP/Xbox; SQLite has broader ecosystem.

**New files**:
- `Metadata/MetadataCacheDb.cs` — `MetadataCacheEntry` model with SQLite attributes.
- `Settings/XFilesSettings.cs` — wrapper for `ApplicationData.Current.LocalSettings`.
- `Controls/SettingsPage.xaml(.cs)` — Settings UI with cache stats and clear button.
