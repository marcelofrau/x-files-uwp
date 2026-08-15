---
layout: default
title: Network File Access
---
# Network File Access

Browsing of remote file shares (SMB first, then FTP/WebDAV/SFTP) from the
gamepad-first file browser. A **Network** virtual entry at the root column
hosts user-saved locations ("Network locations"), each pointing at a remote
share. Remote files are listed, previewed and played over the wire — no
full-download wait for media.

Status: **Planned** — M0 (this docset) in progress. Nothing implemented yet.
Track progress in `IMPLEMENTATION.md`.

## Feature at a glance

- **Network column**: virtual "Network" entry in the root column, next to
  Favorites and User Folders. Drilling in lists saved locations plus two action
  rows: "Add location" and "Download from URL".
- **Locations**: each location is one remote share (server + optional display
  name + credentials). Display name is optional — when omitted the entry is
  named by its composed address, e.g. `smb://user@192.168.1.50/music`.
- **Protocols**: SMB is the only protocol in this delivery. FTP/FTPS, WebDAV
  and SFTP are designed for (per-`Protocol` column + `INetworkFileSystemProvider`
  interface) but not implemented yet. NFS and DLNA are out of scope (see
  `PLAN.md`).
- **Navigation**: drill into a location → connect → list shares → browse the
  remote tree with the normal column UX. Drill-out, preview and media follow
  the existing portal/archive "virtual folder" precedent.
- **Preview/play**: text, image, PDF, ROM and archive read directly from the
  remote stream (zero local copy). Audio streams into a growing local temp
  file (the chiptune growing-file pattern) so playback starts in ~1–2 s
  without waiting for a full download. Video streams via `MediaPlayer`.
  Remote chiptunes are read as bytes and rendered by the existing
  `RetroAudioPlayer`.
- **Download from URL**: relocated from the file action menu into the Network
  column. It asks for a destination folder first (via a generalized
  `FolderBrowserDialog` whose confirm label becomes "Download Here"), then the
  URL, then streams the file through the existing `DownloadService`.
- **Credentials**: non-secret location fields live in a dedicated SQLite table
  (`NetworkServerEntry`, `metadata.db`); passwords live in
  `Windows.Security.Credentials.PasswordVault`, keyed by the canonical URL.

## Docs

| Doc | Purpose |
|---|---|
| `README.md` | This file — feature overview, doc map, cross-cutting notes |
| `PLAN.md` | Vision, scope (in/out), protocol matrix, milestones M0–M7, risks |
| `SPEC.md` | Functional requirements, user stories, acceptance criteria |
| `ARCHITECTURE.md` | Layer/service design, data model, navigation/preview wiring, integration points |
| `DECISIONS.md` | ADRs — why SQLite table, PasswordVault, SMBLibrary, growing-file audio, etc. |
| `IMPLEMENTATION.md` | Step-by-step checklist with status (between-session tracking) |
| `gemini-chat-*.md` | Original research conversation (kept as source material) |

Related: `../FILE-SHARES.md` is the older SMB/UNC feasibility assessment — it
predates this design and is **outdated** (its claimed missing capabilities
already exist; its pure-UNC proposal was superseded by the socket-based
SMBLibrary approach). It remains useful for the Xbox unknowns list.

## Cross-cutting notes

- **Gamepad-first**: every interactive control (dialogs, picker, action sheet)
  must use the existing custom templates from `Theming/BladeTheme.xaml`
  (ADR-002). No default Fluent chrome. `XYFocus`/gamepad handling mandatory.
- **No UI blocking**: every network operation is async with an explicit timeout
  and a `CancellationToken`; the Xbox UI tick floors at ~50 ms and a denied
  probe already cost a reported D-pad freeze (see `docs/DECISIONS.md` #180
  notes) — never run socket work on the UI thread.
- **Logging**: all operations via the central `Log` class with
  `class.method:` prefixes (`Log.Info` connect/disconnect, `Log.Dbg` per
  listing, `Log.Err` on failures). Never swallow exceptions.
- **SQLite storage**: locations live in `metadata.db` (the existing
  `SQLiteAsyncConnection`, migration framework v2→v3). The Settings "Clear
  Cache" action does NOT wipe config tables (`MetadataCache.ClearCacheAsync`
  only clears `MetadataCacheEntry` + `CoverArtEntry`).
- **csproj**: `XFiles.csproj` uses explicit `<Compile>` item lists — every new
  `.cs` file in `XFiles/Network/` and every new control must be added manually
  or it is silently excluded from the build.
- **English**: all code, comments, docs and commits in English.
- **Unit tests**: pure logic (URL composition, config serialization, provider
  parsing) must live in linkable helper classes tested from `tests/` (net8.0).
