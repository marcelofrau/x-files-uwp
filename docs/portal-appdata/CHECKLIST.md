---
layout: default
title: Portal AppData Browser — Implementation Checklist
---
# Portal AppData Browser — Implementation Checklist

Plan: `PLAN.md`. REST reference: `xb-homebrew-vault/docs/portal-filesystem-api.md`.

Legend: `[ ]` todo · `[x]` done · `[~]` in progress · `[!]` blocked/failed.

## 1. Docs (this pass)

- [x] `docs/portal-appdata/PLAN.md` — written, decisions + API facts current.
- [x] `docs/portal-appdata/CHECKLIST.md` — this file.
- [x] `docs/PORTAL-APPDATA.md` — §6 "implemented"; add curl cheat-sheet,
      Termux/manual, credentials (SQLite), cache notes, upload gotcha.
- [x] `tools/README-LIBERATE.md` — cross-reference portal entry + QR.

## 2. `XFiles/Services/DevicePortalService.cs` (refactor)

- [x] Runtime creds: `SetCredentials(user, pass)` in-memory override.
      **No `.env`/`DevPortalSecrets` dependency** — creds come only from SQLite
      (`PortalUser`/`PortalPass`), loaded at startup + saved by the dialog.
- [x] `HasCredentials`, `IsPortalConnected`, `CredentialsRequired` event (401).
- [x] Persistent portal client: cert-ignore filter + `HttpClient` + Basic auth +
      shared `CookieManager`.
- [x] Read API: `GetKnownFoldersAsync`, `GetInstalledPackagesAsync`,
      `ListPortalFilesAsync(knownFolder, packageFullName, portalPath)`.
- [x] `DownloadPortalFileAsync(entry, Stream dest, IProgress<double>)` — streaming;
      **`filename` = separate query param, `path` = parent folder only**.
- [x] Models: `PortalPackage`, `PortalFileEntry` (Type & 0x10 = dir).
- [x] CSRF: `EnsureCsrfAsync` (cookie from `/api/os/info`, fallback `/`),
      `X-CSRF-Token` header on writes, re-fetch + retry once on 403.
- [x] Write API: `DeletePortalEntryAsync`, `RenamePortalEntryAsync`,
      `CreatePortalFolderAsync`, `UploadPortalFileAsync` (hand-rolled browser
      multipart via `StreamContent` + `ContentDispositionHeaderValue.Parse`).
- [x] 401 on any call → set `_accessDenied`, raise `CredentialsRequired` (guarded).
- [x] Build passes (MSBuild Debug x64, build 1135).

## 3. `XFiles/Services/PortalCache.cs` (new)

- [x] Root `LocalState\portal-cache\`; key = knownFolder|package|path|name +
      size + dateCreated.
- [x] `GetCachedPath`, `EnsureAsync(entry, IProgress<double>)`.
- [x] Budget 2 GB LRU (`long` bytes) + `EvictIfNeededAsync`.
- [x] `ClearAsync()` on app launch.
- [x] No re-download on repeated access (preview + playback + zip reuse).

## 4. `XFiles/FileSystem/PortalBrowser.cs` (new)

- [x] `ListKnownFoldersAsync` → LocalAppData, DevelopmentFiles.
- [x] `ListPackagesAsync` → installed non-system packages as dirs.
- [x] `ListDirectoryAsync(knownFolder, packageFullName, portalPath)` → `FileEntry`
      with portal fields + `IsArchive` for zips.
- [x] `DownloadToCacheAsync` → `PortalCache.EnsureAsync`.
- [x] Write passthroughs → `DevicePortalService` (via models).
- [x] Models moved to public `PortalModels.cs` (accessibility fix).

## 5. `FileEntry.cs` + `ColumnState` + `ColumnNavigator.cs`

- [x] `FileEntry`: `IsPortal`, `PortalKnownFolder`, `PortalPackageFullName`,
      `PortalPath`.
- [x] `ColumnState`: same four + `LoadPortalDirectoryAsync(...)` (+KnownFolders/Packages).
- [x] `LoadRootAsync`: inject `"User Folders"` (always visible).
- [x] `DrillInAsync`: portal branch before generic `IsVirtual` favorites;
      not connected → `PortalSetupRequired` event, no navigation.
- [x] Zip drill-in from portal: cache zip first, then `DrillIntoArchiveAsync`.
- [x] `UpdatePreviewAsync`: portal dir → list; portal file ≤ 25 MB → auto-cache →
      `GetPreviewAsync(cachePath)`; larger → metadata card (size/date), no download.
- [x] Skip gamelist enrichment for portal columns.
- [x] `DrillOutAsync` / `RefreshCurrentAsync`: reload portal from API
      (`ReloadPortalColumnAsync`).
- [x] Build passes (build 1133).

## 6. Dialogs + startup

- [x] `PortalCredentialsDialog` (user + PasswordBox, OK/Cancel, gamepad).
- [x] `PortalSetupDialog` (exemption instructions, QR → docs, Enter
      credentials, Re-probe, gamepad).
- [x] `XFilesSettings` portal credential persistence (`PortalUser`/`PortalPass`).
- [x] MillerColumnsPage: dialogs in XAML, router handlers, event wiring
      (`PortalSetupRequired`, `CredentialsRequired`), `IsAnyOverlayVisible`.
- [x] `App.xaml.cs` startup: load creds → `SetCredentials` → `ClearAsync` →
      `ProbeAsync` (fire-and-forget).
- [x] Build passes (build 1134).

## 7. Media / editor / ops / icon

- [x] Playback: ensure cached (progress dialog) → existing local pipeline;
      prev/next reuses cache.
- [x] Edit: `TextEditorOverlay` portal origin; save → write cache + upload back.
- [x] `FileActionSheet` portal branch: files Download/Edit/Rename/Delete; dirs
      New Folder/Upload/Rename/Delete/Refresh.
- [x] Handlers in `MillerColumnsPage.FileOps.cs` (`HandlePortalDownloadAsync` →
      `FolderBrowserDialog` + `OverwriteDialog` + `PortalBrowser.DownloadToDiskAsync`;
      `HandlePortalUploadAsync` → `FileOpenPicker` +
      `DevicePortalService.UploadPortalFileAsync`); reuse confirm/progress dialogs.
- [x] `ColumnListView` icon for portal entries (fallback folder/file-type icon —
      portal entries carry no explicit icon).

## 8. Build + registration

- [x] Register every new `.cs` / `.xaml` in `XFiles.csproj` (explicit includes).
- [x] MSBuild Debug x64: `& "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" "XFiles.sln" /p:Configuration=Debug /p:Platform=x64 /t:Build /v:minimal` (build 1135).

## 9. Manual verification (console, per PLAN §9)

- [ ] V1: `User Folders` visible; drill-in no creds/no exemption → setup modal
      + QR decodes to docs.
- [ ] V2: wrong creds → 401 → dialog reappears.
- [ ] V3: correct creds persisted; exemption applied; probe OK; browse
      LocalAppData → package → Settings; preview small file via cache.
- [ ] V4: download → checksum matches.
- [ ] V5: rename / new folder / upload / delete on throwaway entry — verified in
      portal browser.
- [ ] V6: ≤25 MB audio auto; large video → progress → plays; prev/next reuses cache.
- [ ] V7: zip drill-in small auto + large with progress.
- [ ] V8: edit portal text → save writes back.
- [ ] V9: relaunch → creds remembered.
- [ ] V10: reboot → exemption gone → setup modal again.
