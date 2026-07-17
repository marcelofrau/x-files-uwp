# Assets Guide

## Directory structure

```
XFiles/Assets/
├── Fonts/             # Custom .ttf (post-MVP, see UI-THEMING.md)
├── Icons/
│   └── app.ico        # App icon only (sole .ico)
└── Views/             # Per-view/page icons & images
    ├── MainPage/
    └── ...            # One folder per view that needs assets
```

Each view/page that needs icons gets its own subfolder under `Views/`.

## Naming convention

**Pattern:** `{viewname}-{descriptor}[-{size}].png`

| Part | Rule | Example |
|------|------|---------|
| viewname | Lowercase, no separator | `mainpage`, `fileactionsheet` |
| descriptor | kebab-case, meaningful | `close`, `folder`, `copy`, `step1-disabled` |
| size (optional) | Pixel size for icons | `16`, `20`, `32`, `48`, `100` |

**Examples:**

- `mainpage-about-32.png` — MainPage's about button, 32px
- `fileactionsheet-close-20.png` — Context menu close button, 20px
- `mainpage-banner.png` — No size = full-width banner/background

**Rules:**
- Always lowercase
- Use hyphens as separator (never underscores or camelCase)
- Omit size only for full-width images, backgrounds, or logos that fill a container

## Icon source: personal set

All PNG icons come from the developer's personal Icons8-derived collection at:

```
F:\workspace\icons8-personal-set
```

The set is organized by pixel size:

```
icons8-personal-set/
├── 16x16/
├── 20x20/
├── 24x24/
├── 32x32/
├── 48x48/
├── 50x50/
├── 64x64/
├── 100x100/
├── 128x128/
├── 256x256/
├── ico/           # .ico variants — do not use unless explicitly asked
├── catalog/       # Metadata for browsing
└── download-*.py  # Fetch scripts
```

**Workflow to add a new icon:**

1. Identify the needed size from the context (see "Size selection" below)
2. Copy from `icons8-personal-set/{size}x{size}/{name}-{size}.png`
3. Rename following the convention: `{viewname}-{descriptor}-{size}.png`
4. Place in `XFiles/Assets/Views/{ViewName}/`
5. Reference in XAML: `ms-appx:///Assets/Views/{ViewName}/{filename}`
6. Register in `XFiles.csproj`: `<Content Include="Assets\Views\{ViewName}\{filename}" />`

## Format rules

- **Always use PNG.** Never use `.ico` files unless the user explicitly requests it.
  - The sole exception is `Assets/Icons/app.ico` (application icon), which must be `.ico`.
- Do not convert `.ico` files to `.png` — always source the PNG from the personal set.
- Do not use JPG, BMP, GIF, SVG, or WebP for icons.

## Size selection

Match the icon size to the UI context:

| Context | Size |
|---------|------|
| Inline with small text / status indicators | 16x16 |
| Toolbar buttons, compact actions | 20x20 |
| Tab / sidebar icons (alongside labels) | 32x32 |
| Standalone buttons, dialog body icons | 48x48 |
| Large indicators (success/failure, empty states) | 100x100 |
| Full-width backgrounds, banners | Variable (omit size in name) |

If in doubt between two sizes, prefer the larger one — it can always be scaled down in XAML with `Width`/`Height`.

## XAML referencing (UWP)

UWP uses the `ms-appx:///` URI scheme for app package assets:

```xml
<Image Source="ms-appx:///Assets/Views/MainPage/mainpage-banner.png" />
```

Relative paths also work within the same project:

```xml
<Image Source="/Assets/Views/MainPage/mainpage-banner.png" />
```

**Do NOT use** `avares://` — that is an Avalonia-specific scheme.

## csproj registration

Every asset file must be registered in `XFiles.csproj` for deployment:

```xml
<ItemGroup>
  <Content Include="Assets\Views\MainPage\mainpage-close-20.png" />
</ItemGroup>
```

Without this entry, the file won't be included in the app package and will fail at runtime.

## View-agnostic icons

If an icon is needed by multiple views and is not specific to any single one, place it in `Assets/Icons/` (not `Assets/Views/`). Currently this folder only contains `app.ico`.

## Attribution

All third-party icons used in this project must be attributed in `docs/ATTRIBUTIONS.md`.
See that file for current attributions and licenses.
