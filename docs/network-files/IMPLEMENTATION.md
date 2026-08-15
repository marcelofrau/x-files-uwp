---
layout: default
title: Network File Access — Implementation Checklist
---
# Network File Access — Implementation Checklist

Tracking doc. Update status as you go — this is the single source of truth for
between-session continuity. Legend: `[ ]` todo · `[x]` done · `[~]` in progress
· `[!]` blocked (note why).

## Cross-cutting rules (apply to EVERY task)

1. Add every new `.cs` file to `XFiles.csproj` (explicit item lists — memory
   #167). Forgetting silently excludes it from the build.
2. Every interactive control uses `BladeTheme` templates + gamepad handling
   (ADR-002). No default Fluent chrome.
3. Log everything via `Log` (`Info`/`Dbg`/`Verb`/`Warn`/`Err`), prefix
   `class.method:`. Never swallow exceptions.
4. Network ops: `CancellationToken` + explicit timeout; never on the UI thread.
5. Build verification after structural changes:
   `& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" "XFiles.sln" /p:Configuration=Debug /p:Platform=x64 /t:Build /v:minimal`
6. Unit tests: pure logic only, linked into `tests/` (net8.0):
   `dotnet test tests/XFiles.Tests.csproj`

---

## M0 — Docs & scaffolding
*Status: `[x]` complete · Started 2026-08-15*

- [x] `README.md`, `PLAN.md`, `SPEC.md`, `ARCHITECTURE.md`, `DECISIONS.md`,
      `IMPLEMENTATION.md` written in `docs/network-files/`
- [x] Verified manifest capabilities present (`internetClient`,
      `internetClientServer`, `privateNetworkClientServer`)
- [x] `TalAloni.SMBLibrary` identified (1.5.x, netstandard2.0, LGPL-3.0)
- [x] Register docset in `AGENTS.md` docs table
- [ ] *(optional)* update `docs/FILE-SHARES.md` header to point at this docset
      as its successor

**Done note**: design settled per `DECISIONS.md`. Next session starts at M1.

---

## M1 — Data layer
*Status: `[x]` complete · 2026-08-15*

**Goal**: location config persisted (SQLite table + PasswordVault) with unit
tests.

- [x] Add `TalAloni.SMBLibrary` to `XFiles.csproj` (verify restore on desktop)
- [x] `XFiles/Network/NetworkProtocol.cs` — enum `{ Smb = 0 }`
- [x] `XFiles/Network/NetworkServerConfig.cs` — pure model (Protocol, DisplayName,
      Host, Port, Username, Share) — no password field
- [x] `XFiles/Network/NetworkUrl.cs` — pure helpers: `Compose`, `ParseCanonical`,
      `VaultResource`, `DefaultPort` (SMB 445)
- [x] `NetworkServerEntry` schema class (in `MetadataCacheDb.cs`, matching the
      existing entry classes)
- [x] `MetadataCache`: bump `CurrentSchemaVersion` 2→3; add
      `CreateTableAsync<NetworkServerEntry>()` in `RunMigrationsAsync`
- [x] `XFiles/Network/NetworkServerManager.cs` — `GetAllAsync`, `AddAsync`,
      `UpdateAsync`, `RemoveAsync`, `GetPasswordAsync`, `SetPasswordAsync`
      (PasswordVault, resource = `CanonicalUrl`)
- [x] Unit tests in `tests/`: `NetworkUrl` compose/parse matrix (username empty,
      share empty, host case); `NetworkServerStore` CRUD against in-memory
      SQLite (add/dedup/rename/delete/remove-vault-key)
- [x] Verify: `dotnet test tests/XFiles.Tests.csproj` green (224 passed) +
      full MSBuild Debug/x64 green

**Done note**: store is `NetworkServerStore` (injectable, pure — linkable into
tests) with the manager as a thin facade over it + PasswordVault. sqlite-net
1.9.172 pools connections by connection-string (`SQLiteConnectionPool`, static,
internal) — all `:memory:` share one DB in a process; tests clear the table per
test. `MetadataCache.GetDbAsync()` is now public so both the cache and the
network manager share the singleton connection.

---

## M2 — SMB core
*Status: `[x]` complete · 2026-08-15*

**Goal**: SMB socket plumbing — session, listing, readable stream.

- [x] `XFiles/Network/INetworkFileSystemProvider.cs` — contract per
      `ARCHITECTURE.md` (`ListSharesAsync`, `ListDirectoryAsync`, `OpenReadAsync`,
      `GetFileLengthAsync`, `Disconnect`)
- [x] `XFiles/Network/NetworkFileEntry.cs` — `{ Name, IsDirectory, Size,
      LastWriteTime }`
- [x] `XFiles/Network/NetworkOperationException.cs` — `Reason` enum
      (`TimedOut`, `AccessDenied`, `Unreachable`, `AuthFailed`, `Cancelled`,
      `NotFound`)
- [x] `XFiles/Network/SmbSession.cs` — pool keyed by canonical URL; `Connect`
      (DirectTCPTransport) → `Login` → `TreeConnect` → `ISMBFileStore`; 10 s
      timeout (both the SMB2Client ctor response timeout AND a `Task.WhenAny`
      belt-and-suspenders wrapper); `CancellationToken` plumbing; `Disconnect`
- [x] `XFiles/Network/SmbBrowser.cs` — `ListSharesAsync`, `ListDirectoryAsync`
      (share-root listing with `FILE_DIRECTORY_FILE`), NTStatus →
      `NetworkOperationException` mapping; resolves password from vault;
      all logging lives here (SmbSession is Log-free so it links into tests)
- [x] `XFiles/Network/SmbReadStream.cs` — `Stream` over
      `ISMBFileStore.ReadFile` (offset-based, chunked at MaxReadSize);
      `CloseFile` on dispose
- [x] Desktop smoke: `tests/SmbSessionSmokeTests.cs` — real-share test gated by
      env vars `X_FILES_SMB_HOST`/`_USER`/`_PASS`/`_SHARE` (skips as
      Inconclusive otherwise); exercises connect → list shares → list dir →
      read first bytes. **Run 2026-08-15 against a real Windows SMB server
      (10.0.0.20, share "Media"): PASS** — connect, login, TreeConnect, listing,
      open + first read all succeeded (~99 ms)
- [x] Verify: build + unit tests green (225 tests — smoke skipped)

**Field notes (API gotchas found while implementing)**:
- `ISMBFileStore` exposes only `Disconnect`/`MaxReadSize`/`MaxWriteSize` — the
  file ops (`CreateFile`, `QueryDirectory`, `ReadFile`, `CloseFile`,
  `GetFileInformation`) come from the base `INTFileStore` interface, so calls
  work on the returned `ISMBFileStore` reference with no cast.
- `ISMBFileStore` and `SMB2Client` live in `SMBLibrary.Client`; `ISMBFileStore`
  is NOT visible with `using SMBLibrary;` alone.
- `client.ListShares(out NTStatus status)` **returns** `List<string>`; the
  `out` parameter is the status (same pattern for `TreeConnect`).
- Share-root directory listing: `CreateFile(path: "")` + `QueryDirectory("*")`.
  Subdirectories use backslash-separated paths with no leading/trailing
  backslash. `"."`/`".."` are filtered defensively.
- `SMB2Client(int responseTimeoutInMilliseconds)` — default 5000; M2 passes
  10000.
- `NetworkOperationReason` gained `NotFound` (not in the M0 docs) — needed to
  distinguish "share/path gone" from "server unreachable" for the error UI.
- Per-op serialization: a `SemaphoreSlim(1,1)` gate inside the session; a
  timed-out op invalidates the session (removed from pool, recreated on next
  acquire) so a hung socket never wedges later operations.

---

## M3 — Navigation
*Status: `[x]` DONE — build 1.5.0.1320, 224 tests green (smoke skipped)*

**Goal**: Network column browsable with gamepad; locations CRUD via UI.

- [x] `FileEntry`: add `IsNetwork`, `ActionKind { None, AddLocation, DownloadUrl }`,
      `NetworkServerId`, `NetworkShareName`, `NetworkPath` (FullPath stays null
      for network entries)
- [x] `ColumnNavigator`: inject "Network" virtual entry at root (favorites hook)
      — Favorites(0) / User Folders(1) / Network(2) / separator(3) / AppData
- [x] `ColumnNavigator`: network drill state machine — locations list → shares →
      remote tree → preview; drill-out via own stack; loading state + timeout
      toast on connect failure
- [x] `Controls/NetworkLocationDialog.xaml(.cs)` — form (name, user, pass, host,
      port, share, protocol dropdown), BladeTheme templates, gamepad nav,
      TCS result, edit mode pre-filled
- [x] Action row `AddLocation` → dialog → `NetworkServerManager.AddAsync`
- [x] `FileActionSheet`: branch for network location rows → `RenameLocation`,
      `DeleteLocation` (new `FileAction` values); Rename → dialog pre-filled;
      Delete → confirm dialog → remove + disconnect
- [x] Location list shows display name or composed address
- [x] `MillerColumnsPage`/`FileOps`: handlers for the two new actions
- [x] Verify: desktop — add a location against a real share, browse shares and
      files, drill out; Y-menu rename/delete; build + unit tests green

Notes:
- `NetworkServerConfig` gained `Id` (row id, 0 = not persisted) — needed by the
  Y-menu rename/delete path and by `NetworkServerManager.UpdateAsync(id, ...)`.
- Network entries: locations + remote dirs render the themed **network folder**
  variant `folder-{color}-network-24.png` (default `folder-orange-network-24.png`;
  Papirus folder SVGs with the globe in the middle, converted 24 px for all 9
  supported colors — matching the plain-folder color system so a future folder
  theme switch covers them too). The active color is the static
  `EntryViewModel._folderColor`. The `Add location` action row renders
  `filetype-network-add-location-24.png` and `Download from URL` renders
  `filetype-network-download-24.png` (Icons8, see `docs/ATTRIBUTIONS.md`).
- Network locations column preview = **contextual guide** (`NetworkGuidePanel`,
  reworked 1.6.0.1333): reacts to the selected row in the locations column —
  `..` shows a general overview with three quick-tip cards (shared folders /
  direct downloads / saved locations); `Add location` shows a focused card with
  the `smb://[user]:[pass]@[host]/[share]` syntax; `Download from URL` shows a
  focused card with the URL-first flow; a saved server shows a **details card**
  (protocol, host, user, share — loaded via `NetworkServerManager.GetAsync`).
  Card icons are 128 px copies from the personal Icons8 set
  (`mainpage-network-{add,download,server}-128.png`, quick tips at 32 px) so
  nothing is upscaled; the list-row icons stay untouched. The locations column
  never had a preview before (it was a silent `_preview = null`); shares/tree
  columns still live-list their children.
- Preview (M3 scope): locations column shows the how-to guide; shares/tree columns
  live-list their children; remote files show a metadata card ("preview/play lands
  in a later milestone"). Real stream preview is M5.
- OnConfirm on a remote file shows an info toast (avoids the full-path media
  pipeline crash on null `FullPath`).
- `NetworkServerConfig` password stays out of the dialog/result — the form returns
  `NetworkLocationResult { Config, Password, PasswordEdited }`; empty password in
  edit mode = keep the vault entry.
- **`NetworkLocationDialog` is a 3-step wizard** (rebuilt 1.5.0.1318 to fit above
  the Xbox on-screen keyboard, which covers ~1/3 of the screen bottom):
  step 1 Connection (Name | Protocol side by side, IP/Host) → step 2 Login
  (User + Password) → step 3 Folder (Share + live `smb://user@host/share`
  preview). Top-anchored dialog (Margin top 72), compact — never under the
  keyboard. Footer: **Test** (real SMB connect — host+user+pass+share as typed —
  lists shares / opens the share, inline pass/fail), **Cancel**, **Save**.
- Wizard refinements (1.5.0.1319, Xbox hardware feedback): protocol is a real
  `ComboBox` pre-populated with all planned protocols — **Windows Share (SMB)**,
  FTP, sFTP, WebDAV (`NetworkProtocol` enum grew `Ftp`/`Sftp`/`Webdav`; only SMB
  connects today). **Test** is visible only on the last step; the primary button
  reads **Next** on steps 1-2 and **Save** on step 3 (host is required before
  leaving step 1). The combo is `RequestedTheme="Dark"` so its dropdown flyout
  resolves the dark `ComboBoxItem*` theme brushes from BladeTheme (the app runs
  under the system theme; without it the popup renders light).
- **Gamepad interaction model (1.6.0.1323, Xbox hardware feedback): only the
  text fields + protocol combo take focus — buttons never do.** Start cycles
  fields; after the last field it drops focus (invisible `FocusSink` ContentControl
  + `InputPane.TryHide()`) so the OSK closes and **A/B take over the steps**:
  A = next step (or Save on step 3), B = back a step (or cancel on step 1).
  B on an open keyboard closes it first; **X runs Test on the last step**. The
  combo's A is "smart": it opens/closes the dropdown flyout for selection
  (D-pad moves items) instead of advancing the step.
- **Input leak fix (1.5.0.1319)**: the dialog was not registered in `InputRouter`,
  so Start/Y/X/View pressed in the modal leaked to the page behind. Fixed by
  registering an `OverlayHandler` (priority 77) that routes D-pad + all buttons
  into `NetworkLocationDialog.HandleDPad`/`HandleButton` (which consume
  everything while visible); `OnOverlayKeyDown` also consumes the unhandled
  gamepad keys on the key-event path.
- Floating labels: fields are compact `PlaceholderText` inputs; a small caption
  chip (Border + TextBlock, `FloatingFieldLabel`/`FloatingFieldLabelText`
  styles) appears on the field's top border once focused or filled. UWP
  `TextBlock` has no `Background` — the chip is a `Border` with a solid
  background that masks the field border line.
- **Z-order (1.5.0.1319)**: after reset-credentials the setup dialog (which shows
  a QR code) sat over the credentials dialog. Each dialog now bumps its own
  `Canvas.SetZIndex` when it shows (setup 300, credentials/network-location 400,
  share 500) so the most recently opened modal always renders on top.

---

## M4 — Download from URL
*Status: `[x]` DONE — build 1.5.0.1319, 224 tests green (smoke skipped)*

**Goal**: action relocated to Network with explicit destination.

- [x] `FolderBrowserDialog`: optional `confirmLabel` + `confirmIcon` params on
      `ShowAsync`; default `null` keeps "Move Here" everywhere
      (`FolderBrowserDialog.xaml.cs:84-99`, `:85-87`, `:230-244`)
- [x] `FileActionSheet`: both "Download from URL" actions removed (`:373`,
      `:475`) + `FileAction.Download` enum value dropped; `ActionDownload`
      const removed
- [x] Action row `DownloadUrl` in Network column — icon
      `filetype-network-download-24.png` (24 px column row; the 48 px
      `fileactionsheet-download-48.png` is toolbar-sized, not a row icon;
      copied from `icons8-download-24.png`, see `docs/ATTRIBUTIONS.md`)
- [x] Flow: URL `InputDialog` first → destination picker (Folder, "Download
      Here" + download icon) → `DownloadService` (existing resolve/download;
      WebView fallback intact)
- [x] Empty URL → warning alert ("URL cannot be empty."), no silent dismissal
- [x] Row order in the Network column (1.6.0.1323): `Download from URL` →
      separator → `Add location` → separator → saved locations below (labels
      dropped the "+")
- [x] B-cancel at picker aborts without prompting for a URL
- [x] `ColumnNavigator.NetworkDownloadUrlRequested` event → page handler
      `HandleDownloadFromUrlAsync` (merged the old `HandleDownloadAsync`)
- [ ] Verify: desktop — download a direct URL into a chosen folder; Mega/gofile
      fallback still works (runtime — needs a hand-run; unit/build green above)

Notes:
- The destination picker opens on the local drives root when launched from the
  Network column (network columns have `Path == null`); it only offers local
  disk folders, so the old "only available in local folders" guard was dropped.
- **Root rule (1.5.0.1319)**: at the picker's drives root there is no confirm
  action — the virtual "Move Here"/"Download Here" entry is not shown and the
  A button reads "Navigate" (there is no destination at `C:`/`E:` level; the
  confirm entry appears once you drill into a folder). This applies to both
  move and download modes.

---

## M5 — Preview / play
*Status: `[x]` done (build 1.6.0.1332)*

**Goal**: remote files preview/play without full download.

Implementation notes (deviates from the original growing-file plan — see the
M5 decision-log entry below for why):

- **`RemoteStream`** (`XFiles/Network/RemoteStream.cs`): a blocking
  `IRandomAccessStream` over the `SmbReadStream`. `ReadAsync` performs a
  synchronous chunked SMB read on a worker task and returns the bytes; the
  consumer (audio graph / video player) is the natural backpressure — no
  pre-download, no temp cache, no seek-to-frontier bookkeeping. Serialized by
  a gate lock (the underlying SMB stream is not thread-safe). `Completed`/
  `Progress` are projected **properties** of `AsyncOperationWithProgress
  CompletedHandler`/`AsyncOperationProgressHandler` (verified against the SDK
  metadata — do not re-add them as C# events).
- **Preview pane** (`ColumnNavigator.UpdateNetworkPreviewAsync`): remote text/
  image/svg/ROM files render live from the first streamed bytes via
  `FilePreviewService.GetPreviewFromNetworkAsync` (new). Audio/video **stream
  inline into the pane's player** on selection: video via
  `MediaPreviewControl.LoadRemoteStream` (MediaSource from stream), audio via
  `LoadRemoteAudio` (AudioLevelService.PlayRemoteStreamAsync — AudioGraph + VU
  meter). The navigator skips the content-probe stream open for A/V (type set
  from extension). PDF keeps a metadata card with a "Press A to …" hint.
- **CloneStream / reopen factory**: the media pipeline clones the source stream
  synchronously (`CreateFromStream` → `RemoteStream.CloneStream`). `RemoteStream`
  must be built with a reopen factory (`Func<Stream>` reopening the SMB file at
  offset 0); the factory reopens via `Task.Run` because the clone runs on the UI
  thread and a blocked GetAwaiter().GetResult() on a UI-captured chain deadlocks.
- **A on a remote file** (`MillerColumnsPage.OpenRemoteFileAsync`):
  - image → `ImageFullScreen.Show` with the already-decoded preview
  - PDF → `CacheRemoteFileAsync` (LocalCache\NetworkCache) → path-based
    `PdfFullScreen.Show`
  - chiptune → `CacheRemoteFileAsync` (files are small) → existing
    `OpenAudioFullscreen` RA_Open path
  - audio → `AudioLevelService.PlayRemoteStreamAsync` (new) via
    `RemoteStream` + `MediaSource.CreateFromStream(stream, mime)` → fullscreen
    audio surface with VU meter
  - video → `ShowMediaFullscreenStreamAsync` (new) via `RemoteStream` +
    `MediaSource.CreateFromStream(stream, mime)`
  - unsupported → info toast
- **Remote text edit** (`HandleNetworkTextEditAsync`): Y-menu "Edit" on a
  network text file caches it and opens the editor (`ShowNetwork` overload);
  save writes the local temp back via SMB write (`SmbSession.WriteFileAsync`
  — chunked at `ISMBFileStore.MaxWriteSize` capped 64 KB,
  `WriteFile(out int written, handle, offset, slice)`,
  `FILE_OVERWRITE_IF`, `FlushFileBuffers` on close) through
  `ColumnNavigator.WriteNetworkFileAsync`; failure → "Saved locally — upload
  failed" toast. `SmbBrowser.WriteFileAsync` reads the local file in full
  (text tiers ≤ 256 KB).
- **Known M5 limits**: remote audio/video have no next/prev in the fullscreen
  player (no folder context yet); multi-track remote chiptune drill-in not
  wired (plays track 0); `RemoteStream` spawns a task per
  `ReadAsync` (bounded by the player's read-ahead).

Checklist:

- [x] `GetPreviewFromNetworkAsync` (text/image/svg/ROM via stream; PDF metadata)
- [x] `RemoteStream` blocking IRandomAccessStream (audio+video streaming)
- [x] `UpdateNetworkPreviewAsync` renders remote text/image/svg/ROM live
- [x] OnConfirm routing: image/PDF/chiptune fullscreen, audio/video stream
- [x] `PlayRemoteStreamAsync` + `OpenRemoteAudioFullscreenAsync`
- [x] `ShowMediaFullscreenStreamAsync` (video)
- [x] Remote text edit + SMB write-back (`SmbSession.WriteFileAsync`,
      `SmbBrowser.WriteFileAsync`, `ColumnNavigator.WriteNetworkFileAsync`,
      editor `ShowNetwork` + `NetworkUploadBack`)
- [x] Verify: build green (1.6.0.1332), 224 tests green + write smoke test
      (env-gated)
- [ ] Pending: run the write smoke against a real share (env vars not present
      in the build session — needs the user's share env)

---

## M5.5 — Remote file operations (copy/paste/rename/delete) + media parity fixes
*Status: DONE — build 1.6.0.1349, 224 tests green*

**Goal**: mirror the local file-operations UX on remote (SMB) files, with copy
working across remote ↔ local in both directions. Plus two media-behavior
parity fixes found on Xbox.

### Remote file ops

- **SMB write layer** (`XFiles/Network/`):
  - `SmbWriteStream` — write counterpart of `SmbReadStream`; opened via
    `SmbSession.OpenWriteStreamAsync` (`FILE_OVERWRITE_IF`,
    `GENERIC_WRITE|SYNCHRONIZE`, chunked at `MaxWriteSize` 64 KB), serializes
    every store call through the owning session gate (`WithStoreLock`) — same
    crash protection as reads (SMB2FileStore is not thread-safe).
  - `SmbSession` new ops (all gated): `OpenWriteStreamAsync`,
    `WriteFileAsync` (existing, chunked), `DeleteFileAsync` (DELETE access +
    delete disposition), `DeleteDirectoryAsync` (recursive walk under
    `BulkOperationTimeoutMs` = 5 min — large trees exceed the 10 s
    `OperationTimeoutMs`, which would invalidate the session mid-delete),
    `RenameFileAsync` (`FileRenameInformationType2`, same-parent),
    `CreateDirectoryAsync` (idempotent `FILE_OPEN_IF`).
  - `SmbBrowser` facades (vault + logging) for all of the above.
- **`NetworkCopyService`** — pure orchestration, 3 directions + delete:
  - `CopyRemoteToLocalAsync` (file or dir tree, progress reports, disk-space
    check via `EnsureDiskSpaceAsync`)
  - `CopyLocalToRemoteAsync` (local file or dir → remote, creates remote
    folders, `FileOperations.ListRecursiveAsync` for the local tree)
  - `CopyRemoteToRemoteAsync` (same or different server — streams between the
    two pooled sessions; `destName` override for "Copy of X" on same-dir paste)
  - `ScanRemoteEntriesAsync` (file count + total bytes pre-scan for progress)
  - `DeleteRemoteAsync`
- **ColumnNavigator**: `NetworkBrowser` made public, `GetNetworkConfigAsync`
  made public; `LoadNetworkDirectoryAsync`/`LoadNetworkSharesAsync` now stamp
  `NetworkLocationId` (the config row id) on every file entry so the Y-menu
  ops can resolve the server config.
- **FileActionSheet**: network file rows → `ShowNetworkFileActionsAsync` =
  Refresh / Edit (text) / Copy / Paste / Rename / Delete; network `..` rows =
  Refresh / New Folder / Paste. Location rows keep Rename/Delete location.
- **FileOps handlers**:
  - `HandlePasteAsync` routes: current column network → `HandlePasteToNetworkAsync`;
    clipboard network entries + local dest → `HandlePasteNetworkToLocalAsync`.
  - `HandlePasteToNetworkAsync` — mixed local/remote sources upload/stream into
    the current network dir; same-dir self-paste → "Copy of {name}" (mirrors
    local `CopyAsync`); `OpProgressDialog` with pre-scan totals.
  - `HandleNetworkRenameAsync` / `HandleNetworkDeleteAsync` /
    `HandleNetworkCreateFolderAsync` — InputDialog/confirm + SMB op, delete
    shows the file-operation confirm dialog with the network path.

### Media parity fixes (Xbox feedback)

1. **Remote audio autoplay**: inline remote audio loaded and started playing on
   selection. Local only opens the player — A starts playback. Fix:
   `AudioLevelService.PlayRemoteStreamAsync(stream, mime, autoPlay: false)` —
   the remote stream node is created + connected but never `Start()`ed
   (load-only). `Pause`/`Resume` now start/stop the media-source node when
   `_remoteStreamNode`; `TogglePlayPause` in `MediaPreviewControl` gained a
   remote branch (no `_currentFilePath`) that toggles the prepared graph.
   Fullscreen X path keeps `autoPlay: true` (mirrors local fullscreen).
2. **Remote video A re-load**: A on an already-loaded remote video re-opened the
   SMB stream and re-loaded the player (restart, broken seekbar). Fix: A now
   mirrors local — if the inline player already has the current network file
   loaded (`MediaPreview.IsNetworkFileLoaded(share, path)`) → toggle play/pause;
   only load when a different file. Chiptune A also got the guard (no re-cache).
   `MediaPreviewControl` now tracks `_currentNetworkShare/_currentNetworkPath`
   (set on every remote load, cleared in `Stop()`).

### Verification

- Build green 1.6.0.1349 (VS2026 MSBuild), 224 tests green (2 smoke skipped).
- Pending hands-on (user): SMB write smoke (`X_FILES_SMB_*` env), copy/paste/
  rename/delete on real share, inline audio/video parity on Xbox.

### Decision-log entries

| 2026-08-16 | M5.5 DONE (build 1.6.0.1349). Remote file ops: `SmbWriteStream` + `SmbSession` write/delete/rename/mkdir (bulk timeout 5 min for recursive delete), `NetworkCopyService` (remote↔local↔remote, progress + same-dir "Copy of X"), Y-menu Refresh/Edit/Copy/Paste/Rename/Delete, FileOps handlers, `NetworkLocationId` stamped on entries. Media parity: remote audio load-only (A plays, mirror local), remote video A toggles instead of re-loading. 224 tests green. |
| 2026-08-16 | Media parity bug 1: `PlayRemoteStreamAsync` autoplayed inline — fixed with `autoPlay` param (load-only node; Pause/Resume drive the media-source node; fullscreen keeps autoplay). |
| 2026-08-16 | Media parity bug 2: remote video A re-opened the stream + re-loaded the player — fixed with `IsNetworkFileLoaded` toggle-only guard (local A semantics). |

---

## M6 — Hardware validation (Xbox)
*Status: `[ ]` not started*

**Goal**: prove it on the console; record outcomes.

- [ ] Deploy Debug build to Xbox Developer Mode (`docs/DEPLOY-XBOX.md`)
- [ ] **Port 445 test**: connect to a real share from Xbox — record PASS/FAIL
- [ ] Browse a NAS/PC share end-to-end (list, drill, preview text/image)
- [ ] Audio streaming on console (growing-file start latency, seek behavior)
- [ ] Video streaming on console (stall/seek) → growing-file fallback if bad
- [ ] Chiptune from a share
- [ ] UNC fallback trial IF SMB sockets are blocked (P/Invoke `\\host\share`)
- [ ] Timeout behavior: unplug NAS mid-browse → no freeze, error toast
- [ ] Record every outcome in this section (results + logs)

---

## M7 — Release
*Status: `[ ]` not started — only on user request*

- [ ] `RELEASE-NOTES.md`
- [ ] Bump `version.props` (source of truth — memory #201)
- [ ] Commit + tag `v{major}.{minor}.{patch}.{build}` + push tag
      (workflow builds/packages/releases; never `gh release create` manually)

---

## Decision log / field notes

| Date | Note |
|---|---|
| 2026-08-15 | M5 DONE (build 1.6.0.1332). Streaming replaced the growing-file plan: `RemoteStream` (blocking IRandomAccessStream over SmbReadStream — the consumer is the backpressure, no temp cache, no seek bookkeeping). MP3 growing-file was rejected because AudioGraph reads to EOF on a naturally-growing file and a pre-allocated (zero-padded) file feeds invalid frames to the MPEG parser — only pre-patched WAV (chiptune) tolerates it. Preview pane + fullscreen audio/video + remote text edit (SMB write-back). 224 tests green. Next: M6 (Xbox hardware validation). |
| 2026-08-15 | SMB write added: `SmbSession.WriteFileAsync` (chunked at `MaxWriteSize` capped 64 KB, `WriteFile(out int written, handle, offset, slice)`, `FILE_OVERWRITE_IF`), `SmbBrowser.WriteFileAsync`, `ColumnNavigator.WriteNetworkFileAsync`. Write smoke test added (env-gated; leaves `XFilesSmoke_*.txt` — no delete op yet). Env vars not present in the build session — run pending on user's real share. |
| 2026-08-15 | M0 docset written. Design finalized: SMBLibrary socket primary, UNC fallback; SQLite table + PasswordVault; growing-file audio; Download-from-URL relocated. |
| 2026-08-15 | M1 done. `NetworkServerStore` (pure) + `NetworkServerManager` (facade + PasswordVault); table `NetworkServerEntry` + schema v3; `NetworkUrl` compose/parse; 33 new tests (224 green); build green. Next: M2 (SMB core). |
| 2026-08-15 | M1 fixes: `UpdateAsync` now preserves the vault password when the canonical URL changes and the edit leaves the password blank; dead `VaultResourcePrefix` removed; `AddAsync` treats `null` password as "keep". |
| 2026-08-15 | M2 done. `INetworkFileSystemProvider` + `NetworkFileEntry` + `NetworkOperationException` (+`NotFound` reason) + `SmbSession` (pool, 10s timeout, gate) + `SmbBrowser` (vault + logging) + `SmbReadStream`. Smoke test gated by env vars (skipped without a share). 225 tests green, build green. Next: M3 (navigation). |
| 2026-08-15 | M2 smoke PASSED against real Windows SMB share (10.0.0.20, share "Media"): connect → login → list shares → TreeConnect → list dir → open + read first bytes. M2 complete. Next: M3 (navigation). |
| 2026-08-15 | M3 DONE (build 1.5.0.1314). Network column + drill state machine (locations → shares → remote tree), `NetworkLocationDialog`, Y-menu rename/delete, `NetworkServerConfig.Id`, network icons, `＋ Add location` action row. 224 tests green (smoke skipped), build green. Next: M4 (Download from URL). |
| | |
