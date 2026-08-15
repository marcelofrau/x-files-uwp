---
layout: default
title: File Sharing via QR Code — Shipped (gofile.io)
---
# File Sharing via QR Code — Shipped (gofile.io)

> **Status: shipped** (v1.2.0). `FileShareService` uploads to **gofile.io** and
> `ShareDialog` shows a QR code + download URL. See `XFiles/Services/FileShareService.cs`.

## Concept

Share any file from the file browser by uploading it to a temporary file hosting
service and displaying a QR code + URL in a dialog. The user scans the QR code with
their phone to download the file.

## User Flow

1. User navigates to a file in the file browser
2. Opens `FileActionSheet` (Y button) → selects "Share"
3. `FileShareService` uploads file to gofile.io (progress reported)
4. `ShareDialog` opens with QR code + download URL + clipboard copy
5. User scans QR code with phone → downloads file
6. gofile.io keeps the file indefinitely (no TTL; can be removed from the site)

## Chosen Service: gofile.io

| Service | Status | Max Size | TTL | API |
|---|---|---|---|---|
| **gofile.io** | **SHIPPED** | Unlimited | Permanent | Multipart POST → JSON (server fetch + guest token) |
| litterbox.catbox.moe | Rejected after evaluation | 1GB | 72h | Simple multipart POST → plain URL |
| file.io | Broken (301 redirect) | 4GB | 14 days | — |
| 0x0.st | OFFLINE (AI spam) | 512MB | 14–90 days | — |
| catbox.moe | Paused (storage issues) | 200MB | Permanent | — |
| transfer.sh | Unreliable | 2GB | 14 days | — |

gofile.io was chosen over the simpler litterbox API because it offers unlimited size
and permanent storage (litterbox caps at 1GB with a 72h TTL). Trade-off: two-step
upload (fetch server list → upload with guest token).

## Implementation

### Upload (C#) — `FileShareService.cs`

1. `GET https://api.gofile.io/getServer` → JSON `{ data: { server: "storeX" } }`
2. `POST https://{server}.gofile.io/contents/uploadfile` multipart with
   `file` + `token` (guest token from the same response)
3. Response JSON contains `data.downloadPage` (the share URL)

File is read via P/Invoke stream (`Win32FileStream`) — same path coverage as the rest
of the app (external USB drives on Xbox).

### QR Code Generation

`ShareDialog.xaml.cs` renders the URL as QR via **ZXing.Net** `BarcodeWriterGeneric`.
Dialog also offers clipboard copy + close. Reused for any URL-sharing surface.

### Xbox/UWP Considerations

- **Network capability**: `internetClient` manifest capability already present.
- **File access**: P/Invoke read (no `StorageFile` limitation).
- **Upload progress**: `OperationProgress`-style reporting into `ShareDialog`;
  network errors surface in the dialog with retry.
- **Backend note**: gofile.io may change API shape — the service class isolates it
  in one file for easy migration (litterbox is the documented fallback API).

## Integration Points

- `FileActionSheet.xaml.cs` → "Share" action (Y menu) ✓
- `ShareDialog.xaml`/`.cs` → QR + clipboard + close ✓
- `FileShareService.cs` → upload + share-URL generation ✓

## Out of Scope

- Expiry policy for uploads (gofile.io has none by design).
- Virus scanning before upload.
- Background upload for multi-GB files.
