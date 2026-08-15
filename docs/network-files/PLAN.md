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

### In scope — this delivery (SMB only)

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

### Out of scope — this delivery

- FTP/FTPS, WebDAV, SFTP implementations (architecture accommodates them; see
  below).
- NFS and DLNA/UPnP (no roadmap commitment — see Protocol priority).
- Remote **write** operations (copy/move/rename/delete *to* the remote),
  archives *hosted* on the remote (deferred to a later slice).
- Network discovery (WSD/SSDP/mDNS) — manual entry only.
- `enterpriseAuthentication` capability — only needed if a domain/Kerberos SMB
  auth case appears; not added now.

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
| Location config wiped | User data loss | Config table excluded from Clear Cache (verified: only cache tables wiped) |

## Dependencies

- `TalAloni.SMBLibrary` (1.5.x, netstandard2.0, LGPL-3.0) — SMB2 client.
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
