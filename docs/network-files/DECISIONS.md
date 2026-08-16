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
*Accepted (2026-08-15) — superseded in part by ADR-NF-010 (delivery order)*

Ordering from Gemini's own priority list is right (SMB covers ~90% of home
NAS). Adjustments: SFTP demoted below FTP/WebDAV for *this* app because
SSH.NET's UWP-compatible build is an old netstandard1.3 version (pin + .NET
Native risk), while FluentFTP and WebDav.Client are netstandard2.0/modern.
NFS skipped (scarce C# support, niche — Gemini agrees); DLNA treated as a
separate feature, not a filesystem protocol.

*2026-08-16 update:* FTP/FTPS + SFTP are now the active delivery (M8–M12);
WebDAV is deferred after them. See ADR-NF-010.

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

## ADR-NF-010 — Multi-protocol delivery: FTP/FTPS + SFTP via the thin provider contract
*Accepted (2026-08-16)*

ADR-NF-004 rejected abstracting the *local* filesystem layer; it explicitly
endorsed the thin `INetworkFileSystemProvider` that only the new network code
talks to. M8–M12 ship FTP/FTPS (FluentFTP) and SFTP (Renci.SshNet) behind that
contract:

- The interface gains the write ops (open-write, delete, rename, mkdir, exists)
  that today live only on `SmbBrowser` — `SmbBrowser` already implements them,
  so this is interface promotion, not new SMB work.
- `NetworkProviderFactory.Create(config)` returns the right browser per
  protocol; `ColumnNavigator`/`NetworkCopyService`/`FileOps` move from the
  concrete `SmbBrowser` type to the interface.
- WebDAV is deliberately deferred (a later slice, same contract).
- NFS/DLNA unchanged (skip / separate idea).

Existing shape differences handled as protocol rules, not abstractions:
shares column is SMB-only (FTP/SFTP go straight to a directory, `Share`
means "start folder"), path separators are protocol-aware (`\` vs `/`).

## ADR-NF-011 — SFTP host-key trust: confirmation dialog, persisted acceptances
*Accepted (2026-08-16)*

First connection to an unknown host shows the server's key fingerprint in a
dialog (A = accept); accepted host:port fingerprints persist (settings), and a
changed fingerprint on a later connection raises a warning. Rejected "accept
always" (MITM) and "TOFU pin" (user picked explicit confirmation).

## ADR-NF-012 — FTP media seek: REST-capable → seekable; no REST → sequential
*Accepted (2026-08-16)*

FTP data connections are not seekable; offset reads require the server's REST
command. At connect the browser probes REST support (`FEAT`/`REST STREAM`):

- REST supported → `FtpReadStream` reopens the data connection at the seek
  offset (seekable, matches the `RemoteStream` contract).
- No REST → media plays sequentially from the start, seek disabled; no
  whole-file download and no growing-file fallback (user decision).

## ADR-NF-013 — SFTP auth: password only
*Accepted (2026-08-16)*

PasswordVault already stores the password. Private-key auth (importing
`.ppk`/`.pem`, passphrase handling) is deferred — not required by the seedbox
use case and adds file-storage + UX scope.

## Deferred / open items

- **SFTP private-key auth**: deferred (ADR-NF-013 — password only).
- **WebDAV**: deferred to a later slice (ADR-NF-010 — same provider contract).
- **Remote-hosted archives over non-seekable transports**: opened via the
  file action sheet instead of a virtual folder (M5; SMB and SFTP have
  seekable streams so their archives drill in normally; FTP non-REST servers
  fall back to the flyout).
- **Network discovery** (WSD/SSDP/mDNS): manual entry only.
- **Xbox port 445 outcome**: unknown until M6 hardware test — the single
  biggest unknown. Same risk applies to outbound FTP/SFTP (ports 21/22) on the
  console.
