---
name: fileexplorer-icons
description: Use ONLY when adding, finding, or mapping file-type icons for the file explorer columns (file listing, preview pane). Covers Papirus icon source, extension→icon mapping, SVG-to-PNG conversion, and UWP asset registration. NOT for UI controls/views (use assets-icons instead).
---

# File-Type Icons Guide (File Explorer)

## Purpose

Icons displayed next to files in the file explorer columns (current/preview). Each file
extension maps to a specific icon. Separate from UI control icons (close buttons,
toolbar actions) — those use the `assets-icons` skill.

## Icon Source: Papirus

Primary source for file-type icons. 1100+ mimetype entries, 290+ unique SVGs.
CC-BY-SA 4.0 license. Repo: `PapirusDevelopmentTeam/papirus-icon-theme`.

```
F:\workspace\fileexplorer-icons-reference\papirus\
├── Papirus/
│   ├── 16x16/mimetypes/    # Small list icons
│   ├── 24x24/mimetypes/    # Standard list
│   ├── 32x32/mimetypes/    # Medium/detail view
│   ├── 48x48/mimetypes/    # Large/detail view ← primary
│   ├── 64x64/mimetypes/    # XL preview
│   └── 128x128/mimetypes/  # XXL preview
├── Papirus-Dark/           # Dark theme variant (don't use)
└── Papirus-Light/          # Light theme variant (don't use)
```

**Use Papirus (base) variant only.** Not Dark/Light for file-type icons.

## IMPORTANT: Redirect Files

Many `.svg` files in Papirus are **text redirects**, not actual SVGs. They contain a
relative path to another SVG file (like symlinks). Example:

```
# application-pdf.svg (text redirect)
application-x-pdf.svg

# text-python.svg (text redirect)
text-x-python.svg
```

**Before converting, resolve redirects** to get the actual SVG content.

### How to resolve

```powershell
function Resolve-PapirusIcon {
    param([string]$SvgPath)
    $content = Get-Content $SvgPath -Raw
    if ($content -match '<svg') {
        return $SvgPath  # actual SVG
    } else {
        # resolve relative path
        $dir = Split-Path $SvgPath
        $target = Join-Path $dir $content.Trim()
        return (Resolve-Path $target).Path
    }
}
```

## Folder Icons: Multi-Color System

Folder icons come in 9 color variants for theme support. All colors are pre-converted
and stored in `Assets/FileTypes/`. The active color is selected at runtime via settings.

### Available Colors

| Color | Filename | Hex (main) | Hex (light) |
|-------|----------|-----------|-------------|
| blue | `folder-blue-24.png` | `#4877b1` | `#5294e2` |
| yellow | `folder-yellow-24.png` | `#c8a800` | `#e0c600` |
| deeporange | `folder-deeporange-24.png` | `#bf360c` | `#e64a19` |
| green | `folder-green-24.png` | `#60924b` | `#87b158` |
| indigo | `folder-indigo-24.png` | `#303f9f` | `#5c6bc0` |
| magenta | `folder-magenta-24.png` | `#8e1471` | `#b71c7a` |
| yaru | `folder-yaru-24.png` | `#e95420` | `#f2855d` |
| darkcyan | `folder-darkcyan-24.png` | `#35818a` | `#45abb7` |
| orange | `folder-orange-24.png` | `#e65100` | `#f57c00` |

### How to add a new color

1. Find the SVG: `F:\workspace\fileexplorer-icons-reference\papirus\Papirus\48x48\places\folder-{color}.svg`
2. Resolve redirects if needed (some are text redirects to other SVGs)
3. Convert with Inkscape (required for proper transparency):
   ```powershell
   inkscape "$resolved" --export-type=png --export-filename="folder-{color}-24.png" --export-width=24 --export-height=24
   ```
4. Place in `XFiles/Assets/FileTypes/`
5. Add to `XFiles.csproj` as `<Content Include="Assets\FileTypes\folder-{color}-24.png" />`
6. Add color entry to the table above

### Why Inkscape (not ImageMagick)

ImageMagick's SVG renderer often produces RGB output without alpha channel, even with
`-background none -alpha set`. The resulting PNGs appear to have a white background on
dark UIs. Inkscape handles SVG transparency correctly out of the box.

Verification (corner pixel check):
```powershell
magick "output.png" -crop "1x1+0+0" txt:- 2>&1 | Select-Object -Last 1
# Expected: (255,255,255,0)  #FFFFFF00  — transparent
```

### Runtime color selection

The `EntryViewModel.Icon` property returns the path to the active folder color:

```csharp
// In EntryViewModel or a central config
private static string FolderIconColor = "blue"; // default, changeable in settings

public string Icon => IsDirectory
    ? $"ms-appx:///Assets/FileTypes/folder-{FolderIconColor}-24.png"
    : (IsArchive ? "ms-appx:///Assets/FileTypes/file-archive-24.png"
                  : "ms-appx:///Assets/FileTypes/file-generic-24.png");
```

## Extension → Icon Mapping

Freedesktop MIME naming: `{type}-{subtype}[-{variant}].svg`

To find icon for a file extension:
1. Look up MIME type: `https://www.iana.org/assignments/media-types/`
2. Or: `xdg-mime query filetype <filename>` (Linux)
3. Replace `/` with `-`, lowercase: `text/x-python` → `text-x-python.svg`
4. If not found, try parent type (e.g. `text-x-generic.svg`)

### Common mappings

| Ext | Papirus icon | Notes |
|-----|-------------|-------|
| `.txt` | `text-x-generic` | Generic text |
| `.py` | `text-x-python` | |
| `.js` | `text-x-javascript` | |
| `.ts` | `text-x-typescript` | |
| `.html` | `text-html` | |
| `.css` | `text-css` | |
| `.json` | `application-json` | |
| `.xml` | `application-xml` | |
| `.pdf` | `application-x-pdf` | |
| `.png` | `image-x-generic` | Generic image |
| `.jpg` | `image-jpeg` | |
| `.gif` | `image-gif` | |
| `.mp3` | `audio-mpeg` | |
| `.mp4` | `video-mp4` | |
| `.zip` | `application-x-zip` | |
| `.7z` | `application-x-7z` | |
| `.exe` | `application-x-executable` | |
| `.dll` | `application-x-sharedlib` | |
| `.rs` | `text-x-rust` | |
| `.go` | `text-x-go` | |
| `.c` | `text-x-c` | |
| `.cpp` | `text-x-c++` | |
| `.cs` | `text-x-csharp` | |
| `.java` | `text-x-java` | |
| `.rb` | `text-x-ruby` | |
| `.sh` | `application-x-shellscript` | |

**Unknown extensions:** Fall back to `file-generic-24.png`.

Add new mappings to `docs/FILETYPE-ICONS.md` as discovered.

## Size Selection

| Context | Size | Source dir |
|---------|------|-----------|
| List view (small) | 16x16 | `Papirus/16x16/mimetypes/` |
| List view (standard) | 24x24 | `Papirus/24x24/mimetypes/` |
| Detail/tiles view | 32x32 | `Papirus/32x32/mimetypes/` |
| Large icons view | 48x48 | `Papirus/48x48/mimetypes/` |
| Preview pane (large) | 64x64 | `Papirus/64x64/mimetypes/` |
| Preview pane (XL) | 128x128 | `Papirus/128x128/mimetypes/` |

MVP focus: **24x24** (list view) — current default.

## SVG → PNG Conversion

Papirus ships only SVG. UWP requires PNG. **Always convert.**

### Inkscape (REQUIRED for transparency)

ImageMagick's SVG renderer often drops the alpha channel, producing PNGs with white
backgrounds on dark UIs. **Always use Inkscape** for conversion:

```powershell
# Single icon (resolve redirects first!)
$resolved = Resolve-PapirusIcon "F:\workspace\fileexplorer-icons-reference\papirus\Papirus\48x48\mimetypes\text-x-generic.svg"
inkscape "$resolved" --export-type=png --export-filename="output.png" --export-width=24 --export-height=24
```

### Batch convert mimetype icons

```powershell
$srcDir = "F:\workspace\fileexplorer-icons-reference\papirus\Papirus\48x48\mimetypes"
$outDir = "F:\workspace\x-files-uwp\XFiles\Assets\FileTypes"
New-Item -ItemType Directory -Force -Path $outDir

Get-ChildItem $srcDir -Filter "*.svg" | ForEach-Object {
    $resolved = Resolve-PapirusIcon $_.FullName
    $outName = "filetype-$($_.BaseName)-24.png"
    inkscape "$resolved" --export-type=png --export-filename=(Join-Path $outDir $outName) --export-width=24 --export-height=24
}
```

### Verification (only if requested)

```powershell
# Check corner pixel — should be transparent (alpha=0)
magick "output.png" -crop "1x1+0+0" txt:- 2>&1 | Select-Object -Last 1
# Expected: (255,255,255,0)  #FFFFFF00  — transparent
```

## Naming Convention (project assets)

Pattern: `filetype-{papirus-base-name}-{size}.png`

Examples:
- `filetype-text-x-generic-24.png`
- `filetype-application-x-pdf-24.png`
- `filetype-text-x-python-24.png`

Folder icons: `folder-{color}-{size}.png`
- `folder-blue-24.png`
- `folder-green-24.png`

Fallbacks:
- `file-generic-24.png` (unknown file type)
- `folder-{color}-24.png` (folder, color from settings)

## Project Placement

```
XFiles/Assets/
└── FileTypes/
    ├── folder-blue-24.png        # Folder colors (9 variants)
    ├── folder-yellow-24.png
    ├── folder-deeporange-24.png
    ├── folder-green-24.png
    ├── folder-indigo-24.png
    ├── folder-magenta-24.png
    ├── folder-yaru-24.png
    ├── folder-darkcyan-24.png
    ├── folder-orange-24.png
    ├── file-archive-24.png       # Archive file icon
    └── file-generic-24.png       # Generic file icon
```

## XAML Reference

```xml
<!-- Folder (color from ViewModel) -->
<Image Source="{Binding Icon}" Width="24" Height="24" />

<!-- Specific file type -->
<Image Source="ms-appx:///Assets/FileTypes/filetype-text-x-generic-24.png"
       Width="24" Height="24" />
```

## csproj Registration

```xml
<ItemGroup>
  <Content Include="Assets\FileTypes\folder-blue-24.png" />
  <Content Include="Assets\FileTypes\folder-green-24.png" />
  <Content Include="Assets\FileTypes\file-archive-24.png" />
  <Content Include="Assets\FileTypes\file-generic-24.png" />
</ItemGroup>
```

## Workflow (adding new file type support)

1. Identify file extension(s)
2. Look up MIME type → Papirus icon name
3. Find source: `papirus/Papirus/48x48/mimetypes/{name}.svg`
4. **Resolve redirects** until you reach actual SVG content
5. Convert with **Inkscape**: `inkscape "$resolved" --export-type=png --export-filename="output.png" --export-width=24 --export-height=24`
6. Verify transparency: `magick "output.png" -crop "1x1+0+0" txt:-`
7. Name: `filetype-{name}-24.png`
8. Place: `XFiles/Assets/FileTypes/`
9. Register: add `<Content Include="...">` in `XFiles.csproj`
10. Document: add mapping to `docs/FILETYPE-ICONS.md`

## Workflow (adding new folder color)

1. Find SVG: `papirus/Papirus/48x48/places/folder-{color}.svg`
2. Resolve redirects if needed
3. Convert with Inkscape: `inkscape "$resolved" --export-type=png --export-filename="folder-{color}-24.png" --export-width=24 --export-height=24`
4. Verify transparency
5. Place: `XFiles/Assets/FileTypes/`
6. Register in csproj
7. Add to Available Colors table above
8. Update `EntryViewModel` or config to support new color name

## Updating Papirus

```powershell
cd F:\workspace\fileexplorer-icons-reference\papirus
git pull
```

## Attribution

Papirus: CC-BY-SA 4.0 — `docs/ATTRIBUTIONS.md`
Repo: https://github.com/PapirusDevelopmentTeam/papirus-icon-theme
