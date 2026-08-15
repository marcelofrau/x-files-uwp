---
layout: default
title: Portal AppData Browser — Implementation Plan
---
# Portal AppData Browser — Implementation Plan

Status: **in progress** (started 2026-08-03). Track progress in `CHECKLIST.md`.

## 1. Goal

Let X-Files (running on the console, in Developer Mode) browse, preview, edit,
and manage **other apps' `LocalAppData` and `DevelopmentFiles`** folders through
the Xbox Dev Portal REST API — without a PC.

The portal is reachable only while the app has a **loopback exemption**
(`checknetisolation loopbackexempt -a -n=<PFN>`), which requires elevation a UWP
app cannot get on its own (chicken-egg problem — see below). The exemption is
applied externally and survives app relaunch but is lost on re-install and
console reboot.

## 2. Background

### 2.1 Why the exemption is external (and stays external)

The app's own `NetworkIsolationSetAppContainerConfig` call (self-exempt during
the probe) was tested on console and **did not work** — the exemption list is
set/owned outside the app's control, and the call does not persist across
relaunch. This is a confirmed chicken-egg: the app needs the exemption to reach
the portal that would grant it. **Self-exempt is abandoned.**

The exemption is therefore applied from the outside, one of three ways:

1. **XB Homebrew Vault** (PC, Avalonia) — Tools view → "Loopback Exempt"
   wizard (SSH + `checknetisolation`). Already implemented.
2. **Scripts shipped in the release zip** — `tools/liberate-loopback.ps1`
   (Windows/pwsh 7+, plink) and `tools/liberate-loopback.sh` (bash + sshpass).
   Already in the repo and packaged by `scripts/package.ps1`.
3. **Manual** — SSH/`checknetisolation` by hand, from any device (including
   Android via Termux). Documented in `docs/PORTAL-APPDATA.md`.

The X-Files "Portal" entry tells the user about all three (including a QR code
to the documentation) when it cannot connect.

### 2.2 Reference implementation

`xb-homebrew-vault` already implements the same REST client
(`PortalAppFilesService`, `XboxAuthService`) plus the exemption wizard. The
X-Files probe already validated the exact endpoints used here
(`DevicePortalService.DeepProbeAsync`). Real `curl` examples live in
`xb-homebrew-vault/docs/curl-examples.txt` (credentials sanitized in our docs).

### 2.3 Confirmed API facts (from real captures + vault `portal-filesystem-api.md`)

- `GET /api/filesystem/apps/knownfolders` → `{"KnownFolders":["DevelopmentFiles","LocalAppData"]}`
- `GET /api/app/packagemanager/packages` → `{"InstalledPackages":[{Name, PackageDisplayName, PackageFamilyName, PackageFullName, PackageOrigin, PackageRelativeId, Version}]}`
- `GET /api/filesystem/apps/files?knownfolderid=&packagefullname=&path=` → `{"FullPath":..., "Items":[{Name, Type, FileSize, DateCreated, SubPath, CurrentDir}]}`
  - `Type` bit `0x10` = directory (use `(Type & 0x10) != 0`). `Type == 32` → file.
  - **Omit `packagefullname`** → lists installed packages instead of files.
- **Path conventions**: backslash-separated, always starts `\\`. Root of a
  known folder = `\` (encoded `%5C`), one level = `\\teste`, two = `\\teste\\sub`.
  For `LocalAppData`, `packagefullname` is a separate query param, NOT part of path.
- `GET /api/filesystem/apps/file?filename=&packagefullname=&path=` → raw bytes.
  - **Gotcha**: `filename` is a separate query parameter; `path` = parent folder
    only. Putting the filename at the end of `path` returns **404**. No Range support.
- **Write operations require CSRF**: cookie `CSRF-Token=<token>` + header
  `X-CSRF-Token: <token>`. Token from `GET /api/os/info` (fallback `GET /`).
  **Re-fetch on 403** (one `HttpClient` + shared cookie container per console).
  - `DELETE /api/filesystem/apps/file?filename=&packagefullname=&path=` → delete entry.
  - `POST /api/filesystem/apps/file?...&extract=false` → upload (multipart, see below).
  - `POST /api/filesystem/apps/folder?newfoldername=&path=` → create folder inside `path`.
  - `POST /api/filesystem/apps/rename?filename=&newfilename=&path=` → rename (path = parent).
- **Upload multipart format is critical**: WDP accepts the browser's format
  ONLY; .NET `MultipartFormDataContent` default is rejected with 500. Working
  format: `Content-Disposition` **first**, `name="file"` quoted, `filename="x"` quoted
  plain (no `filename*`), part `Content-Type: application/octet-stream`,
  Content-Length set (never chunked). Hand-roll with `StreamContent` +
  `ContentDispositionHeaderValue.Parse(...)` (no `name` overload).
- **`extract=true` ZIP upload is broken on Xbox** (500 `UPDxxxx.tmp`) — server-side
  unzip/archive upload is out of scope. Portal ZIP operations instead download to a
  temp staging dir, run zip/unzip locally (SharpCompress), then upload the result
  file-by-file via `extract=false` multipart (`UploadLocalToPortalAsync`).
- **500 `{"Reason":"...WdpTempWebFolder\\UPDxxxx.tmp"}` diagnosis**: wrong
  multipart (every upload fails) → console state (reboot dev mode, package
  quota full, stale `UPD*.tmp`) → not CSRF (successful CreateFolder proves token).
- Auth: HTTP Basic (`portal user:password`), HTTPS self-signed → cert-ignore
  HTTP filter (`ChainValidationResult.Untrusted/InvalidName/Expired/RevocationFailure`).

## 3. Requirements

### 3.1 Functional

- Portal entry **always visible** at the drive-list root: `"User Folders"`.
- Drill-in when **not connected** → setup modal with detailed exemption
  instructions (vault / script / manual + Termux) and a **QR code** to the docs
  page.
- Credentials: if connection returns **401 / access denied**, prompt for portal
  **user + password**; persist them (SQLite `XFilesSettings`) so the console
  does not need the build-time `.env`.
- Browse tree: `Portal → { LocalAppData, DevelopmentFiles }` →
  (LocalAppData → installed packages) → files/subfolders.
- Preview via internal managed cache (see §5.2): small files (≤ 25 MB)
  auto-download; larger files / playback / archive drill-in download with a
  progress dialog.
- Write operations: **Download (Copy to disk)**, **Rename**, **Delete**,
  **New Folder**, **Upload file** — all with existing confirmation/progress UX.
- Edit portal text files: edit the cached copy; **Save writes back to the
  portal** (upload).

### 3.2 Non-goals (this pass)

- In-app self-exempt (abandoned, chicken-egg).
- True streaming from the portal (UWP media stack cannot ignore the
  self-signed cert; portal Range support unconfirmed). All playback uses the
  local cache.
- Write beyond the chosen set (no bulk ops, no move-between-packages).
- Browse `DevelopmentFiles` write ops initially follow the same portal
  endpoints but are validated manually.

## 4. Architecture

Layers (top → bottom), mirroring existing patterns:

```
XAML Views (dialogs)
  └── MillerColumnsPage (root injection, dialogs wiring, action handlers)
        └── ColumnNavigator (portal state machine) ── owns PortalBrowser
              ├── DevicePortalService (REST + creds + CSRF + 401)
              ├── PortalBrowser (virtual listing / download / write)
              └── PortalCache (managed temp cache, 2 GB LRU)
```

Virtual-folder precedents reused: **Favorites** (root virtual entry +
`IsVirtual`) and **ArchiveBrowser** (virtual listing provider +
`ArchiveRootPath`/`ArchiveInternalPath`). Portal entries are the same shape with
`IsPortal` + portal metadata fields.

### 4.1 Data flow

1. `LoadRootAsync` injects `"User Folders"` (always).
2. Drill-in: `ColumnNavigator.DrillIntoPortalAsync` → if not connected, raise
   `PortalSetupRequired` (MillerColumnsPage shows `PortalSetupDialog`). If
   connected → `PortalBrowser.ListKnownFoldersAsync()`.
3. Known folder → (LocalAppData) package list | (DevelopmentFiles) file list.
4. Package → file list. Folder → deeper file list (`PortalPath` grows).
5. Preview: portal file → `PortalCache.EnsureAsync` (auto if ≤ 25 MB) →
   `FilePreviewService.GetPreviewAsync(cachePath)`.
6. Playback / large files / zip drill-in: explicit open → `EnsureAsync` with
   `OperationProgressDialog` → existing local-file pipeline.
7. Write ops → `DevicePortalService` write endpoints (CSRF attached) →
   confirm dialog → toast/log.

## 5. Component design

### 5.1 `XFiles/Services/DevicePortalService.cs` (refactor + extend)

Today: static, private probe helpers, creds from `DevPortalSecrets.g.cs`
(generated from `.env` at build). Add:

- **Runtime credentials**
  - `SetCredentials(string user, string pass)` — in-memory override; takes
    precedence over the generated secrets.
  - `HasCredentials` → secrets OR runtime creds.
  - `IsPortalConnected` → `_baseUrl != null`.
  - `CredentialsRequired` event — raised once per session on HTTP 401 (re-armed
    on drill-in); MillerColumnsPage opens `PortalCredentialsDialog`.
- **Persistent portal client** (static, lazily created)
  - Cert-ignore `HttpBaseProtocolFilter` + `HttpClient`, Basic auth header.
  - Cookie persistence (needed for CSRF) via the filter's `CookieManager`.
- **Public read API**
  - `GetKnownFoldersAsync()` → `List<string>`
  - `GetInstalledPackagesAsync()` → `List<PortalPackage>` (filters
    system/framework like the probe: `PackageOrigin`/`IsFramework`).
  - `ListPortalFilesAsync(knownFolder, packageFullName, portalPath)` →
    `List<PortalFileEntry>`.
  - `DownloadPortalFileAsync(entry, Stream dest, IProgress<double>)` —
    streaming, no full byte[] in memory.
- **Public write API** (all ensure CSRF first)
  - `DeletePortalEntryAsync`, `RenamePortalEntryAsync`,
    `CreatePortalFolderAsync`, `UploadPortalFileAsync(fileName, Stream, IProgress)`.
  - Upload body is **hand-rolled** multipart (browser format, §2.3) via
    `StreamContent` + `ContentDispositionHeaderValue.Parse` — never the
    `MultipartFormDataContent` name-overload (500 on Xbox).
- **CSRF**: `EnsureCsrfAsync()` — GET `/api/os/info` (fallback `/`), read the
  `CSRF-Token` cookie via `filter.CookieManager`, cache the token, attach
  `X-CSRF-Token` header to writes. On **403** during a write → drop token,
  `EnsureCsrfAsync()` again, retry once.
- **401 handling**: any portal call that returns 401 sets `_accessDenied` and
  raises `CredentialsRequired` (guarded).
- Small models live in this file: `PortalPackage`, `PortalFileEntry`.

### 5.2 `XFiles/Services/PortalCache.cs` (new)

Managed preview/edit/playback temp store.

- Root: `ApplicationData.Current.LocalFolder.Path\portal-cache\`.
- **Key**: `knownFolder|package|path|name` + `size` + `dateCreated` → a single
  file is downloaded once and reused by preview, playback, and archive drill-in.
- **Budget: 2 GB** (`long` bytes) with **LRU eviction** (in-memory access map;
  evict oldest beyond budget).
- **Cleared at app launch** — no cross-session accumulation.
- API: `GetCachedPath(entry)`, `EnsureAsync(entry, IProgress<double>)`,
  `ClearAsync()`, internal `EvictIfNeededAsync()`.
- Thumb rule for auto vs explicit download lives in the caller
  (`ColumnNavigator`/media): `AutoPreviewMaxBytes = 25 MB`.

### 5.3 `XFiles/FileSystem/PortalBrowser.cs` (new)

Mirrors `ArchiveBrowser`: virtual listing provider + portal operations, owned by
`ColumnNavigator`.

- `ListKnownFoldersAsync()` → `LocalAppData`, `DevelopmentFiles` dirs.
- `ListPackagesAsync()` → installed (non-system) packages as dirs.
- `ListDirectoryAsync(knownFolder, packageFullName, portalPath)` → files +
  subdirs as `FileEntry` (portal fields set; `IsArchive` set for zip files so
  archive drill-in works after caching).
- `DownloadToCacheAsync(entry, IProgress)` → `PortalCache.EnsureAsync`.
- Write passthroughs to `DevicePortalService`.

### 5.4 `FileEntry.cs` + `ColumnState`

`FileEntry` adds:
- `IsPortal`
- `PortalKnownFolder` (`null` on the "Portal" root / known-folder entries carry
  their folder, packages carry `LocalAppData`)
- `PortalPackageFullName`
- `PortalPath` (portal-internal directory, e.g. `\\Settings`)

`ColumnState` adds the same four, plus `LoadPortalDirectoryAsync(...)`.

Level inference (from the four fields):
- all null → Portal root (not a real column; the root entry is virtual).
- `KnownFolder == null` → known-folder list.
- `KnownFolder set, Package == null` → DevelopmentFiles file list, or
  LocalAppData package list (distinguish by folder name).
- `KnownFolder + Package set` → file list at `PortalPath`.

### 5.5 `ColumnNavigator.cs`

- `LoadRootAsync`: inject the `"User Folders"` virtual entry (always).
- `DrillInAsync`: `selected.IsPortal` → `DrillIntoPortalAsync()` (before the
  generic `IsVirtual` favorites path). `_current.IsPortal` → drill into portal
  sub-level. `selected.IsArchive && _current.IsPortal` → zip drill-in (cache the
  zip first, then `DrillIntoArchiveAsync` on the cached path).
- Not connected on portal drill-in → raise `PortalSetupRequired` (new event),
  return without navigating.
- `UpdatePreviewAsync`: portal dir → preview lists via
  `LoadPortalDirectoryAsync`; portal file → if `SizeBytes ≤ AutoPreviewMaxBytes`
  → `PortalCache.EnsureAsync` → `GetPreviewAsync(cachePath)`; else show a
  metadata card ("large file — open to download", size/date) without download.
  Skip gamelist enrichment for portal columns.
- `DrillOutAsync` / `RefreshCurrentAsync`: reload portal columns from the API
  (mirrors the Favorites reload).
- `_gamelistCache` untouched for portal paths.

### 5.6 Dialogs (new, BladeTheme-conformant, gamepad focus)

- **`PortalCredentialsDialog`** — user text + password (PasswordBox), OK/Cancel.
  Submit → `DevicePortalService.SetCredentials` → persist via
  `XFilesSettings.SetStringAsync("PortalUser"/"PortalPass")` →
  `ProbeAsync(force: true)`.
- **`PortalSetupDialog`** — shown on drill-in when not connected. Contains:
  exemption instructions (vault wizard / zip script / manual SSH + Termux), a
  **QR code** (ZXing `BarcodeWriterGeneric`, same as `ShareDialog`) pointing to
  `https://github.com/marcelofrau/x-files-uwp/blob/main/docs/PORTAL-APPDATA.md`,
  a "Enter credentials" button, and a "Re-probe (About+Y)" hint.

### 5.7 `App.xaml.cs`

`OnLaunched`: load persisted creds from `XFilesSettings` → `SetCredentials` →
`ProbeAsync()` (fire-and-forget). Console runs without `.env`.

### 5.8 Media / editor integration

- **Playback**: when opening a portal file (audio/video), ensure it is cached
  (`PortalCache.EnsureAsync` with `OperationProgressDialog`) then hand the
  cache path to the existing pipeline (`AudioLevelService.LoadAndPlay`,
  `MediaSource.CreateFromUri`). Prev/next fullscreen navigation reuses the
  cache.
- **Edit**: `TextEditorOverlay.Show(filePath, portalEntry)` — store the portal
  origin; on save, after `TextEditorService.SaveAsync(cachePath, ...)`, upload
  the cached copy back to the portal (progress/toast).

### 5.9 Action sheet + icon

`FileActionSheet.ShowAsync` portal branch:
- files: **Download**, **Edit** (text), **Rename**, **Delete**
- dirs: **New Folder**, **Upload file**, **Rename**, **Delete**, **Refresh**
- all: **Refresh**

Handlers in `MillerColumnsPage.FileOps.cs`. Reuse
`FileOperationConfirmDialog` (delete), `OverwriteDialog` (download collisions),
`OperationProgressDialog` (download/upload/playback download), and existing
`FolderBrowserDialog`/file-picker patterns for destinations.

`ColumnListView` icon mapping: portal entries get a distinct icon (asset check
via `assets-icons`/`fileexplorer-icons` skills; fallback to a folder/cloud
style icon).

### 5.10 `XFiles.csproj`

The project uses **explicit** `<Compile Include>` / `<Page Include>` entries —
every new `.cs` / `.xaml` file must be registered there.

## 6. Implementation order

1. Docs (this folder + `PORTAL-APPDATA.md`/`README-LIBERATE.md` updates last).
2. `DevicePortalService` refactor (creds, client, read API, CSRF, write, 401).
3. `PortalCache`.
4. `PortalBrowser` + `FileEntry` fields + `ColumnNavigator` state machine.
5. Dialogs (`PortalCredentials`, `PortalSetup` + QR) + `App.xaml.cs` startup.
6. Media/editor integration + action sheet handlers + icon.
7. `XFiles.csproj` includes.
8. Build (MSBuild Debug x64) + manual test script below.

## 7. Decisions (record)

| # | Decision | Rationale |
|---|---|---|
| D1 | Self-exempt abandoned | Confirmed chicken-egg on console; exemption must be external. |
| D2 | Read + write ops | User requirement; CSRF mechanism confirmed from vault. |
| D3 | Entry always visible | User requirement; drill-in gate shows setup modal instead of hiding. |
| D4 | Credentials persisted (SQLite LocalState) | Console has no `.env`; avoids retyping each launch. Plaintext locally (same trust as `.env`). |
| D5 | PortalCache, 2 GB, LRU, clear-on-launch | No uncontrolled temp copying; one download reused by preview/playback/zip. |
| D6 | Auto-download ≤ 25 MB; larger = explicit with progress | Avoids cache churn on hover/scroll for big files. |
| D7 | Playback via local cache, not portal streaming | UWP media stack ignores cert-ignore; portal Range unconfirmed. |
| D8 | QR → GitHub blob docs page | Stable URL; doc is maintained in-repo. |
| D9 | Mirror vault portal paths (DevelopmentFiles + LocalAppData) | Proven endpoint shapes; parity with vault UX. |

## 8. Risks

- JSON shapes vary across OS builds → defensive parsing (probe + vault already
  validated the common shape).
- HTTPS self-signed → cert-ignore filter already proven in probe.
- Large file preview over HTTP → 25 MB auto-download cap + progress dialogs.
- Concurrent probe + browse → single persistent client; probe uses its own
  per-test clients.
- CSRF token expiry → on 403 drop token, re-fetch, retry once.
- Upload 500 (`WdpTempWebFolder` error) → verify hand-rolled multipart first;
  if format is correct, surface the portal reason string to the user and suggest
  rebooting dev mode / checking package quota (see §2.3 diagnosis order).

## 9. Verification (manual, on console)

1. Build + deploy. First launch, no creds, no exemption:
   - `User Folders` visible at root; drill-in → setup modal with QR; QR
     decodes to the docs page.
2. Enter wrong creds via the credentials dialog → 401 → dialog reappears.
3. Enter correct creds → persisted. Apply exemption via script/vault.
   - `About + Y` → probe `OK`.
   - Drill into `Portal` → `LocalAppData` → `XFiles.Xbox...` →
     `Settings` → preview `settings.dat` (hex) via cache; `roaming.lock`
     small text.
4. Download a file to a chosen folder → checksum matches the portal copy.
5. Rename / New Folder / Upload / Delete a throwaway entry on a test package —
   verify in the portal's own browser.
6. Playback: open a medium MP3 (≤ 25 MB) auto; a large video → progress dialog
   → plays; prev/next reuses cache.
7. Zip drill-in: small zip auto-caches and browses internally; large zip → open
   with progress.
8. Edit a portal text file → save → file updated in the portal browser.
9. Relaunch app: creds remembered; exemption still needed (About+Y after
   re-applying).
10. Reboot console: exemption gone → drill-in shows setup modal again.

## 10. Related docs

- `docs/PORTAL-APPDATA.md` — main doc; §6 becomes "implemented", adds curl
  cheat-sheet, Termux/manual, credentials, cache notes.
- `tools/README-LIBERATE.md` — Termux manual.
- `docs/ARCHIVES.md` — virtual-folder precedent (ArchiveBrowser).
- `xb-homebrew-vault/docs/feature-loopback-exempt.md` — vault wizard spec.
- `xb-homebrew-vault/docs/portal-filesystem-api.md` — **primary REST reference**:
  exact query params, path conventions, multipart format, error diagnosis.
