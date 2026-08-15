---
layout: default
title: File Shares (SMB/UNC) — Feasibility Assessment
---
# File Shares (SMB/UNC) — Feasibility Assessment

*Status: **Deferred**. Not in MVP scope. Documented for future implementation.*

## Overview

Support browsing Windows File Shares (SMB protocol) via UNC paths like
`\\server\share\folder\file.txt`. Enables navigation of NAS devices, Windows
shared folders, and Samba/CIFS servers on the local network.

## What Already Works (No Changes Needed)

| Component | UNC Support |
|---|---|
| `FindFirstFileExFromAppW` | Accepts `\\server\share\*` natively — kernel redirects via SMB redirector (`mrxsmb.sys`) |
| `DirectoryScanner.ScanDirectoryAsync` | Works with any UNC path passed as `path` parameter |
| `FileOperations` (copy/move/rename/delete) | P/Invoke `CreateFile2FromAppW` handles UNC transparently |
| `FilePreviewService` (text/image/media) | Same P/Invoke read path — no changes needed |
| `ArchiveBrowser` (zip/7z/rar on UNC) | Reads via `CreateFile2FromAppW`, works on UNC |
| `FileEntry.IsVirtual` | Can reuse Favorites pattern — inject "Network" entry at root |
| `InputDialog` | Can accept `\\server\share` text input from user |
| `XFilesSettings` + SQLite | Ready for `"NasServers"` JSON key to persist server list |

## What Blocks Implementation

### 1. Missing Manifest Capabilities

Add to `Package.appxmanifest`:

```xml
<Capability Name="privateNetworkClientServer" />
<rescap:Capability Name="enterpriseAuthentication" />
```

- `privateNetworkClientServer` — required for any local network traffic
- `enterpriseAuthentication` — required if SMB share uses domain/Kerberos auth

### 2. No Network Server Discovery

`GetLogicalDrives()` returns only local drive letters. Network shares must be
enumerated via alternative methods:

- **Manual entry**: User types `\\server\share` in InputDialog
- **`Windows.Networking.Enumeration`**: Can discover devices via WSD/SSDP/UPnP
  but SMB share discovery (NetBIOS/WSD) is unreliable from UWP
- **mDNS/Zeroconf**: Not natively available in UWP

**Recommendation**: Start with manual UNC entry (like "Connect to Server" dialog)
and optionally add discovery later.

### 3. Authentication

- Anonymous/public shares: No credential handling needed
- Password-protected shares: UWP `StorageFile` API prompts user automatically,
  but P/Invoke `CreateFile2FromAppW` may fail with `ERROR_ACCESS_DENIED`.
  Solution: use `Windows.Networking.Sockets` based SMB library or collect
  credentials via InputDialog and use `WNetUseConnection` P/Invoke.

### 4. Xbox Developer Mode Unknowns

- Port 445 (SMB) may be blocked by Xbox firewall/network stack
- Xbox Developer Mode network isolation may prevent inbound/outbound SMB
- Unknown whether `FindFirstFileExFromAppW` on UNC works on Xbox at all
- **Requires physical testing** on Xbox Series S|X in Developer Mode

### 5. Disconnect Resilience

- NAS going offline during navigation would cause `FindFirstFileExFromAppW`
  to hang or throw `ERROR_BAD_NETPATH` / `ERROR_NETWORK_UNREACHABLE`
- Need timeout/cancellation wrappers and error handling in `ScanDirectoryAsync`

## Proposed Architecture

```
Root column: [Favorites] [Network] [D:] [E:] [G:] [H:]

Network entry (virtual):
  - Name: "Network"
  - IsVirtual: true
  - Same pattern as Favorites

Drill into Network:
  1. Show list of saved NAS servers (from "NasServers" settings key)
  2. If list is empty or user wants to add: show InputDialog
  3. InputDialog: "Connect to Server" — user types \\server\share
  4. Optionally: Username / Password fields
  5. On connect: ScanDirectoryAsync(uncPath) and display results
  6. Navigate normally through the remote folder tree
  7. Preview, media, archives all work via same P/Invoke paths

Persistent storage:
  - Settings key "NasServers": JSON array of {DisplayName, UncPath}
  - Same pattern as FavoritesManager.cs
```

## Implementation Order (When Phased)

1. Add `privateNetworkClientServer` + `enterpriseAuthentication` capabilities
2. Create `NetworkServerManager.cs` (load/save/add/remove NAS entries)
3. Create "Connect to Server" dialog (reuse InputDialog or build simple overlay)
4. Inject "Network" virtual entry at root in `ColumnNavigator.LoadRootAsync`
5. Handle drill-in: show NAS list → connect → scan UNC
6. Error handling: timeout, bad path, disconnected mid-browse
7. Test on Xbox in Developer Mode

## Estimated Effort

- **Coding**: 2-3 days
- **Xbox testing**: 1-2 days (including port 445 verification)
- **Total**: ~3-5 days depending on authentication support
