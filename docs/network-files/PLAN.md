---
layout: default
title: Network File Access — Plan
---
# Network File Access — Plan

## Vision

Let the user browse, preview and play files that live on the local network
(NAS, Windows PC, Samba/Linux box, seedbox, self-hosted cloud) from the same
gamepad-first column UI used for local drives — without copying anything up
front. Remote locations are first-class entries alongside local drives and
favorites.

## Scope

### In scope — SMB delivery (shipped)

- A "Network" virtual entry in the root column.
- Saved **locations** with CRUD (add / rename / delete), persisted in SQLite
  + PasswordVault.
- SMB browsing via **SMBLibrary** (socket-based SMB2, no kernel redirector).
- Remote preview/play for every format the app already supports:
  text, image, PDF, ROM, archive, audio (growing-file streaming), video,
  chiptune (bytes → existing renderer).
- "Download from URL" relocated from the file action menu into the Network
  column, with an explicit destination picker.
- Timeout + cancellation discipline on every network call.

### In scope — FTP/FTPS + SFTP delivery (M8–M12)

- Same saved-location CRUD (protocol already persisted).
- FTP/FTPS via **FluentFTP** (plain + explicit/implicit TLS).
- SFTP via **Renci.SshNet** (password auth only, host-key confirmation).
- Same remote preview/play and write ops (copy/move/rename/delete) as SMB.
- FTP seek: REST-capable servers get a seekable stream; non-REST servers play
  sequentially without seek (no whole-file download).
- SFTP: native seekable streams throughout.

### Out of scope — this delivery

- WebDAV implementation (deferred to a later slice; architecture accommodates
  it via the same `INetworkFileSystemProvider` contract).
- NFS and DLNA/UPnP (no roadmap commitment — see Protocol priority).
- Remote **write** operations *to* the remote are DONE as of M5.5 (SMB) and
  M8–M10 (FTP/FTPS, SFTP); archives *hosted* on a non-seekable remote
  transport open the file action sheet instead of a virtual folder (M5).
- Network discovery (WSD/SSDP/mDNS) — manual entry only.
- `enterpriseAuthentication` capability — only needed if a domain/Kerberos SMB
  auth case appears; not added now.
- SFTP private-key auth — password only (PasswordVault).

## Protocol priority

| Protocol | Priority | Decision | Library |
|---|---|---|---|
| SMB/CIFS | 1 — implement now | Covers ~90% of home NAS/PC cases | `TalAloni.SMBLibrary` (socket) |
| FTP/FTPS | 2 — next | Router-NAS and legacy devices | `FluentFTP` |
| WebDAV | 3 | Nextcloud/ownCloud, HTTP through NAT | `WebDav.Client` |
| SFTP | 4 | Seedboxes/remote SSH servers; needs version pin for UWP | `Renci.SshNet` |
| NFS | skip | C# support scarce; niche (4K enthusiast) | — |
| DLNA/UPnP | separate idea | read-only media catalog, not a filesystem | — |

Rationale and caveats in `DECISIONS.md`.

**Status (2026-08-16):** SMB shipped (M0–M5.5). FTP/FTPS + SFTP planned as
M8–M12 (`IMPLEMENTATION.md`).

## Milestones

| Milestone | Deliverable | Est. effort |
|---|---|---|
| **M0 — Docs & scaffolding** | This docset + AGENTS.md docs-table entry + SMBLibrary package reference decision | — |
| **M1 — Data layer** | `NetworkServerConfig`/`NetworkServerEntry`, SQLite table + migration v3, `NetworkServerManager` (CRUD + PasswordVault), URL composition, unit tests | 0.5–1 d |
| **M2 — SMB core** | `INetworkFileSystemProvider`, `SmbSession`, `SmbBrowser`, `SmbReadStream` | 1–2 d |
| **M3 — Navigation** | Root "Network" entry, drill-in, action rows, `NetworkLocationDialog`, Y-menu rename/delete | 1–2 d |
| **M4 — Download from URL** | Remove from `FileActionSheet`, generalize `FolderBrowserDialog` (`confirmLabel`), Network flow | 0.5–1 d |
| **M5 — Preview/play** | Direct streams (text/image/PDF/ROM/archive), chiptune bytes→render, audio growing-file, video stream | 2–3 d |
| **M6 — Hardware validation** | Xbox Developer Mode port-445 test, UNC fallback decision, timeout tuning, real NAS test | 1–2 d |
| **M7 — Release** | `RELEASE-NOTES.md`, version bump via `version.props`, tag (only on request) | — |
| **M8 — Protocol-layer generalization** | `INetworkFileSystemProvider` write ops, provider factory, nav/`NetworkCopyService`/`FileOps` on the interface, URL/manager/dialog clobbers fixed, separator-aware paths, libs (FluentFTP + SSH.NET), unit tests | 1–2 d |
| **M9 — FTP/FTPS core** | `FtpSession`/`FtpBrowser` (FluentFTP), seekable `FtpReadStream` via REST with capability probe, `FtpWriteStream`, pool + timeouts | 1–2 d |
| **M10 — SFTP core** | `SftpSession`/`SftpBrowser` (SSH.NET), native seekable `SftpFileStream`, host-key confirmation dialog, write ops | 1–2 d |
| **M11 — Nav + UX** | Shares column only for SMB, per-protocol dialog fields, FTPS mode, per-protocol icons | 0.5–1 d |
| **M12 — Multi-protocol tests + docs** | Real FTP/FTPS/SFTP smoke (desktop), docset updates, Xbox validation | 1 d |

Each milestone has a task checklist in `IMPLEMENTATION.md`.

## Risks

| Risk | Impact | Mitigation |
|---|---|---|
| Xbox Developer Mode blocks port 445 / outbound SMB | Whole SMB path dead on console | Test early (M6). SMBLibrary is pure socket (no kernel redirector) — better odds than UNC. Fallback: UNC P/Invoke already works on desktop |
| SMBLibrary LGPL-3.0 | Licensing overhead if Store distribution | OK for sideload (dynamic link); review before any Store submission |
| .NET Native / UWP quirks in SMBLibrary | AOT/reflection issues | SMBLibrary is netstandard2.0; `UseDotNetNativeToolchain` is off (JIT) — verify at M6 |
| AudioGraph needs a `StorageFile` | No direct stream audio | Growing-file streaming (chiptune precedent) starts playback in ~1–2 s |
| Wi-Fi instability on Xbox | Frozen UI / hangs | Timeouts + `CancellationToken` on every socket op; error surfaces as a friendly "connect failed" |
| Large remote media / seek | Seek stalls beyond downloaded region | v1 = sequential play; full seek after complete (documented in SPEC) |
| FTP servers without REST | No offset reads → non-seekable stream | Probe `FEAT`/REST at connect; seekable when supported, sequential-play otherwise (no whole-file download) |
| FTP active/passive + NAT | Passive-mode firewall issues | Passive mode default; M12 smoke decides fallback |
| SSH.NET UWP compatibility | Modern netstandard2.0 build pulls .NET 8 deps (BouncyCastle/Logging/Asn1) | Try latest; fallback pin 2020.0.2 (known UWP-safe) |
| SSH host-key MITM | Fingerprint not verified | Confirmation dialog on first connect; persisted accepted keys; mismatch = warning (ADR-NF-011) |
| Location config wiped | User data loss | Config table excluded from Clear Cache (verified: only cache tables wiped) |

## Dependencies

- `TalAloni.SMBLibrary` (1.5.x, netstandard2.0, LGPL-3.0) — SMB2 client.
- `FluentFTP` (54.x, netstandard2.0, MIT) — FTP/FTPS client (M9).
- `Renci.SshNet` (try latest netstandard2.0; fallback pin 2020.0.2 — UWP-safe
  build, see ADR-NF-010) — SFTP client (M10).
- Existing: `sqlite-net-pcl` + `SQLitePCLRaw.bundle_green` (metadata.db),
  `Windows.Security.Credentials.PasswordVault`, `DownloadService`,
  `AudioLevelService` (growing-file support), `FolderBrowserDialog`.
- No new manifest capabilities (verified present:
  `internetClient`, `internetClientServer`, `privateNetworkClientServer`).

## Process notes (between sessions)

- Always start from `IMPLEMENTATION.md` — the checklist with status is the
  single source of truth for what is done and what is next.
- After any structural change run the VS2026 MSBuild verification command from
  `../../AGENTS.md`.
- Add new `.cs` files to `XFiles.csproj` (explicit item lists) — forgetting
  silently excludes them from the build.
- Keep `SPEC.md`/`ARCHITECTURE.md` updated as reality diverges from this plan.
