# UI and Theming — XAML with Custom ControlTemplate

See `DECISIONS.md` (ADR-002) for why XAML instead of Win2D.

## General Principle

No control uses the default Fluent Design appearance (`ListView`/`GridView` "out of
the box"). Every interactive control (file row, context menu button, etc.) has its own
`Style`/`ControlTemplate`, defined in `Theming/RetroTheme.xaml` (a `ResourceDictionary`
merged in `App.xaml`).

## Gamepad Focus (UWP Native — Do Not Reimplement)

- `IsTabStop="True"` + `UseSystemFocusVisuals="False"` (to replace the default blue
  rectangle with a custom visual indicator via `FocusVisualStyle` or `VisualStateManager`
  in the `Focused`/`PointerFocused` state).
- `XYFocusUp`/`XYFocusDown`/`XYFocusLeft`/`XYFocusRight` used to bind navigation
  between the 3 columns when needed (e.g. focus transition between `Current` and `Preview`
  when switching "modes" — normally each column manages its own focus internally via
  `INavigable`, and `XYFocus` covers cases where the user uses the physical Xbox D-pad
  instead of the `GamepadInputService` logical flow. Define explicitly to avoid relying on
  system auto-heuristics, which may pick the wrong element in asymmetric layouts).
- `IsFocusEngaged` not used in MVP (reserved for a future "lock focus" mode inside
  a modal dialog, e.g. `FileActionSheet`).

## ResourceDictionary Structure

```
Theming/RetroTheme.xaml
├── Brushes                     (Background, Foreground, Selected, Accent, Border, ...)
├── Typography                  (FontFamily monospace, sizes by role: Title/Item/Meta)
├── Styles
│   ├── ColumnItemStyle         (file/folder row — Normal/PointerOver/Focused/Selected)
│   ├── ColumnHeaderStyle       (column header, e.g. current path)
│   ├── ContextMenuItemStyle    (FileActionSheet row)
│   └── StatusBarStyle          (footer with button hints — "A: Open  B: Back  Y: Menu")
```

## Runtime-Editable Theme (JSON)

`Theming/AppTheme.cs`:
1. Reads `x-files-theme.json` from `ApplicationData.Current.LocalFolder` (creates with
   defaults if it doesn't exist, on first run).
2. Parses via `System.Text.Json` with `JsonCommentHandling.Skip` (allows `//` comments in
   JSON, without needing manual stripping like `dosbox-pure-uwp` does with nlohmann/json).
3. Populates `Brush`/`FontFamily` in the `ResourceDictionary` at runtime (via
   `Application.Current.Resources["BrushName"] = new SolidColorBrush(...)`).

JSON schema example (same philosophy as `PUREMENU-THEMING.md` from dosbox-pure-uwp,
adapted):

```jsonc
{
  // Colors in #AARRGGBB or #RRGGBB format
  "background": "#0D0D0D",
  "foreground": "#E0E0E0",
  "accent": "#33AA55",
  "selectedBackground": "#1F3D2B",
  "border": "#333333",
  "fontFamily": "Consolas" // swap for custom font embedded in Assets/Fonts if desired
}
```

## Custom Font (Optional, Post-MVP)

If we want a "retro terminal" visual identity like `dosbox-pure-uwp` (VCR OSD Mono font),
embed `.ttf` in `Assets/Fonts/` and reference via
`FontFamily="/Assets/Fonts/FontName.ttf#Font Name"`. Not part of initial scaffold —
use `Consolas`/`Cascadia Mono` (already available on Windows) as default.

## Base Layout (3 Columns)

```xml
<Grid>
  <Grid.ColumnDefinitions>
    <ColumnDefinition Width="1*" />   <!-- Parent -->
    <ColumnDefinition Width="1.75*" /> <!-- Current -->
    <ColumnDefinition Width="2.25*" /> <!-- Preview -->
  </Grid.ColumnDefinitions>
  <!-- Controls/ColumnListView x3 (Parent, Current) + Controls/PreviewPane (Preview) -->
</Grid>
```
Proportions adjustable; values above are a reasonable starting point (yazi uses something
similar — parent smaller, current medium, preview larger).
