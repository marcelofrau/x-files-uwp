---
layout: default
title: Network File Access — Architecture
---
# Network File Access — Architecture

SMB-first remote file browsing. Follows the **portal / archive "virtual
folder" precedent** (see `docs/DECISIONS.md`) rather than refactoring the
local filesystem layer into a provider-abstraction. Remote entries carry
addressing in their own fields (`FullPath = null`), and each existing pipeline
gains a small `IsNetwork` branch.

## Layer map

```
UI (Controls)
 ├─ MillerColumnsPage (root injection, drill, action rows, preview dispatch)
 ├─ NetworkLocationDialog          (new — add/edit location form)
 ├─ FolderBrowserDialog            (generalized confirmLabel)
 ├─ FileActionSheet                (Y-menu: rename/delete location; download removed)
 └─ MediaPreviewControl            (network branch: stream sources)
Navigation
 └─ ColumnNavigator                (root "Network" entry, drill-in/out, virtual stack)
Network (new namespace XFiles.Network)
 ├─ INetworkFileSystemProvider     (protocol-agnostic contract, read + write ops)
 ├─ NetworkProviderFactory          (per-protocol browser resolution)
 ├─ NetworkServerConfig            (pure model + URL composition)
 ├─ NetworkServerManager           (SQLite CRUD + PasswordVault)
 ├─ SmbSession                     (SMB2Client connect/login/treeconnect pool)
 ├─ SmbBrowser                     (shares + directory listing → FileEntry)
 ├─ SmbReadStream / SmbWriteStream (Stream over ISMBFileStore read/write)
 ├─ FtpSession / FtpBrowser        (FluentFTP; pool, list, read/write)  [M9]
 ├─ FtpReadStream / FtpWriteStream (REST-aware seekable read, upload)   [M9]
 ├─ SftpSession / SftpBrowser      (SSH.NET; pool, list, read/write)    [M10]
 ├─ HostKeyTrustStore              (persisted accepted SFTP fingerprints) [M10]
 ├─ WebDavSession / WebDavBrowser  (HttpClient; PROPFIND, Range, PUT)   [M14]
 └─ WebDavReadStream               (seekable read via HTTP Range)       [M14]
Existing (reused)
 ├─ DirectoryScanner / FileEntry / PortalBrowser / ArchiveBrowser
 ├─ FilePreviewService / TextEditorService
 ├─ AudioLevelService (growing-file) / RetroAudioPlayer (chiptune render)
 ├─ DownloadService / UrlDownloadOverlay
 └─ MetadataCache (metadata.db, migration v2→v3) / PasswordVault
```

## Addressing

`FileEntry` gains network fields; existing portal fields stay untouched:

| New field | Type | Meaning |
|---|---|---|
| `IsNetwork` | bool | Entry lives on a remote share |
| `ActionKind` | enum `{ None, AddLocation, DownloadUrl }` | Action row marker (generalizes the earlier `IsAddLocation` idea) |
| `NetworkServerId` | int | FK → `NetworkServerEntry.Id` (the location) |
| `NetworkShareName` | string | Share (server-level = `null`) |
| `NetworkPath` | string | Server-relative path inside the share; `null` at share root / for action rows |

Rules:
- `FullPath = null` for network entries — keeps `Path.Combine`,
  `FileOperations`, `IsSameVolume` and the `..`/drive logic untouched (same
  deal as portal: `FileEntry.cs:32-40`).
- Drill-out works off the navigator's own network stack (share → path → …),
  mirroring how portal columns manage drill-out without a `..` filesystem row.
- Action rows are network entries with `ActionKind != None`, `NetworkPath = null`.

## Data layer

### `NetworkServerConfig` (pure, unit-testable)

```csharp
public enum NetworkProtocol { Smb = 0 }

public sealed class NetworkServerConfig
{
    public NetworkProtocol Protocol { get; set; }   // = Smb
    public string DisplayName { get; set; }          // optional
    public string Host { get; set; }                 // required
    public int Port { get; set; }                    // 0 → protocol default (445)
    public string Username { get; set; }
    public string Share { get; set; }                // optional
    // NOT stored: Password (PasswordVault)
}
```

Pure helpers (linkable into `tests/`, no UWP types):

- `NetworkUrl.Compose(config)` → `"smb://alice@192.168.1.50/music"` (username
  and share omitted when empty; host normalized to lower-case).
- `NetworkUrl.ParseCanonical(url)` → config (for dedup / vault key recovery).
- `NetworkUrl.VaultResource(config)` → composed address (the PasswordVault
  resource key).
- `NetworkServerManager.DefaultPort(protocol)` → 445.

### SQLite table — `NetworkServerEntry`

Table created in `metadata.db` (existing `SQLiteAsyncConnection`), via the
existing migration framework:

- Bump `MetadataCache.CurrentSchemaVersion` 2 → 3
  (`MetadataCache.cs:16`); in `RunMigrationsAsync` (`MetadataCache.cs:46`)
  add `await db.CreateTableAsync<NetworkServerEntry>();` under `fromVersion < 3`.
- Class lives with the other entry types (`MetadataCacheDb.cs`) or in
  `XFiles/Network/NetworkServerEntry.cs` (same assembly — either is fine; keep
  schema classes together in `MetadataCacheDb.cs` for consistency).

```csharp
[Table("NetworkServerEntry")]
public class NetworkServerEntry
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public int Protocol { get; set; }               // NetworkProtocol
    public string DisplayName { get; set; }
    public string Host { get; set; }
    public int Port { get; set; }                   // 0 = default
    public string Username { get; set; }
    public string Share { get; set; }
    [Indexed(Unique = true)] public string CanonicalUrl { get; set; } // dedup
}
```

Safety: `MetadataCache.ClearCacheAsync` deletes only `MetadataCacheEntry` +
`CoverArtEntry` (`MetadataCache.cs:348-349`) — config tables survive.

### `NetworkServerManager`

Static, `FavoritesManager`-style API (`XFiles/FileSystem/FavoritesManager.cs`):

- `GetAllAsync()` → `List<NetworkServerConfig>` (sorted by display name).
- `AddAsync(config, password)` — insert row (dedup on `CanonicalUrl`),
  `PasswordVault.Add(new PasswordCredential(resource, username, password))`.
- `UpdateAsync(id, config, password?)` — update row; re-key vault entry if the
  canonical URL or username changed (remove old, add new).
- `RemoveAsync(id)` — delete row + `PasswordVault.Remove(vaultEntry)`; callers
  disconnect the session.
- `GetPasswordAsync(config)` → `PasswordVault.RetrieveAll()` lookup by
  resource+username.

Implementation note: `metadata.db` is a plain file path; open a
`SQLiteAsyncConnection` the same lazy way `MetadataCache` does. If a second
connection to the same file is undesirable, expose
`MetadataCache.GetConnectionAsync()` and reuse it — decide during M1.

## SMB core (`XFiles/Network/`)

### `INetworkFileSystemProvider`

Protocol-agnostic contract; SMB implements it now, FTP/FTPS (M9), SFTP
(M10), and WebDAV (M14) plug in behind it. Started read-only (M2), extended with the write ops
that landed on `SmbBrowser` during M5.5 (M8) — callers use the interface, not
the concrete browser:

```csharp
public interface INetworkFileSystemProvider
{
    NetworkProtocol Protocol { get; }
    Task<IReadOnlyList<NetworkServerConfig>> ListSharesAsync(
        NetworkServerConfig loc, CancellationToken ct);
    Task<IReadOnlyList<NetworkFileEntry>> ListDirectoryAsync(
        NetworkServerConfig loc, string share, string remotePath, CancellationToken ct);
    Task<Stream> OpenReadAsync(
        NetworkServerConfig loc, string share, string remotePath, CancellationToken ct);
    Task<long> GetFileLengthAsync(
        NetworkServerConfig loc, string share, string remotePath, CancellationToken ct);
    // write ops (M5.5-era SmbBrowser methods, promoted to the interface in M8)
    Task<bool> EntryExistsAsync(NetworkServerConfig loc, string share, string remotePath, CancellationToken ct);
    Task<Stream> OpenWriteStreamAsync(NetworkServerConfig loc, string share, string remotePath, CancellationToken ct);
    Task WriteFileAsync(NetworkServerConfig loc, string share, string remotePath, byte[] data, CancellationToken ct);
    Task DeleteFileAsync(NetworkServerConfig loc, string share, string remotePath, CancellationToken ct);
    Task DeleteDirectoryAsync(NetworkServerConfig loc, string share, string remotePath, CancellationToken ct);
    Task RenameFileAsync(NetworkServerConfig loc, string share, string remotePath, string newName, CancellationToken ct);
    Task CreateDirectoryAsync(NetworkServerConfig loc, string share, string remotePath, CancellationToken ct);
    void Disconnect(NetworkServerConfig loc);
}
```

- `share` is SMB-shaped: for FTP/SFTP/WebDAV it is empty (`""`) and `remotePath` is
  absolute from the server root (`/music/track.mp3`). `ListSharesAsync`
  returns an empty list for FTP/SFTP/WebDAV (no share layer).
- `NetworkProviderFactory.Create(config)` → the browser for
  `config.Protocol` (SMB/FTP/SFTP/WebDAV). Callers that hold a browser for the
  active location resolve it once via the factory.

`NetworkFileEntry` = `{ Name, IsDirectory, Size, LastWriteTime }` (minimal;
maps to `FileEntry` by each browser).

### `SmbSession` — connection pool

- Keyed by canonical URL; lazy `Connect` + `Login` + `TreeConnect`.
- `SMB2Client.Connect(host, SMBTransportType.DirectTCPTransport)` (port 445)
  → `Login(domain, username, password)` (empty strings for anonymous; null
  password = anonymous) → `TreeConnect(share)` → `ISMBFileStore`.
- **Timeouts**: socket connect + login wrapped in `Task.WhenAny` with an
  explicit timeout (default ~10 s, configurable); every op takes a
  `CancellationToken`. This is non-negotiable (Xbox D-pad freeze precedent in
  `docs/DECISIONS.md` / `docs/tech-debts/`).
- Never hold the session from the UI thread — all work inside `Task.Run`.
- `Disconnect` closes `SMB2Client` (frees the `ISMBFileStore`).

### `SmbBrowser` — listing

- `ListSharesAsync`: `client.ListShares(out status)` → map each share to a
  `NetworkServerConfig` clone with `Share` filled (drill target).
- `ListDirectoryAsync`: `fileStore.CreateFile(remotePath + @"\*",
  CreateDisposition.FILE_OPEN, FILE_DIRECTORY_FILE | FILE_NON_DIRECTORY_FILE)` +
  `QueryDirectory(...)` / enumerate — map entries to `NetworkFileEntry`,
  dirs and files, sorted like `DirectoryScanner` (dirs first, ordinal-ignore-case).
- Server-level failures (`NTStatus` access denied / bad network path) surface
  as friendly errors, never raw codes.

### `SmbReadStream : Stream` — byte access

- Wraps `ISMBFileStore.ReadFile(handle, offset, maxReadSize)` with
  `MaxReadSize` chunking (SMBLibrary reads are bounded; pick a safe chunk, e.g.
  64 KB, verify against the lib's documented max).
- `Seek`/`Position` map to read offsets — this makes it a **seekable** stream,
  which lets it be wrapped via
  `System.Runtime.InteropServices.WindowsRuntime.AsRandomAccessStream()` for
  `BitmapImage.SetSourceAsync`, `PdfDocument.LoadFromStreamAsync`, and
  `MediaPlayer.SetSource`.
- Opens via `CreateFile` with `FILE_READ_DATA`; `CloseFile` on dispose.

## Navigation wiring

### Root entry injection

`ColumnNavigator` root build (the favorites-injection method) gains a
"Network" virtual entry (name, network icon, `IsVirtual`), placed after
Favorites / User Folders, before drives. Reuse the existing
`AddRootVirtualEntriesAsync`-style hook; do NOT touch `..` handling.

### Drill-in state machine

`Network` (root) → **locations + action rows** (from `NetworkServerManager`) →
(confirm location) → **shares** (`SmbBrowser.ListSharesAsync`) → (confirm
share) → **remote tree** (`ListDirectoryAsync`) → normal drill/preview.

- Drill state lives on the navigator (like the portal stack): current
  location id + share + remote path.
- Drill-out pops the stack (no `..` filesystem row needed); confirm on a
  directory advances, confirm on a file previews.
- Action rows: `ActionKind.AddLocation` → open `NetworkLocationDialog`;
  `ActionKind.DownloadUrl` → download flow (below).
- Loading state + timeout toast while connecting; on failure return to the
  location list with an error.

### Preview dispatch

`MillerColumnsPage.Preview` / `MediaPreviewControl` gain an `IsNetwork`
branch (alongside `IsPortal`), routing by `NetworkPath` → provider:

| Type | Route |
|---|---|
| Text | `provider.OpenReadAsync` → text renderer (existing caps) |
| Image | stream → `BitmapImage.SetSourceAsync` |
| PDF | stream → `PdfDocument.LoadFromStreamAsync` |
| ROM | first bytes → `RomHeaderParser` |
| Audio | growing-file (below) |
| Video | `MediaPlayer.SetSource(AsRandomAccessStream(readStream))` |
| Chiptune | full bytes → `RetroAudioPlayer` (below) |

## Media

### Audio — growing-file streaming (chiptune precedent)

`AudioLevelService.SwapSourceAsync(path, forceStream: true)`
(`AudioLevelService.cs:587`) already streams from a growing file via
`MediaSourceAudioInputNode` with `FileShare.Read | Write | Delete`
(`AudioLevelService.cs:687`, and `:382` in the fallback path). Reuse it:

```
SmbReadStream (remote, sequential)  ──►  temp file  (LocalState\tmp\net-<id>.<ext>)
                                          • FileStream(Create, ReadWrite, FileShare.Read)
                                          • producer writes sequentially (copy from read stream)
                                          • header arrives first → container valid immediately
AudioGraph  ◄── SwapSourceAsync(tmpPath, forceStream: true)
                                          • consumer reads while producer writes
                                          • start playback once ≥ ~256 KB buffered (tunable)
```

- Format-agnostic: bytes are copied verbatim (MP3/FLAC/OGG/WAV/m4a all fine);
  no header patching needed (unlike chiptune WAV rendering, which must
  pre-declare size).
- `GetFileLengthAsync` gives progress: total bytes known up front → progress
  bar / "streaming" indicator.
- Temp lifecycle: delete on playback stop/navigate-away; M6 decides whether a
  bounded cache is worth it on Xbox (disk budget).
- Seek: clamp to the downloaded region while growing; full seek after the file
  completes.

### Chiptune (remote)

`RetroAudioPlayer` `RA_Open` accepts an in-memory buffer
(`RA_Open(data, size, ext, baseDir, outHandle)` — see `docs/ARCHIVES.md` /
memory #5). Remote chiptunes are small files: read full bytes via the
provider, then run the existing render path (sibling `.psflib`/`.usflib` not
supported from network — same limitation as inside archives, memory #14).

### Video

Primary: `MediaPlayer.SetSource` on the seekable `SmbReadStream`. If M6 shows
Xbox/socket streaming problems (stalls, seek corruption), fall back to the
same growing-file producer + `MediaPlayer` on the local temp file.

## Download from URL flow

1. Action row → `FolderBrowserDialog.ShowAsync(null, PickerMode.Folder, null,
   confirmLabel: "Download Here")` (new optional param).
2. Result path → `InputDialog` URL prompt.
3. `DownloadService.ResolveAsync(url)` → direct link or `UrlDownloadOverlay`
   WebView fallback (`MillerColumnsPage.FileOps.cs:2427-2480` flow, unchanged).
4. `DownloadService.TryDownloadAsync(url, destDir, …)` with existing progress.

`FolderBrowserDialog` changes (`Controls/FolderBrowserDialog.xaml.cs`):
- `ShowAsync(initialPath, mode, fileExtensions = null, string confirmLabel = null)`.
- `null` keeps today's exact "Move Here" behavior; non-null replaces the
  virtual-row name + footer label + icon
  (`FolderBrowserDialog.xaml.cs:84-99`, `:85-87`, `:230-244`).
- Existing callers untouched (backwards compatible by default).

`FileActionSheet`: remove both "Download from URL" actions
(`FileActionSheet.xaml.cs:373`, `:475`) and their `FileAction` enum value.

## Error handling & threading

- Every provider method: `CancellationToken` + explicit timeout. Network
  errors → `NetworkOperationException { Reason }` enum
  (`TimedOut`, `AccessDenied`, `Unreachable`, `AuthFailed`, `Cancelled`),
  translated to toasts.
- No socket work on the UI thread; results marshalled back via `await`.
- Xbox: UI tick floors at ~50 ms — never rely on tick-based socket progress;
  use `Task`/events.
- All failures logged (`Log.Warn`/`Log.Err` with `class.method:` prefix);
  connection lifecycle logged (`Log.Info` connect/disconnect).

## Manifest

No changes. Verified: `internetClient`, `internetClientServer`,
`privateNetworkClientServer` already declared
(`XFiles/Package.appxmanifest:56-62`). `enterpriseAuthentication` only if a
Kerberos/domain SMB case materializes (not now).

## Test strategy

- **Unit (desktop, net8.0)**: `NetworkUrl` compose/parse; `NetworkServerEntry`
  mapping; `NetworkServerManager` CRUD against in-memory SQLite
  (`SQLitePCLRaw.bundle_green` is netstandard2.0 — linkable). Pure helpers
  only (memory #2).
- **Desktop manual**: real SMB share from the running UWP app.
- **Hardware (Xbox)**: port 445 + end-to-end against the same share; results
  recorded in `IMPLEMENTATION.md` M6.

## csproj / build notes

- Add every new `.cs` under `XFiles/Network/` to `XFiles.csproj` (explicit
  item lists — memory #167).
- Add `TalAloni.SMBLibrary` NuGet reference (netstandard2.0).
- Build verification: VS2026 MSBuild command in `../../AGENTS.md`.
