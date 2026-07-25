# File Sharing via QR Code — Future Feature

## Concept
Allow users to share any file from the file browser by uploading it to a temporary
file hosting service and displaying a QR code + URL in a dialog. The user can then
scan the QR code with their phone to download the file.

## User Flow
1. User navigates to a file in the file browser
2. Opens FileActionSheet (Y button) → selects "Share File"
3. App uploads file to hosting service
4. ShareDialog opens with QR code + download URL
5. User scans QR code with phone → downloads file
6. File expires after a configured TTL (hours/days)

## Candidate Services (tested 2026-07-25)

| Service | Status | Max Size | TTL | API |
|---|---|---|---|---|
| **litterbox.catbox.moe** | **WORKING** | 1GB | 72h | Simple multipart POST → plain URL |
| **gofile.io** | **WORKING** | Unlimited | Permanent | Multipart POST → JSON (needs server fetch first) |
| **file.io** | Broken (301 redirect) | 4GB | 14 days | — |
| **0x0.st** | **OFFLINE** (AI spam) | 512MB | 14-90 days | — |
| **catbox.moe** | Paused (storage issues) | 200MB | Permanent | — |
| **transfer.sh** | Unreliable | 2GB | 14 days | — |

### Recommended: litterbox.catbox.moe
- Dead simple API: `POST` with `reqtype=fileupload`, `time=72h`, `fileToUpload=@file`
- Returns plain URL directly (no JSON parsing needed)
- 1GB limit — more than enough for logs and most files
- 72h TTL by default (configurable: 1h, 12h, 24h, 72h)

### Backup: gofile.io
- More complex (need to fetch server list first)
- Returns JSON with `downloadPage` URL + guest token
- No size limit, permanent storage

## Implementation Notes

### Upload (C#) — litterbox.catbox.moe
```csharp
using (var client = new HttpClient())
using (var form = new MultipartFormDataContent())
{
    var fileBytes = await File.ReadAllBytesAsync(filePath);
    var fileContent = new ByteArrayContent(fileBytes);
    fileContent.Headers.Add("Content-Type", "text/plain");
    form.Add(new StringContent("fileupload"), "reqtype");
    form.Add(new StringContent("72h"), "time");
    form.Add(fileContent, "fileToUpload", Path.GetFileName(filePath));
    var resp = await client.PostAsync("https://litterbox.catbox.moe/resources/internals/api.php", form);
    string url = await resp.Content.ReadAsStringAsync(); // plain URL
}
```

### QR Code Generation
Already implemented in `ShareDialog.xaml.cs` using ZXing.Net `BarcodeWriterGeneric`.
Same component can be reused — just pass the download URL.

### Xbox/UWP Considerations
- **Network capability**: `internetClient` manifest capability required (already present)
- **File size limits**: Litterbox supports up to 1GB
- **File access**: Must use `StorageFile` APIs or P/Invoke to read file bytes
- **Background upload**: For large files, consider `BackgroundDownloader` for upload resilience

## Integration Points
- `FileActionSheet.xaml.cs` → add "Share File" action
- `ShareDialog.xaml.cs` → reuse for QR display (already has QR + clipboard + close)
- `FileOperations.cs` → potential home for `ShareFileAsync()` method

## Open Questions
- Max file size limit for share feature? (suggest 500MB as safe default)
- Expiry policy? (72h default via litterbox)
- Progress indicator for large uploads?
- Should shared files be virus-scanned before upload? (out of scope for MVP)
