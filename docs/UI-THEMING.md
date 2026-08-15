---
layout: default
title: UI and Theming — XAML with Custom ControlTemplate
---
# UI and Theming — XAML with Custom ControlTemplate

See `DECISIONS.md` (ADR-002) for why XAML instead of Win2D, and ADR-009 for the Win2D
exception (audio visualizers only).

## General Principle

No control uses the default Fluent Design appearance (`ListView`/`GridView` "out of the
box"). Every interactive control (file row, context menu button, dialog, etc.) has its own
`Style`/`ControlTemplate`, defined in `Theming/BladeTheme.xaml` (a `ResourceDictionary`
merged in `App.xaml`).

## Gamepad Focus (UWP Native — Do Not Reimplement)

- `IsTabStop="True"` + `UseSystemFocusVisuals="False"` — replace the default blue
  rectangle with a custom visual indicator via `VisualStateManager` states
  (`Focused`/`PointerOver`/`Selected`) in each custom `ControlTemplate`.
- `XYFocusUp`/`XYFocusDown`/`XYFocusLeft`/`XYFocusRight` bind navigation between the
  columns and modal surfaces when the user uses the physical D-pad instead of the
  `GamepadInputService` logical flow. Define explicitly to avoid relying on system
  auto-heuristics, which may pick the wrong element in asymmetric layouts.
- `IsFocusEngaged` is used in `TextEditorOverlay`/`StartMenu` to lock focus to the modal
  surface — that's the intended UWP pattern for "trapped" focus in a dialog-style overlay.

## ResourceDictionary Structure

```
Theming/BladeTheme.xaml
├── Brushes                     (XFilesBackgroundBrush, XFilesAccentBrush, XFilesBorderBrush,
│                                XFilesSelectedBackgroundBrush, XFilesDangerBrush, ...)
├── Typography                  (XFilesTitleFont, XFilesBodyFont, XFilesMonoFontFamily — Oxanium)
├── Styles
│   ├── RetroListViewStyle      (column list container)
│   └── RetroListViewItemStyle  (file/folder row — Normal/PointerOver/Focused/Selected)
└── Templates                   (ConfirmDialogButton, etc. — per-control)
```

Reference via `StaticResource`/`{ThemeResource}` from any XAML in the app. Brushes use the
`XFiles*` prefix; typography uses `XFiles*Font*`; there is no per-widget naming scheme —
one dictionary, one style set, used everywhere.

## Typography

Custom font **Oxanium** embedded in `Assets/Fonts/` (`Oxanium-Regular.ttf`,
`Oxanium-Bold.ttf`, `Oxanium-ExtraLight.ttf`) and referenced as
`XFilesTitleFont`/`XFilesBodyFont`/`XFilesMonoFontFamily`. Fallback chain keeps
`Consolas`/`Cascadia Mono` as monospace fallback for code previews.

## No Runtime JSON Theme

`Theming/AppTheme.cs` (JSON theme loader) was **planned but never built** — the theme is
XAML-only. `BladeTheme.xaml` is merged at `App.xaml` level and there is no
`x-files-theme.json` parsing at runtime. A future theme selector would add it; see
`SETTINGS-EXPANSION.md` and the `ROADMAP.md` backlog.

## Base Layout (3 Columns)

```xml
<Grid>
  <Grid.ColumnDefinitions>
    <ColumnDefinition Width="1*" />   <!-- Parent -->
    <ColumnDefinition Width="1.75*" /> <!-- Current -->
    <ColumnDefinition Width="2.25*" /> <!-- Preview -->
  </Grid.ColumnDefinitions>
  <!-- Controls/ColumnListView (Parent, Current) + Controls/MediaPreviewControl (Preview) -->
</Grid>
```

Proportions adjustable; values above are the shipped default (yazi-style: parent smaller,
current medium, preview larger).

## Control Inventory (Controls/*.xaml)

| Control | Purpose |
|---|---|
| `MillerColumnsPage` | Main page: 3 columns, preview, fullscreen media, OSD, batch mode |
| `ColumnListView` | Reusable column list (parent + current) |
| `MediaPreviewControl` | Preview pane: text/image/audio/video/PDF/ROM |
| `FileActionSheet` | Y-button context menu |
| `StartMenu` | Start button overlay (settings, logs, favorites, search, about) |
| `TextEditorOverlay` | Fullscreen text editor (WebView + TextBox bridge) |
| `FolderBrowserDialog` | Destination picker for copy/move/paste |
| `AlertDialog` / `InputDialog` / `OverwriteDialog` | Modal prompts |
| `FileOperationConfirmDialog` / `OperationProgressDialog` | Batch operation flow |
| `SettingsPage` / `LogsPage` / `CounterPage` / `DirectoryTestPage` | Dev/utility pages |
| `LetterGridOverlay` | Letter-jump grid for long listings |
| `GamepadLegend` | Footer button hints ("A: Open  B: Back  Y: Menu") |
| `ImageFullScreenOverlay` / `PdfFullScreenOverlay` | Fullscreen media viewers |
| `ShareDialog` / `VideoTrackMenu` | QR share / video track picker |
| `VuMeterBar` | Audio level indicator |
| `DebugOverlay` | **Dead code** (see tech-debts) |

## Custom Font / Further Styling (Backlog)

- Theme selector UI (JSON runtime theme) — see `SETTINGS-EXPANSION.md`.
- Empty-state visual pass and column transition animations — `ROADMAP.md` backlog.
