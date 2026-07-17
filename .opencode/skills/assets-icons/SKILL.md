---
name: assets-icons
description: Use ONLY when adding, finding, or referencing UI icons/images/assets in this project (toolbar buttons, view controls, dialogs). Covers naming convention, directory structure, format rules, size selection, personal set path, and workflow. NOT for file-type icons in the file explorer — use fileexplorer-icons skill instead.
---

# Assets & Icons Guide (UWP/XAML)

> **File-type icons** (for file listing/preview in the explorer) use the
> `fileexplorer-icons` skill instead. This skill covers UI controls only.

## Directory structure

```
XFiles/Assets/
├── FileTypes/             # File-type icons (see fileexplorer-icons skill)
│   ├── 48x48/
│   └── generic/
├── Fonts/             # Custom .ttf (post-MVP, see UI-THEMING.md)
├── Icons/
│   └── app.ico        # App icon only (sole .ico)
└── Views/             # Per-view folder
    ├── MainPage/
    └── ...
```

Each view/page needing icons gets subfolder under `Views/`.

## Naming convention

Pattern: `{viewname}-{descriptor}[-{size}].png`

- `viewname` — lowercase, no separator (`mainpage`, `fileactionsheet`)
- `descriptor` — kebab-case (`close`, `folder`, `copy`, `step1-disabled`)
- `size` — optional pixel size for icons (`16`, `20`, `32`, `48`, `100`)
- Omit size only for full-width banners/backgrounds

Examples: `mainpage-about-32.png`, `fileactionsheet-close-20.png`, `mainpage-banner.png`.

## Format rules

- **Always PNG.** No JPG, BMP, GIF, SVG, WebP.
- `.ico` only for `Assets/Icons/app.ico` — never elsewhere unless explicitly asked.
- Never convert `.ico` → PNG. Source PNG from personal set.

## Size selection

| Context | Size |
|---------|------|
| Inline with small text / status | 16x16 |
| Toolbar buttons, compact actions | 20x20 |
| Tab / sidebar icons | 32x32 |
| Standalone buttons, dialog body | 48x48 |
| Large indicators (success/failure) | 100x100 |
| Full-width banners | Omit size in name |

When unsure between two sizes, prefer larger — scale down in XAML via `Width`/`Height`.

## Icon source

Personal Icons8-derived collection at:
```
F:\workspace\icons8-personal-set
```

Organized by size: `{size}x{size}/{name}-{size}.png`. Also `ico/`, `catalog/` dirs.

## Workflow

1. Identify needed size from context
2. Copy from `icons8-personal-set/{size}x{size}/{name}-{size}.png`
3. Rename per convention: `{viewname}-{descriptor}-{size}.png`
4. Place in `XFiles/Assets/Views/{ViewName}/`
5. Reference in XAML: `ms-appx:///Assets/Views/{ViewName}/{filename}`
6. Add to `XFiles.csproj` as `<Content Include="Assets\Views\{ViewName}\{filename}" />`

**Minimum required for a new page:** `{viewname}-close-20.png` (close/back button) if applicable.

## XAML referencing (UWP)

UWP uses `ms-appx:///` scheme for app assets:
```xml
<Image Source="ms-appx:///Assets/Views/MainPage/mainpage-banner.png" />
```

Or relative path (works within same project):
```xml
<Image Source="/Assets/Views/MainPage/mainpage-banner.png" />
```

**Do NOT use** `avares://` — that is Avalonia-only.

## View-agnostic icons

If icon needed by multiple views, place in `Assets/Icons/` (currently only `app.ico`).

## csproj registration

Every asset file must be registered in `XFiles.csproj`:
```xml
<ItemGroup>
  <Content Include="Assets\Views\MainPage\mainpage-close-20.png" />
</ItemGroup>
```

Without this, the file won't be deployed with the app package.

## Attribution

Third-party icons must be attributed in `docs/ATTRIBUTIONS.md`. Confirm license allows redistribution before committing.
