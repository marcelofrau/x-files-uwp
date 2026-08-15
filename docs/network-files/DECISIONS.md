---
layout: default
title: Network File Access — Architecture Decisions (ADRs)
---
# Network File Access — Architecture Decisions (ADRs)

Short, dated rationale records so future sessions don't re-litigate settled
questions. Statuses: **Accepted** / **Deferred** / **Rejected**.

## ADR-NF-001 — SQLite table instead of JSON-blob settings
*Accepted (2026-08-15)*

The proposal first said "settings JSON key `NetworkServers`" (Favorites
pattern). Digging in, the app's settings are *already* SQLite-backed —
`XFilesSettings` routes every key through `MetadataCache.GetSettingAsync`/
`SetSettingAsync` into the `AppSettingEntry` table (`metadata.db`); a
"Favorites" JSON string is just a JSON-*serialized value* in one row.

Decision: a **dedicated table** `NetworkServerEntry` with real columns
(Protocol, Host, Port, Username, Share, CanonicalUrl), created via the
existing schema migration (v2 → v3).

Why:
- Per-row CRUD for rename/delete (no re-serialize of a whole blob).
- `Protocol` column structurally anticipates FTP/WebDAV/SFTP.
- Schema versioning already exists; `ClearCacheAsync` provably does not wipe
  config tables (`MetadataCache.cs:348-349`).

## ADR-NF-002 — Passwords in PasswordVault
*Accepted (2026-08-15)*

Non-secret fields live in `NetworkServerEntry`; the password goes to
`Windows.Security.Credentials.PasswordVault`, keyed by canonical URL + username.
Alternative (plaintext in the table, like `PortalCredentials` today) rejected
on security grounds; Vault is available on UWP/Xbox and the storage cost is one
extra lookup.

## ADR-NF-003 — SMBLibrary (socket) primary; UNC as fallback
*Accepted (2026-08-15)*

Two candidate SMB paths:

1. **UNC + existing P/Invoke** (`FindFirstFileExFromAppW` `\\host\share\*`) —
   works today on desktop for free; all pipelines (preview/media/archive)
   work unchanged. BUT the kernel SMB redirector inside the UWP sandbox /
   Xbox Developer Mode is unverified and suspected blocked (port 445 +
   sandbox network isolation). `docs/FILE-SHARES.md` lists this as an open
   unknown.
2. **SMBLibrary** — pure-socket SMB2 client (`TalAloni.SMBLibrary`, 1.5.x,
   netstandard2.0, LGPL-3.0). No kernel dependency; SMB2 semantics owned by us;
   fits the `SmbSession`/`SmbReadStream` design. Costs: NuGet dep, LGPL, a
   real read-stream wrapper.

Decision: **SMBLibrary is the implementation path** (works uniformly on
desktop + Xbox as long as port 445 is reachable). UNC remains the documented
fallback if the console blocks SMB sockets — M6 records which one actually
works on hardware. LGPL-3.0 accepted for sideload (dynamic link); revisit
before any Store submission.

## ADR-NF-004 — Reject Gemini's full `IFileSystemProvider` abstraction
*Rejected (2026-08-15)*

The Gemini conversation recommends a base interface abstracting local/SMB/SFTP
(`IFileSystemProvider.GetFilesAsync`, …). Adopting it would mean re-architecting
`DirectoryScanner`, `FilePreviewService`, `FileOperations`, `AudioLevelService`
and `TextEditorService` — the whole path-string/P/Invoke filesystem layer — to
talk streams. High risk, high churn, breaks established, tested code.

Alternative (chosen): the **portal/archive "virtual folder" precedent**.
`FileEntry` already models non-local entries via flags + extra fields
(`IsPortal`, `PortalPath`, … — `FileEntry.cs:32-40`); `PortalBrowser`,
`ArchiveBrowser` and the portal cache already solve "remote addressing +
stream fetch" without touching the local layer. Network mirrors that: new
fields (`IsNetwork`, `ActionKind`, `NetworkServerId`, `NetworkShareName`,
`NetworkPath`), `FullPath = null`, and a thin `INetworkFileSystemProvider`
that only the *new* network code talks to. Future protocols plug into that
interface without refactoring the local layer.

## ADR-NF-005 — Direct stream for preview; growing-file for audio
*Accepted (2026-08-15)*

Rejected "download whole file to cache, then preview" (slow start, Xbox disk
budget) as the *default*. Instead:

- Text/image/PDF/ROM: read the remote stream directly (zero local copy).
- Audio: **growing-file streaming** — remote bytes streamed sequentially into
  a temp file opened with `FileShare.Read`; `AudioLevelService.SwapSourceAsync
  (tmp, forceStream: true)` plays it while it grows. This is exactly the
  proven chiptune PSF/USF pattern (playback starts in ~1–2 s, renderer/graph
  never catches up). Format-agnostic because bytes are copied verbatim and the
  container header arrives first — no size pre-patching needed (unlike
  chiptune WAV rendering).
- Video: `MediaPlayer.SetSource` on the seekable `SmbReadStream`; growing-file
  fallback if the socket path misbehaves on Xbox (M6).
- Remote chiptunes: read the small file as bytes → `RA_Open(data,…)` existing
  render pipeline (no network streaming needed).

## ADR-NF-006 — Protocol scope: SMB now; FTP/WebDAV/SFTP next; NFS/DLNA skip
*Accepted (2026-08-15)*

Ordering from Gemini's own priority list is right (SMB covers ~90% of home
NAS). Adjustments: SFTP demoted below FTP/WebDAV for *this* app because
SSH.NET's UWP-compatible build is an old netstandard1.3 version (pin + .NET
Native risk), while FluentFTP and WebDav.Client are netstandard2.0/modern.
NFS skipped (scarce C# support, niche — Gemini agrees); DLNA treated as a
separate feature, not a filesystem protocol.

## ADR-NF-007 — Capabilities already present; no manifest change
*Accepted (2026-08-15)*

`docs/FILE-SHARES.md` claims `privateNetworkClientServer` and
`enterpriseAuthentication` are missing. Verified false for the first —
`internetClient`, `internetClientServer`, `privateNetworkClientServer` are all
in `Package.appxmanifest:56-62` today. `enterpriseAuthentication` only added
if a Kerberos/domain SMB case appears. `broadFileSystemAccess` + `runFullTrust`
unchanged (still required for local filesystem code).

## ADR-NF-008 — "Download from URL" moves to Network with destination picker
*Accepted (2026-08-15)*

The action currently lives in the file action sheet and writes into the
current folder (`MillerColumnsPage.FileOps.cs:2385`, guard at `:2397`).
Relocated to the Network column as an action row; destination chosen first via
`FolderBrowserDialog` with a generalized `confirmLabel` ("Download Here").
`FolderBrowserDialog.ShowAsync` gains an optional `confirmLabel` parameter
defaulting to current behavior, so Move/copy/file-picking callers are
untouched. Download itself still uses `DownloadService` unchanged.

## ADR-NF-009 — Action rows modeled as `FileEntry.ActionKind`, not raw flags
*Accepted (2026-08-15)*

The first draft had `IsAddLocation`. Two action rows already exist (add
location, download URL) and protocols will add more; a single enum
(`ActionKind { None, AddLocation, DownloadUrl }`) on `FileEntry` beats a
growing set of booleans and gives `ColumnNavigator`/`FileActionSheet` one
dispatch point.

## Deferred / open items

- **Remote write-back** (copy-to-remote, remote rename/delete/move): needs
  `ISMBFileStore.WriteFile`/`SetFileInformation` plumbing; post-M6.
- **Remote-hosted archives**: `ArchiveBrowser` is path-based
  (`CreateFile2FromAppW`); SharpCompress `ArchiveFactory.Open(stream)` is the
  likely route later.
- **Network discovery** (WSD/SSDP/mDNS): manual entry only.
- **Xbox port 445 outcome**: unknown until M6 hardware test — the single
  biggest unknown.
