---
layout: default
title: Text Editor — Encoding Detection and Handling
---
# Text Editor — Encoding Detection and Handling

## Overview

Text files come in many encodings. The editor must detect the encoding on load and
handle it correctly, while always saving in UTF-8 (the modern default).

## Detection Algorithm

On file load, encoding is detected in this priority order:

### 1. BOM (Byte Order Mark)

If the first bytes of the file match a known BOM, use the corresponding encoding:

| BOM Bytes | Encoding |
|---|---|
| `EF BB BF` | UTF-8 |
| `FF FE` | UTF-16 Little Endian |
| `FE FF` | UTF-16 Big Endian |
| `FF FE 00 00` | UTF-32 Little Endian |
| `00 00 FE FF` | UTF-32 Big Endian |

UTF-16 and UTF-32 files are decoded using their respective encodings, then
re-encoded as UTF-8 for the editor buffer. The original encoding is noted but
not preserved on save (MVP always saves UTF-8).

### 2. Null Byte Heuristic

If no BOM is found, scan the first 512 bytes:

- If any `00` byte is found → likely UTF-16 (ASCII subset has no null bytes).
  Try UTF-16 LE decode. If it produces valid text, use it.
- If no null bytes → proceed to UTF-8 validation.

### 3. UTF-8 Validation

Attempt to decode the file as UTF-8. UTF-8 has a strict structure:
- Single-byte chars: `0xxxxxxx`
- Multi-byte sequences: `110xxxxx 10xxxxxx`, `1110xxxx 10xxxxxx 10xxxxxx`, etc.

If any byte sequence violates UTF-8 structure → not valid UTF-8.

### 4. Fallback: Windows-1252

If UTF-8 validation fails and no BOM/null bytes suggest UTF-16, assume
**Windows-1252** (superset of ISO-8859-1, covers most Western European languages).
This is the same fallback used by modern browsers and text editors.

Windows-1252 can decode any byte sequence (every byte maps to a character),
so this always succeeds.

### 5. Binary Detection

Before encoding detection, check if the file is likely binary:

- If any null byte (`0x00`) is found in the first 8KB → treat as binary
- Binary files cannot be edited → open read-only with message:
  "This file appears to be binary and cannot be edited."

**Exception**: UTF-16 files (detected by BOM or null-byte pattern) are not binary.
Only non-BOM, non-UTF-16 files with null bytes are treated as binary.

## Detection Flow

```
Read first 8KB of file
  │
  ├─ has BOM? → use BOM encoding
  │
  ├─ null bytes in first 512B?
  │   ├─ try UTF-16 LE decode → valid? → use UTF-16 LE
  │   └─ not valid → binary detected → read-only
  │
  ├─ no null bytes → try UTF-8 decode
  │   ├─ valid UTF-8 → use UTF-8
  │   └─ invalid → fallback to Windows-1252
  │
  └─ store: detectedEncoding (for display in status bar)
```

## Saving

MVP always saves as **UTF-8 with BOM**:

1. Write BOM bytes: `EF BB BF`
2. Encode text content as UTF-8
3. Write encoded bytes via `CreateFile2FromAppW` + `WriteFile`

**Why UTF-8 with BOM?**
- Windows Notepad and many Windows tools expect BOM to identify UTF-8
- Avoids ambiguity with ASCII/Windows-1252 on re-open
- Consistent behavior regardless of original encoding

**Post-MVP**: "Save As Encoding" option to preserve original encoding or choose
a different one.

## Line Endings

### Detection

On load, detect the dominant line ending style:

- Count `\r\n` (CRLF), `\n` (LF), and `\r` (CR) occurrences
- Use the most common style for display
- Preserve the original style on save

### Editor Behavior

The contentEditable div normalizes line endings to `\n` internally (HTML behavior).
On save, convert back to the detected line ending style:

```csharp
if (originalLineEnding == "\r\n")
    content = content.Replace("\n", "\r\n");
else if (originalLineEnding == "\r")
    content = content.Replace("\n", "\r");
```

### Edge Cases

- **Mixed line endings**: use the most common style, log a warning
- **Unix-only files (LF)**: preserve as LF on save
- **Old Mac files (CR)**: rare, but detected and preserved

## Status Bar Information

When the editor is open, the footer or a status indicator shows:

```
UTF-8  |  Ln 42, Col 15  |  1,234 lines  |  45.2 KB
```

Components:
- Encoding: detected encoding name (UTF-8, UTF-16 LE, Windows-1252, etc.)
- Cursor: line number, column number
- Document: total lines
- File size: human-readable size

This information is read from the JS editor object via `InvokeScriptAsync`.

## Known Limitations

- **UTF-16 on save**: MVP always saves UTF-8. Original UTF-16 encoding is lost
  after save. Documented as known behavior, not a bug.
- **Very long lines**: lines > 10,000 characters may cause performance issues
  in the contentEditable div. Not limited explicitly, but documented.
- **Null bytes in text**: files with intentional null bytes (some config formats)
  are treated as binary. Acceptable trade-off for the 99% case.
