<p align="center">
  <img src="docs/social-preview.png" alt="X-Files" width="600" />
</p>

<h1 align="center">X-Files</h1>

<p align="center">
  <strong>Gamepad-first file browser for Xbox</strong><br/>
  Miller-column navigation (Parent | Current | Preview) inspired by
  <a href="https://github.com/sxyazi/yazi">yazi</a>, built natively in C#/XAML for UWP.
</p>

<p align="center">
  <a href="docs/SPEC.md">Spec</a> ·
  <a href="docs/ARCHITECTURE.md">Architecture</a> ·
  <a href="docs/ROADMAP.md">Roadmap</a> ·
  <a href="docs/DECISIONS.md">Decisions</a> ·
  <a href="docs/DEPLOY-XBOX.md">Deploy to Xbox</a>
</p>

---

## What is this?

X-Files is a file browser designed for **Xbox consoles**, fully operable via gamepad. It
uses a three-column Miller layout — just like [yazi](https://github.com/sxyazi/yazi) does
in the terminal — but reimplemented from scratch in C#/XAML for the UWP platform.

No code is shared with yazi (different language, different stack). The inspiration is
purely UX: the Parent → Current → Preview column model with live preview as you navigate.

## Current status

**Phase 4 complete** — three-column Miller navigation working on real Xbox hardware.

| What works | Status |
|---|---|
| Gamepad navigation (D-pad, sticks, all buttons) | Done |
| Directory scanning (P/Invoke, all local drives) | Done |
| Three-column Miller layout (Parent / Current / Preview) | Done |
| Live preview on selection change (debounced) | Done |
| LB/RB/LT/RT page navigation (±8 items) | Done |
| Drill in/out (A/Right = enter, B/Left = back) | Done |
| Retro theme (custom ControlTemplate, no Fluent chrome) | Done |
| Debug overlay (XrayLib, remote logging) | Done |
| Archive browsing (zip/7z/rar) | Phase 6 |
| File operations (copy/move/rename/delete/extract) | Phase 7 |
| Context menu (Y button) | Phase 7 |

## Controls

| Button | Action |
|---|---|
| D-pad / Left stick | Navigate up/down |
| D-pad Left / B | Go back (drill out) |
| D-pad Right / A | Enter folder (drill in) |
| LB / LT | Page up (−8 items) |
| RB / RT | Page down (+8 items) |
| Y | Context menu (WIP) |

## Inspirations

- **[yazi](https://github.com/sxyazi/yazi)** — the Miller-column UX model, live preview,
  keyboard-first philosophy adapted to gamepad-first
- **[dosbox-pure-uwp](../dosbox-pure-uwp)** — sibling project; patterns reused as
  documented references (P/Invoke directory scanning, gamepad input abstraction, manifest
  capabilities), not shared code
- **RetroArch UWP** — precedent for `FindFirstFileExFromAppW` P/Invoke on Xbox, where
  `StorageFolder` APIs fail with `AccessDenied` for arbitrary drive paths

## Getting started

### Prerequisites

- **Windows** with Visual Studio 2022 + "Universal Windows Platform development" workload
- Xbox with **Developer Mode** enabled (see [DEPLOY-XBOX.md](docs/DEPLOY-XBOX.md))

### Build

```powershell
MSBuild.exe "XFiles.sln" /p:Configuration=Debug /p:Platform=x64
```

### Deploy to Xbox

See [docs/DEPLOY-XBOX.md](docs/DEPLOY-XBOX.md) for sideload via Device Portal or Visual
Studio Remote Machine.

## Project structure

```
XFiles/
├── Controls/          # XAML views (MillerColumnsPage, ColumnListView, DebugOverlay)
├── Navigation/        # ColumnNavigator, GamepadInputService, INavigable
├── FileSystem/        # DirectoryScanner (P/Invoke), FileEntry model
├── Theming/           # RetroTheme.xaml, AppTheme (JSON-backed)
├── Assets/            # Icons, images
├── ContextMenu/       # Y-button action sheet (WIP)
└── App.xaml           # Entry point, theme merging
```

## Key docs

| Doc | What it covers |
|---|---|
| [SPEC.md](docs/SPEC.md) | Functional spec, MVP scope, done criteria |
| [ARCHITECTURE.md](docs/ARCHITECTURE.md) | Layered architecture, data flow, column model |
| [GAMEPAD.md](docs/GAMEPAD.md) | Button mapping, INavigable contract |
| [FILEBROWSER.md](docs/FILEBROWSER.md) | FileEntry model, DirectoryScanner, sorting |
| [ARCHIVES.md](docs/ARCHIVES.md) | zip/7z/rar via SharpCompress |
| [UI-THEMING.md](docs/UI-THEMING.md) | ControlTemplate conventions |
| [ROADMAP.md](docs/ROADMAP.md) | Phased plan with done criteria |
| [DECISIONS.md](docs/DECISIONS.md) | ADRs — why XAML, why SharpCompress, etc. |

## License

[GPL-3.0](LICENSE) — free software; you can redistribute it and/or modify it under the
terms of the GNU General Public License as published by the Free Software Foundation,
either version 3 of the License, or (at your option) any later version.
