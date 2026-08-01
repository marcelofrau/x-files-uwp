# File-Type Icon Mappings

File explorer icons (listing columns + preview pane) map file extensions to Papirus
24px icons, stored in `Assets/FileTypes/` and referenced as
`ms-appx:///Assets/FileTypes/<name>-24.png`.

> Source: Papirus icon theme (GPL-3.0, matches app license). Conversion workflow and
> naming rules: see `.opencode/skills/fileexplorer-icons/SKILL.md`.

## Naming Convention

| Asset | Pattern |
|---|---|
| File-type icons | `filetype-{category}-{name}-24.png` |
| Folder icons | `folder-{color}-24.png` (orange/blue/green/yellow/magenta/...; color runtime-selectable) |
| Drive icon | `drive-harddisk-24.png` |
| Generic file | `file-generic-24.png` |
| Archive (zip/7z/rar virtual folder) | `file-archive-24.png` |
| Favorites | `favorite-24.png` / `favorites-24.png` / `file-favorite-24.png` |

All file-type icons are **24×24 PNG** (`-24.png`). Directory: `XFiles/Assets/FileTypes/`
(61 icons as of v1.2.0).

## Resolution Order (`ColumnListView.xaml.cs`)

1. `IsVirtual` (favorites root / archive root) → drive / favorite icon.
2. Directory → `folder-{color}-24.png`.
3. `.zip`/`.7z`/`.rar` → `file-archive-24.png`.
4. Known extension → lookup in the static `ExtIcons` dictionary
   (`ColumnListView.xaml.cs:63`).
5. Unknown extension → `file-generic-24.png`.

## Representative Mappings

| Extension | Asset |
|---|---|
| `.png`/`.jpg`/`.jpeg`/`.bmp`/`.gif`/`.svg`/`.tiff`/`.tga`/`.webp`/`.heic` | `filetype-image-{…}-24.png` |
| `.mp4`/`.avi`/`.mkv`/`.webm`/`.flv`/`.wmv`/`.mov`/`.m4v`/`.ts`/`.vob`/`.3gp` | `filetype-video-{…}-24.png` |
| `.mp3`/`.flac`/`.wav`/`.ogg`/`.m4a`/`.wma`/`.aac`/`.opus`/`.mid`/`.midi` | `filetype-audio-{…}-24.png` |
| `.tar`/`.gz`/`.bz2`/`.xz`/`.tgz`/`.zst` | `filetype-application-tar/gzip-24.png` |
| `.iso`/`.img`/`.cdi`/`.gdi`/`.cue`/`.nrg`/`.mdf`/`.ciso` | `filetype-application-iso-24.png` |
| `.pdf` | `filetype-application-pdf-24.png` |
| `.txt`/`.csv`/`.ini`/`.cfg`/`.yaml`/`.toml`/`.srt`/`.sql`/`.tex`/`.doc(x)`/`.xls(x)`/... | `filetype-text-generic-24.png` |
| `.log`/`.out`/`.err`/`.env` | `filetype-text-log-24.png` |
| `.md`/`.rst` | `filetype-text-markdown-24.png` |
| `.py`/`.c`/`.cpp`/`.cs`/`.java`/`.js`/`.ts`/`.css`/`.xml`/`.go`/`.rs`/`.rb`/`.lua`/`.sh`/`.pl` | `filetype-text-{lang}-24.png` |
| `.exe` | `filetype-application-executable-24.png` |

ROM extensions (`.nes`, `.sfc`, `.gb`, `.gba`, `.gen`, `.sms`, ...) resolve through the
ROM system→icon mapping — see `ROM-FORMATS.md`.

## Adding a New File-Type Icon

1. Export the Papirus SVG → 24×24 PNG (keep `-24.png` suffix).
2. Drop it in `XFiles/Assets/FileTypes/` (add `<Content>` entry in `XFiles.csproj`).
3. Add the extension → asset entry to the `ExtIcons` dictionary in
   `ColumnListView.xaml.cs`.
4. Full workflow: `.opencode/skills/fileexplorer-icons/SKILL.md`.
