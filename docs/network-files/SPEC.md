---
layout: default
title: Network File Access — Functional Spec
---
# Network File Access — Functional Spec

Scope: SMB-only delivery, as defined in `PLAN.md`. Everything below is written
against the gamepad-first UX of the file browser.

## User stories

### US1 — Browse a remote share
As a user I want to add a network location once and then open it from the root
column, so I can browse my NAS/PC folders with the same column UI as local
drives.

### US2 — Add a location
As a user I want a guided form to describe a remote share (name optional,
server, credentials, share/path, protocol), so I can connect without typing a
UNC path from memory.

### US3 — Manage locations
As a user I want to rename or delete saved locations from the Y context menu,
so I can keep the list tidy.

### US4 — Preview/play remote files
As a user I want to preview text/images/PDFs, play audio/video and render
chiptunes from a remote share without downloading the whole file first, so
browsing a remote media library feels like browsing a local drive.

### US5 — Download from a URL
As a user I want to trigger "Download from URL" from the Network column and
choose the destination folder in the picker, so downloads no longer land in
whatever folder I happen to be in.

## Network column

- Root column gains a **Network** entry (virtual, same injection point as
  Favorites). Selecting it and pressing A drills in.
- The Network listing shows, in order:
  1. **＋ Add location** (action row — always present)
  2. **＋ Download from URL** (action row — always present)
  3. One row per saved location (display name or composed address), sorted
     case-insensitively by display name.
- When there are no locations the two action rows are the only content.
- Action rows are visually distinct from locations (action glyph/style); they
  never show a preview. Confirming an action row runs its action; confirming a
  location connects and lists its shares.

## Location model

A location is a single remote share:

| Field | Type | Required | Notes |
|---|---|---|---|
| `Protocol` | enum (`Smb = 0`) | yes | Dropdown in the form; new protocols slot in here |
| `DisplayName` | string | no | Optional friendly name |
| `Host` | string | yes | IP or hostname |
| `Port` | int | no | Default per protocol (SMB 445) |
| `Username` | string | no | Anonymous shares may be empty |
| `Password` | string | no | → PasswordVault, never in the table |
| `Share` | string | no | Share name/path. Empty = list shares on connect; filled = drill straight into it |

**Composed address / canonical ID**: `{protocol}://{username}@{host}/{share}`
(e.g. `smb://alice@192.168.1.50/music`). When `DisplayName` is empty the list
row and the Y-menu title use the composed address. Host comparison is
case-insensitive; the composed address is the dedup/identity key.

## NetworkLocationDialog (form)

New gamepad dialog. Fields, in order:
1. **Name** (optional) — hint: "leave empty to use smb://user@host/share"
2. **Username**
3. **Password** (masked)
4. **Host / IP**
5. **Port** — pre-filled with protocol default (445); empty/0 = default
6. **Share / path** (optional) — hint: "leave empty to list shares"
7. **Protocol** — dropdown, SMB only for now

Behavior:
- A = confirm, B/Escape = cancel. Empty Host or invalid port → inline error,
  no confirm.
- Reuses the `BladeTheme` templates; focus starts on the first field; A/D-pad
  navigate between fields.
- Edit mode: same dialog pre-filled from the existing location; confirm
  persists changes (and updates the vault entry if username/password changed).

## Y menu on locations

On a saved location row (not on action rows):
- **Rename** — reopens `NetworkLocationDialog` in edit mode.
- **Delete** — confirmation dialog; on confirm, removes the location and its
  vault password; disconnects the session if it was live.
- No file-type actions (copy/rename/delete/etc.) on location rows.

## Download from URL (relocated)

Today the action lives in the file action sheet (`FileActionSheet`) and
downloads into the current local folder only. It moves to the Network column:

1. User selects **＋ Download from URL** and presses A.
2. `FolderBrowserDialog` opens in Folder mode with confirm label **"Download
   Here"** → user picks a destination (local disk only, as today's picker
   already enforces).
3. `InputDialog` asks for the URL.
4. Existing `DownloadService` flow: resolve → stream download with progress →
   success/failure toast; WebView fallback (`UrlDownloadOverlay`) for
   non-direct links (Mega/gofile, browser-required pages).
5. B at any step aborts (picking destination cancels the whole action; no URL
   prompt follows).

The old file-action-menu entries are removed (both branches in
`FileActionSheet`).

## FolderBrowserDialog generalization

`ShowAsync` gains an optional `confirmLabel` parameter (default `null` →
existing "Move Here" behavior unchanged). When provided:
- The confirm virtual row reads "`{confirmLabel}` ({folderName})" / the bare
  label at the root.
- The A-button footer label mirrors the same text.
- The virtual row icon is parameterized too (downloaded location:
  `Assets/Views/FileActionSheet/fileactionsheet-download-48.png`).

Existing callers (Move/copy destination, ROM/playlist file picking) are
unaffected.

## Preview / play model

| Remote type | Path | Detail |
|---|---|---|
| Text | Direct stream | Read via `SmbReadStream`, same size caps as local |
| Image | Direct stream | `BitmapImage.SetSourceAsync` on the stream |
| PDF | Direct stream | `PdfDocument.LoadFromStreamAsync` |
| ROM header | Direct stream | Read first bytes via stream |
| Archive | **Deferred** | Not in this delivery (path-based `ArchiveBrowser` today) |
| Audio (mp3/flac/wav/ogg/m4a/wma/aac) | Streaming | `RemoteStream` (blocking `IRandomAccessStream` over `SmbReadStream`) → `MediaSource.CreateFromStream(stream, mime)` → fullscreen audio surface + VU meter. Consumer is the backpressure; playback starts in ~1–2 s |
| Video | Streaming | `MediaPlayer.SetSource(MediaSource.CreateFromStream(RemoteStream, mime))` | If flaky on Xbox, growing-file / cache fallback — M6 decides |
| Chiptune (PSF/USF/SPC/NSF/VGM/…) | Bytes → render | File is small; read full bytes, feed `RA_Open(data, size, ext, …)`, existing `RetroAudioPlayer` renders to a growing WAV and plays it |

Media limitations (documented, accepted for v1):
- Audio/video **seek** over the socket is a server/seek interplay — M6 decides
  whether to clamp to a buffered region or accept full restart.
- No gapless / auto-next guarantee on remote playlists while streaming
  (navigation between remote files works; each file streams independently).

## Acceptance criteria

Per milestone (see `IMPLEMENTATION.md`). Global must-haves:

1. Every screen reachable with a gamepad only; no mouse/keyboard required.
2. No UI freeze during connect/list/download — worst case a "connecting…"
   indicator plus a timeout error toast.
3. All ops logged through `Log` with `class.method:` prefix.
4. Desktop run: add a location → browse a real SMB share → preview text/image →
   play audio (starts fast) → play video → render a remote chiptune.
5. Xbox Developer Mode: same flow against the same share (port 445 result
   recorded in `IMPLEMENTATION.md` M6). If SMB fails on console, UNC fallback
   is evaluated and the outcome recorded.
6. Settings "Clear Cache" does not delete locations.
7. Unit tests green: `dotnet test tests/XFiles.Tests.csproj`.

## Out of scope (explicit)

- Write-back to remote (copy-to-remote, remote rename/delete/move, remote
  folder create).
- Remote-hosted archives.
- FTP/WebDAV/SFTP in this delivery.
- NFS/DLNA.
- Discovery.
