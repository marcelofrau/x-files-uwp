# Architecture — X-Files

## Overview

X-Files is a gamepad-oriented UWP file browser, inspired by yazi's Miller column UX
(live preview, 3 columns), but implemented natively in C#/XAML to run well on Xbox
(native UWP gamepad focus), without reusing any code from yazi (see `DECISIONS.md`,
ADR-003).

## Layers

```
┌─────────────────────────────────────────────────────────────┐
│  XAML Views (MainPage, Controls/ColumnListView, PreviewPane) │  ← binding, templates, visual focus
├─────────────────────────────────────────────────────────────┤
│  ViewModels (ColumnViewModel, PreviewViewModel)               │  ← observable state, commands
├─────────────────────────────────────────────────────────────┤
│  Navigation (INavigable, ColumnNavigator)                     │  ← pure navigation logic, no UI
├─────────────────────────────────────────────────────────────┤
│  Input (GamepadInputService)                                  │  ← polling Windows.Gaming.Input, edge-detect
├─────────────────────────────────────────────────────────────┤
│  FileSystem (DirectoryScanner, ArchiveBrowser, FileOperations) │  ← disk access, P/Invoke, SharpCompress
└─────────────────────────────────────────────────────────────┘
```

Each layer only knows the layer directly below it. `Navigation` knows nothing about XAML;
`FileSystem` knows nothing about gamepad. This allows testing `ColumnNavigator` and
`DirectoryScanner` without UI (pure unit tests).

## Input → Screen Flow

```
Windows.Gaming.Input.Gamepad.GetCurrentReading()
        │  (every tick / CompositionTarget.Rendering or DispatcherTimer)
        ▼
GamepadInputService
  - compares current bitmask vs previous → detects "JustPressed"
  - dpad held → repeat-after-delay (same as dosbox-pure-uwp)
        │  semantic events: DPadUp, DPadDown, Confirm, Back, ContextMenu, PageUp, PageDown
        ▼
INavigable (implemented by ColumnNavigator)
  - OnDPad(bool up)
  - OnConfirm()
  - OnBack()
  - OnContextMenu()
  - OnPageUp() / OnPageDown()
        │  updates state (selected index, column stack)
        ▼
ColumnViewModel / PreviewViewModel (INotifyPropertyChanged)
        │  data binding (x:Bind)
        ▼
XAML re-renders (ItemsControl with custom ControlTemplate)
```

## Miller Column Model

3 `ItemsControl` side by side in a 3-column `Grid`:

| Column | Content | Width |
|---|---|---|
| Parent | parent directory listing, with "current" item highlighted | ~20% |
| Current | current directory listing, with active selection (gamepad focus) | ~35% |
| Preview | content of the selected item in the Current column | ~45% |

When pressing **A** on a folder:
1. `Current` becomes `Parent` (visually slides left — can be a simple
   `Storyboard` translation, or just content swap without animation in the MVP).
2. `Preview` becomes `Current`.
3. New `Preview` column is loaded with the content of the item now selected in the new
   `Current`.

When pressing **B** (or D-pad left): reverse process.

## Live Preview (no explicit action)

Moving the selection in the `Current` column immediately triggers:
- Folder → lists its children (same listing component, no interaction)
- Text file → first N lines / KB (truncated, with "..." indicator if larger)
- Image → thumbnail via `BitmapImage` (async decode, simple in-memory cache)
- `.zip`/`.7z`/`.rar` → internal entry listing via `ArchiveBrowser` (same listing UI,
  treated as "virtual folder")
- Unknown binary → "no preview available" message (no hex dump in MVP —
  in backlog, see `ROADMAP.md`)

## Why Not D2D (recapping ADR-002)

Gamepad focus (`XYFocusUp/Down/Left/Right`, `IsFocusEngaged`) is native to XAML/UWP.
Implementing in D2D would mean manually recreating: hit-test, scroll-follow-selection,
wrap-around, long-text marquee — everything that `dosbox-pure-uwp` had to build by hand
(see `Content/FileBrowser.cpp`, ~900 lines, in that repo). With XAML + custom
`ControlTemplate`, we get the same "not-stock-Windows" look with a fraction of the code.

## Theme/Config Persistence

`Theming/AppTheme.cs` loads an editable JSON (`x-files-theme.json`, saved in
`ApplicationData.Current.LocalFolder`) via `System.Text.Json` with
`JsonCommentHandling.Skip` (allows `//` comments in JSON, same convention as
`dosbox-pure-uwp`, but without needing manual stripping). The JSON populates a
`ResourceDictionary` at runtime (brushes/fonts used by `ControlTemplate`).
