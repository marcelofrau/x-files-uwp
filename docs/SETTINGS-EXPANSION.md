---
layout: default
title: Settings Expansion — Feature Plan
---
# Settings Expansion — Feature Plan

> **Status: planned / backlog.** Not yet implemented. Settings page currently has
> 2 items (`Clear Cache`, `Log Level`) — the doc below is the full expansion plan.
> Entry point: Start button → Start menu → Settings (`SettingsPage`).

## Overview

Expand the Settings page from 2 items (Clear Cache, Log Level) to a full
configurable preferences system. Also cleans up dead debug overlay code
that was disabled but never removed.

**Scope:** ~13 files touched, 3 deleted, 2 new. No architecture changes.
All settings use existing `XFilesSettings` + SQLite `AppSettingEntry` persistence.

---

## Part 1: Debug Overlay Cleanup (Tech Debt)

Remove dead code from the disabled debug overlay feature.

> **DONE (Aug 2026)** — `DebugOverlay.xaml(.cs)` and `ScreenLogger.cs` deleted;
> `Log.Screen` property, `WriteTo.Sink(Screen)` registration, and the commented
> instantiation in `App.xaml.cs` removed; csproj entries cleaned. Serilog File/Debug
> sinks remain.

### Files to Delete

| File | Reason |
|---|---|
| `Controls/DebugOverlay.xaml` | Disabled UI control (commented out in App.xaml.cs:128) |
| `Controls/DebugOverlay.xaml.cs` | Code-behind for above |
| `ScreenLogger.cs` | `ILogEventSink` — sole consumer was DebugOverlay; `LogsPage` reads log files, not this |

### Files to Clean

| File | Line(s) | Change |
|---|---|---|
| `App.xaml.cs` | 110 | Remove `// rootGrid.Children.Add(new DebugOverlay(Log.Screen));` |
| `Log.cs` | 46 | Remove `public static ScreenLogger Screen { get; private set; }` |
| `Log.cs` | 59 | Remove `Screen = new ScreenLogger();` |
| `Log.cs` | 64 | Remove `.WriteTo.Sink(Screen)` from Serilog pipeline |
| `XFiles.csproj` | 150-151 | Remove `<Compile Include="Controls\DebugOverlay.xaml.cs">` + `<DependentUpon>` |
| `XFiles.csproj` | 272 | Remove `<Page Include="Controls\DebugOverlay.xaml">` |

### Risk

Low. `ScreenLogger` is not used by `LogsPage` (which reads via `Log.GetAllLogContent()`
from file sinks). Removing it from the Serilog pipeline has no observable effect.

---

## Part 2: Theme Selector

### Current State

- Only `BladeTheme.xaml` exists (green accent)
- No second theme to switch to
- No runtime theme switching code
- No `AppTheme.cs` (planned in ROADMAP Phase 8, never implemented)

### Plan

#### A) Create `Theming/CosmicTheme.xaml`

Blue/purple variant. Same brush keys as BladeTheme, different colors:

| Brush | Blade (green) | Cosmic (blue) |
|---|---|---|
| `XFilesAccentBrush` | `#93C43C` | `#4A90D9` |
| `XFilesAccentDimBrush` | `#7AA832` | `#3A7BC8` |
| `XFilesAccentGlowBrush` | `#B0E050` | `#6AB0F0` |
| `XFilesAccentHoverDarkBrush` | `#4A6220` | `#2A5090` |
| `XFilesSelectedBackgroundBrush` | `#93C43C` | `#4A90D9` |
| `XFilesSelectedUnfocusedBrush` | `#1E2A12` | `#121E2A` |
| `XFilesHeaderBgBrush` | `#284325` | `#253843` |
| `XFilesSidebarBgBrush` | `#0F1A0F` | `#0F141A` |
| `XFilesInputBorderBrush` | `#4A6220` | `#2A5090` |
| `XFilesInputBorderHoverBrush` | `#7AA832` | `#3A7BC8` |
| Title gradient | green | blue |

All other brushes (background, surface, text, border, danger, success, warning)
stay identical — only accent/header/sidebar/selection colors change.

#### B) Create `Theming/ThemeManager.cs`

```csharp
namespace XFiles.Theming
{
    public static class ThemeManager
    {
        public static readonly string[] AvailableThemes = { "Blade", "Cosmic" };

        public static async Task ApplyThemeAsync(string themeName)
        {
            string uri = $"ms-appx:///Theming/{themeName}Theme.xaml";
            var dict = new ResourceDictionary { Source = new Uri(uri) };

            var md = Application.Current.Resources.MergedDictionaries;
            if (md.Count > 0) md[0] = dict;
            else md.Insert(0, dict);

            await XFilesSettings.SetStringAsync("Theme", themeName);
        }

        public static async Task<string> GetCurrentThemeAsync()
            => await XFilesSettings.GetStringAsync("Theme", "Blade");
    }
}
```

#### C) Startup Integration — `App.xaml.cs`

After settings load in `OnLaunched`, before window activation:

```csharp
string theme = await XFilesSettings.GetStringAsync("Theme", "Blade");
await ThemeManager.ApplyThemeAsync(theme);
```

#### D) Settings UI — `SettingsPage.xaml.cs`

New menu item:

```csharp
new SettingsMenuItem
{
    Label = "Theme",
    Description = $"Current: {currentTheme}",
    IconPath = IconBase + "startmenu-settings-48.png",
    Action = "theme"
}
```

Action handler cycles through `ThemeManager.AvailableThemes`,
calls `ThemeManager.ApplyThemeAsync()`, persists, and refreshes the item description.

### Files Touched

| File | Change |
|---|---|
| `Theming/CosmicTheme.xaml` | **New** — blue/purple theme |
| `Theming/ThemeManager.cs` | **New** — runtime theme switcher |
| `Settings/XFilesSettings.cs` | +`GetStringAsync`/`SetStringAsync` for "Theme" key |
| `App.xaml.cs` | Load theme at startup |
| `Controls/SettingsPage.xaml.cs` | +1 menu item + action handler |

---

## Part 3: Easy-Win Settings

Expose hardcoded constants that already exist in code. Minimal implementation —
just replace `const` with settings call.

### 3A) Add `GetDoubleAsync`/`SetDoubleAsync` to `XFilesSettings.cs`

Needed for dead zone (float) values:

```csharp
public static async Task<double> GetDoubleAsync(string key, double defaultValue = 0.0)
{
    string val = await MetadataCache.GetSettingAsync(key, null);
    if (val == null) return defaultValue;
    return double.TryParse(val, System.Globalization.NumberStyles.Float,
        System.Globalization.CultureInfo.InvariantCulture, out double result)
        ? result : defaultValue;
}
```

### 3B) Gamepad Dead Zone

**Source:** `Navigation/GamepadInputService.cs`

| Constant | Line | Current Value | Setting Key |
|---|---|---|---|
| `Deadzone` | 15 | `0.5` | `GamepadMainDeadzone` |
| `StickDeadzone` | 317 | `0.18` | `GamepadStickDeadzone` |
| `ScrollDeadzone` | 386 | `0.15` | `GamepadScrollDeadzone` |

**Change:** Load from `XFilesSettings.GetDoubleAsync()` at construction time.
Constants become fallback defaults.

**Settings UI:** Single item "Stick Sensitivity" cycling through presets:

| Preset | Main | Stick | Scroll |
|---|---|---|---|
| Tight | 0.4 | 0.12 | 0.10 |
| Normal (default) | 0.5 | 0.18 | 0.15 |
| Loose | 0.6 | 0.25 | 0.20 |

### 3C) D-Pad Repeat Timing

**Source:** `Navigation/GamepadInputService.cs:315-316`

| Constant | Current Value | Setting Key |
|---|---|---|
| `DpadInitialDelay` | `300` ms | `DpadInitialDelay` |
| `DpadRepeatInterval` | `80` ms | `DpadRepeatInterval` |

**Settings UI:** Single item "D-Pad Speed" cycling through presets:

| Preset | Initial (ms) | Repeat (ms) |
|---|---|---|
| Slow | 400 | 120 |
| Normal (default) | 300 | 80 |
| Fast | 200 | 50 |

### 3D) Text Editor Tab Size

**Source:** `Controls/TextEditorOverlay.xaml.cs:747`

Hardcoded CSS: `tab-size:4; -moz-tab-size:4;`

**Change:** Interpolate from `XFilesSettings.GetIntAsync("EditorTabSize", 4)`:

```csharp
int tabSize = await XFilesSettings.GetIntAsync("EditorTabSize", 4);
// In CSS template:
$"tab-size:{tabSize}; -moz-tab-size:{tabSize};"
```

**Settings UI:** Cycle: `2 → 4 → 8 → 2`

### Files Touched

| File | Change |
|---|---|
| `Settings/XFilesSettings.cs` | +`GetDoubleAsync`, +`SetDoubleAsync`, +convenience properties |
| `Navigation/GamepadInputService.cs` | Load 3 dead zones + 2 repeat timings from settings |
| `Controls/TextEditorOverlay.xaml.cs` | Interpolate tab-size from settings |
| `Controls/SettingsPage.xaml.cs` | +3 menu items + action handlers |

---

## Part 4: Settings Page UX

### Current State

Flat list of 2 items. No sections. Works fine for 2 items.

### With 6+ Items Needs Grouping

Add `Section` property to `SettingsMenuItem`. Section headers rendered as
non-selectable `TextBlock` items in the ListView.

```
── General ──
  Theme           Current: Blade
  Log Level       Current: Info

── Input ──
  Stick Sensitivity   Normal
  D-Pad Speed         Normal

── Editor ──
  Tab Size            4

── Maintenance ──
  Clear Cache         247 cached entries
```

Navigation: D-pad Up/Down moves between ALL items (skipping section headers
automatically via `SelectedIndex` logic). Section headers are visual only.

### Files Touched

| File | Change |
|---|---|
| `Controls/SettingsPage.xaml` | Section header template (TextBlock in DataTemplate) |
| `Controls/SettingsPage.xaml.cs` | +`Section` property, build grouped item list |

---

## Implementation Order

1. **Debug overlay cleanup** — pure deletion, no dependencies, quick win
2. **Theme selector** — most visible user-facing change
3. **Easy-win settings** — mechanical constant→settings replacements
4. **Settings page UX** — section grouping (do last, after all items exist)

Each step is independently mergeable. No step depends on another.

---

## Settings Key Registry

Centralized reference for all setting keys, types, and defaults:

| Key | Type | Default | Consumer |
|---|---|---|---|
| `Theme` | string | `"Blade"` | ThemeManager |
| `LogLevel` | string | `"Info"` | Log.cs (existing) |
| `GamepadMainDeadzone` | double | `0.5` | GamepadInputService |
| `GamepadStickDeadzone` | double | `0.18` | GamepadInputService |
| `GamepadScrollDeadzone` | double | `0.15` | GamepadInputService |
| `DpadInitialDelay` | int | `300` | GamepadInputService |
| `DpadRepeatInterval` | int | `80` | GamepadInputService |
| `EditorTabSize` | int | `4` | TextEditorOverlay |
| `FirstRunShown` | bool | `false` | (existing, unused) |

---

## Not In Scope (Future Consideration)

These items were audited but deferred — they need moderate refactor
(XAML data binding, HTML string interpolation) and aren't worth it yet:

- Column width ratios (XAML star values → binding)
- Font sizes (XAML + HTML → resource binding)
- Archive extension unification (4 inconsistent locations)
- Hardcoded colors in code-behind (should use `StaticResource` lookups)
- Animation duration settings (too many values, low user value)
- Poll interval (33ms — changing has perf implications)
- Stick sensitivity/speed (9 values across 6 files — overkill for now)
