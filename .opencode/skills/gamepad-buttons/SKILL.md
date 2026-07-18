---
name: gamepad-buttons
description: Use ONLY when adding, finding, or referencing gamepad button icons for the UI (button legends, command hints, help overlays). Covers Xbox button asset pack, theme system, naming convention. NOT for file-type icons (use fileexplorer-icons) or UI control icons (use assets-icons).
---

# Gamepad Button Icons Guide

## Purpose

Button icons displayed in the UI to show gamepad commands (legends, hints, help).
Source: "XBOX BUTTONS - Premium Assets" by Arks @Scissormarks.

## Asset Source

```
F:\workspace\assets\XBOX BUTTONS - Premium Assets\
├── Digital Buttons/ABXY/      # A, B, X, Y (xbox variant only)
├── Digital Buttons/Shoulder/  # LB, RB bumpers
├── Digital Buttons/System/    # View, Menu, Home
├── D-Pad/                     # D-Pad (xbox + xboxone variants)
├── Analog Triggers/           # LT, RT triggers
└── Svg/                       # SVG sources (120×120 viewBox)
```

**License:** Premium asset pack. Attribution required (see Attribution section).

## Themes

4 visual themes, stored in `XFiles/Assets/GamepadButtons/`:

| Theme | D-Pad style | Other buttons |
|-------|-------------|---------------|
| `xbox-dark` | Xbox 360 dark | Xbox classic |
| `xbox-light` | Xbox 360 light | Xbox classic |
| `xboxone-dark` | Xbox One/Series dark | Xbox classic (fallback) |
| `xboxone-light` | Xbox One/Series light | Xbox classic (fallback) |

**Note:** ABXY, Bumpers, Triggers, System buttons only have `xbox` variant.
D-Pad is the only button with both `xbox` and `xboxone` styles.

## Naming Convention

Each theme folder contains 22 PNGs:

```
{theme}/
├── a-1.png, a-2.png, a-3.png     # A button (states 1=idle, 2=pressed, 3=highlighted)
├── b-1.png, b-2.png, b-3.png     # B button
├── x-1.png, x-2.png, x-3.png     # X button
├── y-1.png, y-2.png, y-3.png     # Y button
├── dpad-1.png, dpad-2.png, dpad-3.png  # D-Pad
├── lb.png, rb.png                 # Bumpers (no state variants)
├── lt.png, rt.png                 # Triggers (no state variants)
├── view.png, menu.png, home.png   # System (no state variants)
```

### State numbers

| State | Usage |
|-------|-------|
| 1 | Idle / default |
| 2 | Pressed / active |
| 3 | Highlighted / focused |

## Runtime theme selection

```csharp
// Central config (will be in settings later)
private static string _gamepadButtonTheme = "xbox-dark"; // default

public static string GetButtonIcon(string button, int state = 1)
{
    string suffix = state > 1 ? $"-{state}" : "";
    return $"ms-appx:///Assets/GamepadButtons/{_gamepadButtonTheme}/{button}{suffix}.png";
}

// Usage in XAML
<Image Source="{Binding AIcon}" Width="64" Height="64" />
```

## Adding new button themes

1. Find source files in `F:\workspace\assets\XBOX BUTTONS - Premium Assets\`
2. Run conversion script:
   ```powershell
   .\scripts\copy-gamepad-icons.ps1
   ```
3. Script copies relevant buttons, renames, converts 480→64 via Inkscape
4. Output goes to `XFiles/Assets/GamepadButtons/{theme}/`
5. Register new entries in `XFiles.csproj` as `<Content>`
6. Update default theme in code

## Adding new individual buttons

1. Find PNG in asset pack: `F:\workspace\assets\XBOX BUTTONS - Premium Assets\`
2. Convert with Inkscape: `inkscape "$src" --export-type=png --export-filename="out.png" --export-width=64 --export-height=64`
3. Place in all 4 theme folders
4. Register in csproj

## SVG sources

SVGs available in `Svg/` folder (120×120 viewBox, clean vector).
Useful if XAML vector rendering is needed later (scalable, no pixelation).

## Attribution

**Required** when distributing the app:

> UI Elements by Mikkel Julian "Arks" Petersen
> - @ScissorMarks (Twitter)
> - https://arks.itch.io
> - https://arks.itch.io/xbox-buttons

Add to `docs/ATTRIBUTIONS.md`.

## Key button mapping (for legends)

| Action | Button | Icon filename |
|--------|--------|---------------|
| Confirm / Select | A | `a-1.png` |
| Back / Cancel | B | `b-1.png` |
| Secondary action | X | `x-1.png` |
| Context menu | Y | `y-1.png` |
| Navigate left/right (columns) | LB / RB | `lb.png` / `rb.png` |
| Page up/down (alternative) | LT / RT | `lt.png` / `rt.png` |
| View toggle | View | `view.png` |
| Menu / Options | Menu | `menu.png` |
| Home / System | Home | `home.png` |
| Directional navigation | D-Pad | `dpad-1.png` |
