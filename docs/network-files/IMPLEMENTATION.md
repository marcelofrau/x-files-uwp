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
*Status: `[ ]` not started*

**Goal**: SMB socket plumbing — session, listing, readable stream.

- [ ] `XFiles/Network/INetworkFileSystemProvider.cs` — contract per
      `ARCHITECTURE.md` (`ListSharesAsync`, `ListDirectoryAsync`, `OpenReadAsync`,
      `GetFileLengthAsync`, `Disconnect`)
- [ ] `XFiles/Network/NetworkFileEntry.cs` — `{ Name, IsDirectory, Size,
      LastWriteTime }`
- [ ] `XFiles/Network/NetworkOperationException.cs` — `Reason` enum
      (`TimedOut`, `AccessDenied`, `Unreachable`, `AuthFailed`, `Cancelled`)
- [ ] `XFiles/Network/SmbSession.cs` — pool keyed by canonical URL; `Connect`
      (DirectTCPTransport) → `Login` → `TreeConnect` → `ISMBFileStore`; 10 s
      connect timeout via `Task.WhenAny`; `CancellationToken` plumbing;
      `Disconnect`
- [ ] `XFiles/Network/SmbBrowser.cs` — `ListSharesAsync`, `ListDirectoryAsync`
      (share-root listing with `FILE_DIRECTORY_FILE|FILE_NON_DIRECTORY_FILE`),
      NTStatus → `NetworkOperationException` mapping
- [ ] `XFiles/Network/SmbReadStream.cs` — `Stream` over `ISMBFileStore.ReadFile`
      (offset-based, chunked at MaxReadSize); `CloseFile` on dispose
- [ ] Desktop smoke: throwaway test page or unit hook to connect + list shares
      + read first bytes of a real share (validates SMBLibrary in this runtime)
- [ ] Verify: build + unit tests green

---

## M3 — Navigation
*Status: `[ ]` not started*

**Goal**: Network column browsable with gamepad; locations CRUD via UI.

- [ ] `FileEntry`: add `IsNetwork`, `ActionKind { None, AddLocation, DownloadUrl }`,
      `NetworkServerId`, `NetworkShareName`, `NetworkPath` (FullPath stays null
      for network entries)
- [ ] `ColumnNavigator`: inject "Network" virtual entry at root (favorites hook)
- [ ] `ColumnNavigator`: network drill state machine — locations list → shares →
      remote tree → preview; drill-out via own stack; loading state + timeout
      toast on connect failure
- [ ] `Controls/NetworkLocationDialog.xaml(.cs)` — form (name, user, pass, host,
      port, share, protocol dropdown), BladeTheme templates, gamepad nav,
      TCS result, edit mode pre-filled
- [ ] Action row `AddLocation` → dialog → `NetworkServerManager.AddAsync`
- [ ] `FileActionSheet`: branch for network location rows → `RenameLocation`,
      `DeleteLocation` (new `FileAction` values); Rename → dialog pre-filled;
      Delete → confirm dialog → remove + disconnect
- [ ] Location list shows display name or composed address
- [ ] `MillerColumnsPage`/`FileOps`: handlers for the two new actions
- [ ] Verify: desktop — add a location against a real share, browse shares and
      files, drill out; Y-menu rename/delete; build + unit tests green

---

## M4 — Download from URL
*Status: `[ ]` not started*

**Goal**: action relocated to Network with explicit destination.

- [ ] `FolderBrowserDialog`: add optional `confirmLabel` + icon param to
      `ShowAsync`; default `null` keeps "Move Here" everywhere
      (`FolderBrowserDialog.xaml.cs:84-99`, `:85-87`, `:230-244`)
- [ ] `FileActionSheet`: remove both "Download from URL" actions
      (`:373`, `:475`) and the enum value
- [ ] Action row `DownloadUrl` in Network column (icon
      `fileactionsheet-download-48.png`)
- [ ] Flow: picker (Folder, "Download Here") → URL `InputDialog` →
      `DownloadService` (existing resolve/download; WebView fallback intact)
- [ ] B-cancel at picker aborts without URL prompt
- [ ] Verify: desktop — download a direct URL into a chosen folder; Mega/gofile
      fallback still works; build + tests green

---

## M5 — Preview / play
*Status: `[ ]` not started*

**Goal**: remote files preview/play without full download.

- [ ] Text/image/PDF/ROM: `IsNetwork` branch in preview dispatch reading via
      `SmbReadStream` (→ `AsRandomAccessStream()` where the API needs it)
- [ ] Audio growing-file: producer copies `SmbReadStream` → temp file
      (`LocalState\tmp\net-<id>.<ext>`, `FileShare.Read`); start playback via
      `SwapSourceAsync(tmp, forceStream: true)` after ~256 KB buffer (tune);
      seek clamped to downloaded region; temp deleted on stop/navigate-away
- [ ] Chiptune (remote): read full bytes → `RA_Open(data, …)` → existing render
      pipeline
- [ ] Video: `MediaPlayer.SetSource(AsRandomAccessStream(readStream))`; note
      Xbox result for M6 fallback decision
- [ ] Progress indicator while a remote file buffers/streams
- [ ] Verify: desktop — preview each type from a real share; audio starts fast;
      chiptune plays; video plays; build + tests green

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
| 2026-08-15 | M0 docset written. Design finalized: SMBLibrary socket primary, UNC fallback; SQLite table + PasswordVault; growing-file audio; Download-from-URL relocated. |
| 2026-08-15 | M1 done. `NetworkServerStore` (pure) + `NetworkServerManager` (facade + PasswordVault); table `NetworkServerEntry` + schema v3; `NetworkUrl` compose/parse; 33 new tests (224 green); build green. Next: M2 (SMB core). |
| | |
