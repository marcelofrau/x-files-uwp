# WebDAV Implementation Plan

WebDAV support for X-Files network browsing. HTTP-based protocol — the simplest
of the four providers. No session pooling, no custom TLS handshake, no data
connection lifecycle. `HttpClient` + `System.Xml.Linq` handle everything.

## Scope

| Feature | Supported | Notes |
|---------|-----------|-------|
| Browse directories | Yes | PROPFIND Depth:1 |
| Audio streaming | Yes | Sequential read via WebDavReadStream |
| Video streaming | Yes | Seek via HTTP Range headers (unlike FTP) |
| Text preview/edit | Yes | GET → cache → edit → PUT back |
| Image/SVG/PDF/ROM preview | Yes | Partial read via Range |
| Copy/paste (local↔remote) | Yes | GET streaming + PUT |
| Batch delete/mkdir/rename | Yes | DELETE/MKCOL/MOVE |
| Text editing save-back | Yes | PUT with StreamContent |

## Dependency

None. Uses `System.Net.Http.HttpClient` (already in 8+ files) and
`System.Xml.Linq` (already used in WebDavSmokeTests.cs).

## Estimated effort

~750-900 new lines across 3 files + ~15 lines of modifications to existing files.

---

## Milestone 1 — Core provider (WebDavSession + WebDavBrowser)

### New files

**`XFiles/Network/WebDavSession.cs`** (~400 lines)

HTTP engine. Stateless — no session pool, `HttpClient` connection pooling is
built-in. Constructor takes `NetworkServerConfig` + password. All methods are
async, throw `NetworkOperationException` on failure.

Key methods and their HTTP mapping:

| Method | HTTP | Notes |
|--------|------|-------|
| `TestConnectionAsync` | PROPFIND / (Depth:0) | Parse response, count items |
| `ListDirectoryAsync` | PROPFIND /path (Depth:1) | Parse DAV: XML → `List<NetworkFileEntry>` |
| `OpenReadAsync` | GET /path | Returns `WebDavReadStream` with size from PROPFIND cache |
| `GetFileLengthAsync` | HEAD /path | Content-Length header |
| `EntryExistsAsync` | HEAD /path | 200 = exists, 404 = not |
| `WriteFileAsync` | PUT /path | StreamContent from local file |
| `OpenWriteStreamAsync` | PUT /path | Returns upload stream wrapper |
| `DeleteFileAsync` | DELETE /path | — |
| `DeleteDirectoryAsync` | DELETE /path | Recursive; fallback PROPFIND+iterate if 409 |
| `RenameFileAsync` | MOVE /path | Header `Destination: /newpath` |
| `CreateDirectoryAsync` | MKCOL /path | 405 = already exists (ignore) |

PROPFIND XML parsing with `System.Xml.Linq`:
- Namespace: `DAV:` (XNamespace D = "DAV:")
- Each `<D:response>` contains `<D:href>`, `<D:propstat>/<D:prop>` with:
  - `<D:resourcetype>/<D:collection/>` (directory indicator)
  - `<D:getcontentlength>` (file size)
  - `<D:getlastmodified>` (modified date)
- Entries with `<D:collection>` are directories; without are files
- Parse `<D:href>` to extract filename (last segment of URL path)

EffectivePath pattern (same as SftpBrowser):
```csharp
private static string EffectivePath(string share, string path)
{
    return string.IsNullOrEmpty(path) ? (share ?? "") : path;
}
```

Custom HTTP methods:
```csharp
private static readonly HttpMethod PROPFIND = new HttpMethod("PROPFIND");
private static readonly HttpMethod MKCOL = new HttpMethod("MKCOL");
```

Authentication: `HttpClientHandler` with `NetworkCredential(username, password)`.

TLS self-signed: `HttpClientHandler.ServerCertificateCustomValidationCallback =
    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;`

Logging: structured templates following the pattern from FtpBrowser/SftpBrowser:
```
Log.Info("WebDavSession.ListDirectory: {Url}{Path}", baseUrl, remote);
Log.Dbg("WebDavSession.ListDirectory: {Count} entries", entries.Count);
```

Error mapping:
- 401 → `NetworkOperationReason.AuthFailed`
- 403 → `NetworkOperationReason.AccessDenied`
- 404 → `NetworkOperationReason.FileNotFound`
- 5xx → `NetworkOperationReason.Unreachable`
- Timeout/cancellation → `NetworkOperationReason.TimedOut`

**`XFiles/Network/WebDavBrowser.cs`** (~250 lines)

Facade following SmbBrowser/SftpBrowser pattern exactly:
- `Protocol => NetworkProtocol.Webdav`
- Password from vault via `NetworkServerManager.GetPasswordAsync(config)`
- Delegate each method to `WebDavSession`
- Structured logging at facade level (Log.Info for entry, Log.Dbg for results)
- `ListSharesAsync` → returns empty list (WebDAV has no share layer)
- `Disconnect` → no-op (stateless, no sessions to release)

**`XFiles/Network/WebDavReadStream.cs`** (~180 lines)

Seekable read stream over HTTP GET with Range headers.

Design:
- Constructor: `(string url, HttpClient client, long totalSize)`
- Properties: `Length = totalSize`, `Position` tracked internally
- `Seek(offset, SeekOrigin)`: record target position, close current response if needed
- `Read(byte[], int, int)`: 
  - If no active response or position past response end → new GET with `Range: bytes=position-`
  - Read from current response stream, advance position
- `Dispose`: close active HTTP response

Key difference from FtpReadStream: no data connection management, no EPSV
quirk. HTTP Range is a standard feature. Connection pooling via `HttpClient`
keeps TCP+TLS alive across seek/reopen cycles.

---

## Milestone 2 — Registration and URL wiring

### Modifications to existing files

**`XFiles/Network/NetworkProviderFactory.cs:36`**

Replace:
```csharp
// WebDAV stays on the roadmap (M13+).
```
With:
```csharp
case NetworkProtocol.Webdav:
    provider = new WebDavBrowser();
    break;
```

**`XFiles/Network/NetworkUrl.cs`**

In `DefaultPort()` (line 14-28), add:
```csharp
case NetworkProtocol.Webdav:
    return 80;
```

In `ProtocolFromScheme()` (line 31-46), add:
```csharp
case "webdav":
    return NetworkProtocol.Webdav;
```

**`XFiles/XFiles.csproj`**

Add 3 manual `<Compile>` entries for the new files:
```xml
<Compile Include="Network\WebDavSession.cs" />
<Compile Include="Network\WebDavBrowser.cs" />
<Compile Include="Network\WebDavReadStream.cs" />
```

---

## Milestone 3 — UI integration

### Modifications to existing files

**`XFiles/Controls/NetworkLocationDialog.xaml:103`**

Add after the SFTP ComboBoxItem:
```xml
<ComboBoxItem Content="WebDAV" Tag="Webdav" />
```

**`XFiles/Controls/MillerColumnsPage.Preview.cs:597-606`**

In `ProtocolLabel()`, add:
```csharp
case NetworkProtocol.Webdav: return "WebDAV";
```

---

## Milestone 4 — Video streaming (no gate needed)

WebDAV video streaming works via HTTP Range headers in `WebDavReadStream`.
Unlike FTP (which requires re-opening a data connection per seek), HTTP seeks
are handled within the existing `HttpClient` connection pool.

**No changes needed** in:
- `MillerColumnsPage.Navigation.cs` — `IsFtpProtocol()` gate is FTP-only, WebDAV passes
- `MillerColumnsPage.Preview.cs` — `PlayRemoteVideoInlineAsync()` works with any RemoteStream

The `RemoteStream(webDavReadStream, reopen)` → `MediaSource.CreateFromStream`
pipeline works because:
1. `WebDavReadStream.Length` returns size from PROPFIND (no `.Length` issue)
2. `WebDavReadStream.Seek()` issues Range header (no data connection reopen)
3. `CloneStream()` creates new GET request (no lock contention)

---

## Milestone 5 — Unit tests

### Expand existing tests

**`tests/WebDavSmokeTests.cs`** — add tests:
- `RealWebDav_Propfind_ParseEntries` — verify PROPFIND XML parses to correct
  NetworkFileEntry fields (name, size, isDirectory, lastWriteTime)
- `RealWebDav_Mkcol_CreateAndDelete` — create directory, verify exists, delete
- `RealWebDav_Move_Rename` — create file, rename via MOVE, verify old name gone

**New test file** `tests/WebDavSessionTests.cs` (~100 lines):
- Unit tests for PROPFIND XML parsing logic (extracted into a pure helper)
- Unit tests for EffectivePath
- Unit tests for URL scheme/port mapping

---

## Milestone 6 — Documentation

**`docs/network-files/WEBDAV.md`** — protocol overview:
- WebDAV = HTTP with extensions (PROPFIND, MKCOL, MOVE)
- No share layer (paths are absolute from server root)
- Streaming via HTTP Range headers
- Authentication: Basic (most servers) or Digest
- Self-signed TLS support

**Update `docs/network-files/README.md`** — add WebDAV to the supported protocols table.

---

## Milestone 7 — Smoke test against docker

Run existing `WebDavSmokeTests` against `maltokyo/docker-nginx-webdav`:
```
X_FILES_WEBDAV_HOST=127.0.0.1 X_FILES_WEBDAV_PORT=8081 \
  dotnet test --filter "FullyQualifiedName~WebDavSmokeTests"
```

Validate:
1. PROPFIND listing (seed folders + files)
2. GET read (download seed.txt, verify content)
3. PUT write (upload temp file, verify via GET)
4. DELETE (remove temp file, verify via HEAD 404)
5. MKCOL (create temp dir, verify via PROPFIND)

---

## Milestone 8 — Xbox validation

Deploy to Xbox and validate against docker:
1. Add WebDAV location in NetworkLocationDialog
2. Browse root directory (PROPFIND listing)
3. Navigate into subdirectories
4. Preview image (seed.png)
5. Preview text (seed.txt)
6. Play audio (seed.wav) — inline
7. Play video (unix.mp4) — inline and fullscreen
8. Y-menu Edit text file → save back
9. Y-menu Copy local → Paste to WebDAV
10. Y-menu Copy from WebDAV → Paste local
11. Batch delete multiple files
12. Create folder (MKCOL)

---

## Risks

| Risk | Impact | Mitigation |
|------|--------|------------|
| PROPFIND XML varies across servers | Medium | Test with nginx (smoke), Nextcloud, IIS; parse only DAV: namespace basics |
| MKCOL on existing returns 405 | Low | Handle 405 as "already exists" |
| No LOCK/UNLOCK | Low | Most read/write servers accept without locks; add later if needed |
| Range header not supported | Low | Fallback: full GET + in-memory skip (rare) |
| WebDAV image 500 on PUT to :ro mount | None | Test infra only — real servers handle PUT fine |
