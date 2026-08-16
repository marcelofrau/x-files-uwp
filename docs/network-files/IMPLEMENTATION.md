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
| 2026-08-16 | Remote chiptune + archive drill-in (build 1.6.0.1356). Remote entries now get `IsChiptune`/`IsArchive` (they never had them — file rows fell through to "no drill-in"). `DrillIntoNetworkChiptuneAsync` caches the chip → `ChiptuneBrowser.BuildTrackEntries` → track-list column (GBS/RSN-internal-SPC/NSFE multi-track); `DrillIntoNetworkArchiveAsync` caches → local archive browse (`.rsn` is a ZIP of `.spc`). `.rsn` also gained the archive icon (`file-archive-24.png`). LB/RB inside a drilled-in track list navigates subsongs of the same chip (`NavigatePreviewChiptuneTracks`), mirroring local. |
| 2026-08-16 | Copy throughput: `ChunkSize` 256 KB → 1 MB (`NetworkCopyService`) — both directions (remote→local read, local→remote write) chunk at `Min(buffer, MaxReadSize/MaxWriteSize)`, so bigger buffers cut SMB round-trips. Instrumented `SmbSession.NegotiatedInfo()` + once-per-session negotiated MaxRead/MaxWrite log in `SmbBrowser` to verify the real cap on the user's server (pending — 18 MB/s report). |
| 2026-08-16 | Unhandled-exception UX: `SmbSession.RunAsync` never lets the `Task.Run` inner lambda fault (capture + rethrow via `ExceptionDispatchInfo` outside the task). An exception thrown inside `Task.Run` reads as "not handled in user code" in the VS debugger (async state-machine continuation is external code) and pauses the app even when the caller catches it — the reported "crash" on `STATUS_ACCESS_DENIED` in the preview column. Now every network error is handled at the catch site. |
| 2026-08-16 | Remote drill-in/drill-out bug batch (build 1.6.0.1362). (1) Crash `ArgumentNullException` on `GetOrCreateArchive(null)` after drilling out of a remote ZIP: `DrillIntoNetworkChiptuneAsync`/`DrillIntoNetworkArchiveAsync` pushed history without the network context (`IsNetwork`/`NetworkLocationId`/`NetworkShareName`/`NetworkPath`), so drill-out restored a "local" column whose network entry has `FullPath=null` → `UpdatePreviewAsync` → archive path → `ListEntries(null)`. Push now copies the full network context (mirrors `CommitNetworkColumnAsync`); also fixes `location id=0 not found` and `type="Error"` preview after drill-out. (2) Repeated drill-in (6× in ~5s): the remote archive/chiptune cache takes ~2.7s but `_networkBusy` was never set on those paths, so every Right press re-entered. Added `_networkBusy` guard + try/finally. (3) A on a remote multi-track chiptune (GBS/NSFE/RSN) played instead of drilling in: `OnConfirm` routed every remote file to `OpenRemoteFileAsync` before the chiptune branch. New `OnRemoteChiptuneConfirmAsync` (cache → probe track count → multi drills in / single plays), mirroring the local `OnChiptuneFileConfirmAsync`. |
| 2026-08-16 | Remote archive drill-in via stream (build 1.6.0.1364). Remote ZIPs no longer download in full before drilling in — `DrillIntoNetworkArchiveAsync` opens the archive directly from the seekable SMB stream (`OpenNetworkStreamAsync` → `ArchiveBrowser.TryOpenArchiveFromStream`): SharpCompress random-access ZIP reads only the EOCD + central directory at the end (few seeks), so a 296 MB zip lists in ~1-2 s instead of ~12.5 s. Virtual cache key `net~{locationId}~{share}~{path}` (no `|` — ArchiveBrowser's `archive|internal` addressing splits on the first pipe). `ArchiveBrowser` is shared between `ColumnNavigator` and `MediaPreviewControl` (`SetArchiveBrowser`) so preview/play of entries inside the remote archive resolves through the same stream-backed cache. Fallback: full `CacheNetworkFileAsync` download when the stream cannot be opened as an archive. Extraction from inside a remote ZIP got a network branch (staging + upload back to the ZIP's parent remote folder, overwrite-confirm) mirroring the portal flow; `ColumnState` inside a remote archive now carries the network context for it. |
| 2026-08-16 | Remote archive drill-in subdirectory fix (build 1.6.0.1367). A remote archive column carries `IsNetwork=true` (for drill-out context), which made `DrillInAsync` route a subdirectory INSIDE the remote archive to the SMB branch (`SmbBrowser.ListDirectory` on a virtual path → `STATUS_OBJECT_PATH_NOT_FOUND`). Preview worked (the preview branch excludes archives) but Right/A drill-in did not. `DrillInAsync` now checks `(_current.IsNetwork && !_current.IsArchive) || selected.IsNetwork` so internal archive subdirectories fall through to `DrillIntoArchiveSubdirectoryAsync` (which already preserved the network context). |
| 2026-08-16 | Remote metadata (build 1.6.0.1368). ID3/Deezer/MusicBrainz/cover-art now work for remote (SMB) audio — previously metadata was local-only: `OpenRemoteAudioFullscreenAsync` skipped enrichment entirely and `LoadRemoteAudio` showed only the filename. Added `Id3Tag.ReadFromStream` (reads the leading tag bytes of a seekable stream; `SmbReadStream` is seekable) and `MetadataGuesser.ResolveStreamAsync` (ID3 from the stream + FilenameParser from the remote display path, same Deezer/MusicBrainz/cache pipeline). Inline (`MediaPreviewControl.LoadRemoteAudio` gained an `id3StreamFactory` param; the 3 call sites pass the existing `reopen` factory) and fullscreen (`OpenRemoteAudioFullscreenAsync` now calls `LoadAudioFullscreenMetadataAsync` with a reopen factory + stale-key on `_fsNetworkPath`). `LoadMetadataAsync` stale-check switched to a dedicated `_currentMetadataKey` (remote sets it to the title, local sets it to the path; `_currentFilePath` stays null for remote). 245 tests green. |
| 2026-08-16 | Archive drill-in over non-seekable transports (build 1.6.0.1369). Generic rule: SMB can open a seekable stream (stream drill-in, fast for large zips); the web portal cannot (no stream API, only whole-file HTTP download). Drilling into a portal archive used to silently cache the whole file; it now raises `ColumnNavigator.ArchiveDrillInUnavailable` and the page shows the file action sheet (Copy / Extract / ...) so the user decides. Same generic path a future FTP transport will use (`DrillIntoPortalArchiveAsync` is now the "no seekable stream" template). Portal preview of small files (`PortalCache.AutoPreviewMaxBytes`) is unchanged. |

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

## M8 — Protocol-layer generalization
*Status: `[x]` done — build 1.6.0.1371, 253 tests green*

**Goal**: make the SMB-shaped layer protocol-agnostic so FTP/FTPS (M9) and
SFTP (M10) plug in without refactoring the local filesystem layer
(ADR-NF-010).

- [x] Promote `SmbBrowser`'s M5.5-era write ops to `INetworkFileSystemProvider`
      (`EntryExistsAsync`/`OpenWriteStreamAsync`/`WriteFileAsync`/
      `DeleteFileAsync`/`DeleteDirectoryAsync`/`RenameFileAsync`/
      `CreateDirectoryAsync`) — `SmbBrowser` already implements them
- [x] `NetworkProviderFactory.Create(config)` → per-protocol browser
      (SMB today; FTP/FTPS + SFTP register in M9/M10)
- [x] `ColumnNavigator`: `SmbBrowser` field → `INetworkFileSystemProvider`,
      resolved via the factory; shares column only for SMB
      (`config.Share` becomes the FTP/SFTP start folder; empty = server root)
      — drill-in, reload and preview branch on protocol (shared provider +
      empty-share path for non-SMB); `NetworkShareName` stays `""` on
      non-SMB columns
- [x] `NetworkCopyService` + `MillerColumnsPage.FileOps` handlers:
      `SmbBrowser` parameters → `INetworkFileSystemProvider`
- [x] Fix clobbers: `NetworkServerManager` (`Protocol = Smb` hardcoded at
      Add/Update), `NetworkLocationDialog.RunTestAsync` (now factory +
      provider `TestConnectionAsync`), `NetworkUrl.Parse` (only `"smb"`) +
      `DefaultPort` (FTP/FTPS 21, SFTP 22)
- [x] `NetworkPathUtil` separator-aware (`\` SMB vs `/` FTP/SFTP)
- [ ] Add `FluentFTP` (netstandard2.0) + `Renci.SshNet` (try latest, fallback
      pin 2020.0.2) to `XFiles.csproj`; verify the UWP build resolves —
      **deferred to M9/M10 (no consumer yet; avoids dead package refs)**
- [x] Unit tests: factory dispatch, URL scheme/port matrix, separator-aware
      path joins (8 new cases: FTP/FTPS/SFTP compose/parse, ports 21/22,
      unknown schemes webdav/nfs → null)
- [x] Build + full test run green

---

## M9 — FTP/FTPS core (FluentFTP)
*Status: `[x]` done — core + navigation wiring complete; smoke against a real external server pending M12*

**Goal**: browse/read/write a router-NAS or legacy FTP server.

- [x] `FtpSession` (per-op connection; FTP handshake is cheap so no pool —
      mirrors `SmbSession` error mapping + timeout discipline instead),
      plain + explicit TLS (FTPS)
- [x] `FtpBrowser : INetworkFileSystemProvider` — list/open-read/get-length/
      write/delete/rename/mkdir/exists; `ListSharesAsync` returns empty
- [x] `FtpReadStream`: probe REST support at connect (`HasFeature(REST)`); REST →
      reopen+`REST` offset on seek (seekable); no REST → sequential-only (seek
      disabled, no whole-file download — ADR-NF-012)
- [x] `FtpWriteStream` (upload via FluentFTP `OpenWrite`)
- [x] Unit tests (pure parts): URL/port, config (M8)
- [x] Docker smoke infra (`tools/network-smoke`: vsftpd + OpenSSH sftp,
      seed files generated by `make-seed.ps1`) — FTP smoke tests green
      (list/read/seek-REST/write-back), env-gated on `X_FILES_FTP_*`
- [x] Navigation wiring (build 1.6.0.1379): dialog protocol combo already had
      FTP/FTPS/sFTP; URL preview scheme-aware (was hardcoded `smb://`); Share
      field means "start folder" on FTP/FTPS/SFTP (placeholder + doc updated);
      WebDAV removed from the combo (enum keeps it, no provider yet); empty
      network state text is multi-protocol; drill-in/out, preview and media all
      resolve the provider per-protocol via `BrowserFor(config.Protocol)`
- [ ] Desktop smoke against a real FTP server (user has one): list, preview,
      audio/video play, copy/paste/rename/delete
- [x] Build + tests green (1.6.0.1379, 256 tests; FTP smoke via docker env)

---

## M10 — SFTP core (Renci.SshNet)
*Status: `[x]` done — host-key dialog UI shipped in M11 (1.6.0.1389)*

**Goal**: browse/read/write a seedbox or SSH server.

- [x] `SftpSession`: connection pool keyed by canonical URL, password auth,
      timeout discipline (mirrors `SmbSession`; `SftpClient` not thread-safe
      → store calls gated). API map for SSH.NET **2026.0.0**: `HostKeyReceived`
      moved from `ConnectionInfo` to `SftpClient` (breaks older ADR-NF-011
      notes); `SftpFileStream` (not `FileStream`) from `OpenRead`.
- [x] `HostKeyTrustStore`: persisted accepted fingerprints by host:port
      (pure model, JSON file supplied by caller — LocalState\Network\
      host-keys.json on Xbox; `JsonSimple` helper, no JSON dependency)
- [x] `SftpBrowser : INetworkFileSystemProvider` — list/open-read/
      get-length/write/delete/rename/mkdir/exists; `SftpFileStream` is
      natively seekable (no REST probe needed, better than FTP)
- [x] Write ops map 1:1 to `SftpClient` (Create/DeleteFile/DeleteDirectory/
      RenameFile/CreateDirectory/Exists/GetAttributes)
- [x] Unit tests (pure parts): host-key trust store (+8, round-trip
      persistence, case-insensitivity, forget)
- [x] Desktop smoke against the docker SFTP server (atmoz/sftp, port 2222):
      list, read, seek (native), write-readback-delete — 3/3 green,
      env-gated on `X_FILES_SFTP_*`
- [x] Build + tests green (1.6.0.1385, 264 pass / 6 skip)
- [x] First-connect host-key dialog (A = accept, changed = warning) — trust
      store + resolver hook exist; gamepad dialog UI shipped in M11 (1.6.0.1389)

---

## M11 — Navigation + UX
*Status: `[x]` done (build 1.6.0.1389, 261 pass / 9 skip)*

- [x] Shares column only for SMB; FTP/SFTP go straight to the directory
      (`config.Share` = start folder) — done in M8/M9, re-verified end-to-end
- [x] `NetworkLocationDialog`: per-protocol fields — host + **port** (defaults
      from `NetworkUrl.DefaultPort`: SMB 445, FTP/FTPS 21, SFTP 22; protocol
      switch resets only while the field still holds the previous default),
      URL preview scheme-aware, start-folder placeholder for FTP/SFTP
- [x] FTPS mode: **port 990 = implicit FTPS** (RFC 4217, TLS on the first
      byte), any other port = explicit (AUTH TLS) — user picks the mode by
      entering the port in the dialog (`FtpSession` maps it to
      `FtpEncryptionMode.Implicit`/`Explicit`)
- [x] Per-protocol icons: location rows show `filetype-network-server-24.png`
      (SMB) vs `filetype-network-globe-24.png` (FTP/FTPS/SFTP); `FileEntry` +
      `EntryViewModel` gained `NetworkProtocol`
- [x] Host-key first-connect confirmation dialog (ADR-NF-011) —
      `HostKeyDialog`: host:port + SHA256 fingerprint, **A = TRUST** (persist
      to `LocalState\Network\host-keys.json` via `HostKeyTrustStore`),
      **B = REJECT**; registered in `InputRouter` at priority 82 (above the
      location dialog, since it can fire during Test); wired from the page's
      `ConfirmHostKey` bridge — the SFTP resolver runs on the connect
      background thread, so the bridge blocks only that thread
      (`ManualResetEventSlim` + `Dispatcher.RunAsync`, no UI-thread deadlock)
- [x] A on FTP/SFTP non-REST media: sequential play, seek disabled —
      `FtpReadStream.CanSeek=false` without REST, seek throws
      `NotSupportedException`, `AudioLevelService.Seek` catches and logs
      (ADR-NF-012); SFTP seek is native
- [x] Build + tests green (1.6.0.1389; FTP + SFTP docker smoke 6/6)

---

## M12 — Multi-protocol tests + docs
*Status: `[ ]` not started*

- [ ] Real FTP/FTPS + SFTP smoke (desktop) against the user's servers:
      list/drill/preview/media/copy/paste/rename/delete, plus timeout
      behavior (unplug/server-down → toast, no freeze)
- [ ] Docset updates: `PLAN.md` (done), `DECISIONS.md` (ADRs done),
      `ARCHITECTURE.md` (done), this file, `docs/FILE-SHARES.md` header
- [ ] Xbox validation of all three protocols (ports 21/22 open on the
      console?)
- [ ] Release when asked

---

## Decision log / field notes

| Date | Note |
|---|---|
| 2026-08-16 | M11 DONE (build 1.6.0.1389, 261 pass / 9 skip; FTP + SFTP docker smoke 6/6). **Port field** added to the location dialog (defaults per protocol from `NetworkUrl.DefaultPort`, preserved when manually edited; URL preview shows `:port` only when non-default). **FTPS implicit** by convention: port 990 → `FtpEncryptionMode.Implicit` (RFC 4217), other ports explicit. **Per-protocol icons**: `filetype-network-server-24.png` (SMB) vs `filetype-network-globe-24.png` (FTP/FTPS/SFTP) on location rows — `FileEntry` + `EntryViewModel` gained `NetworkProtocol`; two new 24px icons generated from the personal icon set (downscaled for crispness). **Host-key dialog** shipped (ADR-NF-011): `HostKeyDialog` shows host:port + SHA256 fingerprint, A = TRUST (persists via `HostKeyTrustStore`), B = REJECT; registered in `InputRouter` priority 82 (above the location dialog — can fire during Test); page `ConfirmHostKey` bridge blocks only the connect background thread (`ManualResetEventSlim` + `Dispatcher.RunAsync`), no UI deadlock. Non-REST FTP media verified: `FtpReadStream.CanSeek=false`, seek throws, `AudioLevelService.Seek` catches → sequential play (ADR-NF-012). |
| 2026-08-16 | M10 SFTP core done (build 1.6.0.1385, 264 pass / 6 skip). SSH.NET **2026.0.0** resolves on UWP with JIT (the net8.0 dep chain — Logging.Abstractions 8.0.3 + Asn1 10.0.10 — is fine with `UseDotNetNativeToolchain` off; the ADR-NF-006 2020.0.2 fallback was unnecessary). `HostKeyReceived` moved from `ConnectionInfo` → `SftpClient` in 2026.0.0 (deviation from earlier ADR notes). `SftpSession` = pool + gate (SftpClient not thread-safe — same pattern as SmbSession), password auth from PasswordVault, per-op timeout. `HostKeyTrustStore` = pure model (JSON file, path injected by caller; LocalState\Network\host-keys.json on Xbox) — resolver hook wired before connect so the first Acquire can't reject every key (fail-safe ordering bug fixed in 1.6.0.1384). `SftpBrowser` mirrors FtpBrowser but with pool + trust store. `SftpFileStream` from `OpenRead` is natively seekable — no REST probe needed (better than FTP). Smoke: docker atmoz/sftp (port 2222), 3/3 green — list/read, native seek, write-readback-delete. Environment: home must stay `root:root` (OpenSSH `ChrootDirectory %h` aborts ALL connections if the chroot dir is user-writable — a manual chown broke sshd); writable folder = `uploads/` (seed mount is `:ro`). vsftpd-style MLST quirks don't apply here. `EntryExistsAsync` bug fixed: `SftpClient.GetAttributes` THROWS `SftpPathNotFoundException` for missing files (vsftpd-style null-return does not apply) — caught → false. First-connect host-key gamepad dialog deferred to M11 (trust store + resolver exist). |
| 2026-08-16 | M9 FTP/FTPS core done (build 1.6.0.1376, 256 tests green). `FtpSession` = per-op connect (FTP handshake ~100ms, no pool needed — mirrors SmbSession error mapping), plain + explicit TLS via FluentFTP 54.2.0 (netstandard2.0, UWP-safe, verified by build). `FtpBrowser` registered in `NetworkProviderFactory` (Ftp/Ftps → FtpBrowser). `FtpReadStream` probes REST via `HasFeature(FtpCapability.REST)`; REST → reopen+offset on seek (vsftpd confirmed); no REST → sequential-only, seek throws `NotSupportedException` (ADR-NF-012, media degrades to sequential play). `RemoteStream` (blocking IRandomAccessStream) is already protocol-agnostic — FTP media uses the same reopen-factory path as SMB. Docker smoke infra in `tools/network-smoke` (vsftpd + atmoz/sftp compose, `make-seed.ps1` generates seed.txt/png/wav). vsftpd quirks discovered: (1) `reverse_lookup_enable=YES` delays login ~25s when there's no PTR record for the client — set `REVERSE_LOOKUP_ENABLE=NO`; (2) `GetObjectInfo` returns null even for existing files (MLST quirk) — use `GetFileSize` (SIZE) / `FileExists` / `DirectoryExists` instead. FTP smoke tests green (list/read/seek-REST/write-back), env-gated on `X_FILES_FTP_*`. Next: FTP navigation wiring (M9 UX), then M10 SFTP. |
| 2026-08-16 | M8 DONE (build 1.6.0.1371, 253 tests green). `INetworkFileSystemProvider` extended with all M5.5-era write ops (EntryExists/OpenWriteStream/WriteFile/DeleteFile/DeleteDirectory/RenameFile/CreateDirectory) + `TestConnectionAsync`; `NetworkProviderFactory` dispatches per protocol (SMB today); `ColumnNavigator`/`NetworkCopyService`/FileOps now type against the interface; shares column only for SMB (non-SMB shares empty → straight into the start folder; `NetworkShareName=""` on non-SMB columns); `NetworkUrl` parses ftp/ftps/sftp + `DefaultPort` 21/22; `NetworkPathUtil` separator-aware (`\` SMB, `/` FTP/SFTP). Libs deferred to M9/M10 (no consumer yet). Next: M9 (FTP/FTPS). |
| 2026-08-16 | M8–M12 planned (FTP/FTPS + SFTP). Docset updated: PLAN.md (scope + milestones + deps + risks), DECISIONS.md (ADR-NF-010 protocol delivery via thin provider contract, ADR-NF-011 host-key confirmation dialog, ADR-NF-012 FTP seek via REST-probe, ADR-NF-013 SFTP password-only), ARCHITECTURE.md (provider contract extended with write ops + factory + per-protocol browsers). Next: M8 implementation. |
| 2026-08-15 | M5 DONE (build 1.6.0.1332). Streaming replaced the growing-file plan: `RemoteStream` (blocking IRandomAccessStream over SmbReadStream — the consumer is the backpressure, no temp cache, no seek bookkeeping). MP3 growing-file was rejected because AudioGraph reads to EOF on a naturally-growing file and a pre-allocated (zero-padded) file feeds invalid frames to the MPEG parser — only pre-patched WAV (chiptune) tolerates it. Preview pane + fullscreen audio/video + remote text edit (SMB write-back). 224 tests green. Next: M6 (Xbox hardware validation). |
| 2026-08-15 | SMB write added: `SmbSession.WriteFileAsync` (chunked at `MaxWriteSize` capped 64 KB, `WriteFile(out int written, handle, offset, slice)`, `FILE_OVERWRITE_IF`), `SmbBrowser.WriteFileAsync`, `ColumnNavigator.WriteNetworkFileAsync`. Write smoke test added (env-gated; leaves `XFilesSmoke_*.txt` — no delete op yet). Env vars not present in the build session — run pending on user's real share. |
| 2026-08-15 | M0 docset written. Design finalized: SMBLibrary socket primary, UNC fallback; SQLite table + PasswordVault; growing-file audio; Download-from-URL relocated. |
| 2026-08-15 | M1 done. `NetworkServerStore` (pure) + `NetworkServerManager` (facade + PasswordVault); table `NetworkServerEntry` + schema v3; `NetworkUrl` compose/parse; 33 new tests (224 green); build green. Next: M2 (SMB core). |
| 2026-08-15 | M1 fixes: `UpdateAsync` now preserves the vault password when the canonical URL changes and the edit leaves the password blank; dead `VaultResourcePrefix` removed; `AddAsync` treats `null` password as "keep". |
| 2026-08-15 | M2 done. `INetworkFileSystemProvider` + `NetworkFileEntry` + `NetworkOperationException` (+`NotFound` reason) + `SmbSession` (pool, 10s timeout, gate) + `SmbBrowser` (vault + logging) + `SmbReadStream`. Smoke test gated by env vars (skipped without a share). 225 tests green, build green. Next: M3 (navigation). |
| 2026-08-15 | M2 smoke PASSED against real Windows SMB share (10.0.0.20, share "Media"): connect → login → list shares → TreeConnect → list dir → open + read first bytes. M2 complete. Next: M3 (navigation). |
| 2026-08-15 | M3 DONE (build 1.5.0.1314). Network column + drill state machine (locations → shares → remote tree), `NetworkLocationDialog`, Y-menu rename/delete, `NetworkServerConfig.Id`, network icons, `＋ Add location` action row. 224 tests green (smoke skipped), build green. Next: M4 (Download from URL). |
| | |
| 2026-08-16 | Fullscreen VU/visualizer break on remote music (build 1.6.0.1361). Log forensics: the repeated TaskCanceledException was first-chance debugger noise (drift-monitor Task.Delay(5000, ct) cancel on Stop() - caught), not a crash. The real break: switching visualizers (NightCity -> DEFAULT) left _drawCapturedVis pointing at the disposed visualizer, so DrawWater threw ArgumentException: Effect source #0 is null mid-render. Fix: AudioVisualizerBase.OnDrawScene re-reads the current _visualizer under lock every frame (removed _drawCapturedVis) - a deactivated visualizer is never drawn again. |
