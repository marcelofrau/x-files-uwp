# ROM Format Headers

Parses ROM file headers to extract game title and system name for the preview
pane. Works for both raw ROM files and ROMs inside ZIP archives (No-Intro format).
38 extensions across 25+ systems.

## Supported Systems

| Extension | System | Header Location | Title Size | Encoding |
|-----------|--------|----------------|------------|----------|
| `.nes` | NES (iNES) | 0x010 | 16 bytes | ASCII |
| `.sfc` | SNES | 0x7FC0 (LoROM) or 0xFFC0 (HiROM) | 21 bytes | ASCII |
| `.gb` | Game Boy | 0x134 | 11 bytes | ASCII |
| `.gbc` | Game Boy Color | 0x134 | 11 bytes | ASCII |
| `.gba` | Game Boy Advance | 0xA0 | 12 bytes | ASCII |
| `.gen` / `.md` | Genesis / Mega Drive | 0x120 | 48 bytes (24 chars) | 16-bit big-endian |
| `.sms` | Master System | 0x7FF0 | 32 bytes | ASCII |
| `.gg` | Game Gear | 0x7FF0 | 32 bytes | ASCII |
| `.pce` / `.tg16` | PC Engine / TurboGrafx-16 | 0x120 | 32 bytes | ASCII |
| `.a26` | Atari 2600 | — | — | filename fallback |
| `.a52` | Atari 5200 | — | — | filename fallback |
| `.a78` | Atari 7800 | — | — | filename fallback |
| `.j64` / `.jag` | Atari Jaguar | — | — | filename fallback |
| `.lnx` | Atari Lynx | — | — | filename fallback |
| `.col` | ColecoVision | — | — | filename fallback |
| `.int` | Intellivision | — | — | filename fallback |
| `.sg` | SG-1000 | — | — | filename fallback |
| `.msx` | MSX | — | — | filename fallback |
| `.sna` / `.z80` | ZX Spectrum | — | — | filename fallback |
| `.vec` | Vectrex | — | — | filename fallback |
| `.n64` / `.z64` / `.v64` | Nintendo 64 | — | — | filename fallback |
| `.nds` | Nintendo DS | — | — | filename fallback |
| `.3ds` | Nintendo 3DS | — | — | filename fallback |
| `.vb` | Virtual Boy | — | — | filename fallback |
| `.ngp` / `.ngc` | Neo Geo Pocket | — | — | filename fallback |
| `.ws` / `.wsc` | WonderSwan | — | — | filename fallback |
| `.gcm` | GameCube | — | — | filename fallback |
| `.gdi` / `.cdi` / `.chd` | Dreamcast / disc images | — | — | filename fallback |

Systems with reliable header signatures (`NES`, `SNES`, `GB/GBC`, `GBA`,
`Genesis/MD`, `SMS`, `GG`, `PCE/TG16`) get a real parsed title. The rest are
recognized by extension and fall back to the filename (see `RomHeaderParser.cs`).

## Detection Details

### NES (iNES)

Magic bytes `"NES\x1a"` at offset 0x000 are **required**. If missing, the file
is not recognized as NES. Title is at 0x010, 16 bytes, ASCII padded with 0x00.

### SNES (LoROM vs HiROM)

Two possible header locations:
- **LoROM**: offset 0x7FC0 (older games, <32Mbit)
- **HiROM**: offset 0xFFC0 (most games from late SNES era)

Detection algorithm:
1. Read 21 bytes at 0xFFC0 (HiROM candidate)
2. Read 21 bytes at 0x7FC0 (LoROM candidate)
3. For each: check if bytes are printable ASCII (0x20-0x7E), not all zeros
4. If only one valid → use it
5. If both valid → prefer HiROM (0xFFC0, more common)
6. If neither valid → fallback to filename

Title is 21 bytes, ASCII padded with 0x20 (space).

### Game Boy / Game Boy Color

Title at 0x134, 11 bytes ASCII. CGB flag at 0x143 determines if Color-enhanced:
- 0x80 = DMG + CGB compatible
- 0xC0 = CGB only

### Game Boy Advance

Title at 0xA0, 12 bytes ASCII. Logo data at 0x04 can confirm valid GBA ROM.

### Sega Genesis / Mega Drive

Title at 0x120, 48 bytes. Each character uses 2 bytes big-endian (e.g., `'A'` =
0x0041). Parser reads only the low byte of each pair, producing up to 24
characters. This matches the convention used by BlastEm, Genesis Plus GX, and
other emulators.

### Master System / Game Gear

Title at 0x7FF0, 32 bytes ASCII. Same offset for both systems — the extension
determines the system label shown in the preview.

### PC Engine / TurboGrafx-16

Title at 0x120, 32 bytes ASCII.

## Fallback Behavior

If header parsing fails (invalid bytes, too short, not a known extension):
- Preview shows the filename without extension as plain text
- Status bar shows "ROM" as system label
- No error is shown — this is expected for non-ROM files with ROM extensions

## Preview Display

ROM files render in the preview pane as:
- **Content**: parsed game title (plain text, no syntax highlighting)
- **Status bar**: `"NES — Super Mario Bros"` or `"ROM — filename"` (fallback)
- **Panel**: same TextScroll panel used for plain text files

## Cover Art and Metadata

Cover art and game metadata loaded via fallback chain
(`FilePreviewService` + `MillerColumnsPage`):

### 1. gamelist.xml (local)

If `gamelist.xml` exists in the ROM's directory, `GamelistParser` parses it on
directory entry (streaming XmlReader, no DOM). Match by filename, name-without-ext,
or parent ZIP name for files inside archives.

**Data**: name, description, developer, publisher, genre, players, rating,
release date, cover art image paths.

**Cover art priority**: `<cover>` → `<image>` → `<thumbnail>` (local files).

### 2. LibRetro Thumbnails (network)

If no gamelist.xml or no local cover, fetch from LibRetro:

- **URL**: `https://thumbnails.libretro.com/{system_name}/Named_Titles/{title}.png`
- **Auth**: none required
- **Matching**: exact title, then stripped region variations (`(USA)`, `(Europe)`, ...)

System name mapping (partial; full dict in `MillerColumnsPage.xaml.cs:4763`):

| System | LibRetro Name |
|--------|--------------|
| NES | `Nintendo - Nintendo Entertainment System` |
| SNES | `Nintendo - Super Nintendo Entertainment System` |
| Game Boy | `Nintendo - Game Boy` |
| Game Boy Color | `Nintendo - Game Boy Color` |
| GBA | `Nintendo - Game Boy Advance` |
| Genesis | `Sega - Mega Drive - Genesis` |
| Master System | `Sega - Master System` |
| Game Gear | `Sega - Game Gear` |
| PC Engine | `NEC - PC Engine` |
| Atari 2600/5200/7800/Jaguar/Lynx | `Atari - …` / `Atari - Jaguar` / `Atari - Lynx` |
| ColecoVision | `Coleco - ColecoVision` |
| Intellivision | `Mattel - Intellivision` |
| Vectrex | `GCE - Vectrex` |
| N64 / NDS / 3DS / Virtual Boy / GameCube | `Nintendo - …` |
| Dreamcast | `Sega - Dreamcast` |
| WonderSwan | `Bandai - WonderSwan` |
| Neo Geo Pocket | `SNK - Neo Geo Pocket` |

### 3. System Icon

Retro console icons from the icon set shown when: system not in LibRetro mapping,
cover 404, or offline/network error.

### Cache Strategy — SQLite, 30-day TTL

Cover-art lookups are **cached** in SQLite (`MetadataCache`): both hits and misses
are recorded, and a URL that was already fetched (or already 404'd) within the last
**30 days** is skipped — `IsLibRetroUrlCachedAsync(url)` → skip;
`SetLibRetroThumbnailAsync(url, hitOrMiss)`. This prevents re-fetching every
preview and rate-limiting the LibRetro service. Clearing the cache: Settings →
Clear cache (also clears the metadata DB).

> The shared `MetadataCache`/`MetadataCacheDb` SQLite database also serves music
> metadata (see `Metadata/`); the ROM cover-art URL cache lives in the same DB.

### Implementation Notes

- `GamelistParser` streams with XmlReader (no DOM), dictionary indexed by filename
  and name-without-extension for O(1) lookup.
- Cover art: gamelist local → LibRetro (with title variations) → system icon.
- In-flight fetch is cancelled when the preview changes.
- Metadata rows shown when gamelist data available; description limited to 3 lines.
