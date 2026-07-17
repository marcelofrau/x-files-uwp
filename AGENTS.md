# AGENTS.md — X-Files

## Project
Gamepad-first file browser for Xbox (UWP), inspired by yazi's Miller-column UX (Parent |
Current | Preview, live preview), but implemented natively in C#/XAML — no code/core reuse
from yazi (Rust, terminal-based, incompatible tech stack).

Sibling project: `../dosbox-pure-uwp` — some infra patterns (P/Invoke directory scanning,
gamepad input abstraction, manifest capabilities) are intentionally reused as documented
patterns, not shared code (different language, no shared dependency).

## Language
All documentation, code, comments, and commit messages MUST be in English.
(User may converse in Portuguese or English; agent responds accordingly.)

## Status
**Scaffold phase.** No feature logic implemented yet. See `docs/ROADMAP.md` for phased
plan. Do not skip ahead to later phases without completing/documenting earlier ones.

## Critical Rules
- **NEVER commit or push** without explicit user request. Stage changes only. Wait for
  "commit", "push", "faz o commit", etc.
- **This project cannot be built or run in this (Linux) environment.** UWP requires
  Visual Studio on Windows. Treat all `.csproj`/`.xaml`/manifest edits as unverified until
  built on a Windows machine — flag this clearly whenever making structural changes.
- **x64 target primarily** (Xbox Series). Confirm ARM64 needs before adding that platform.
- **`broadFileSystemAccess` + `runFullTrust`** capabilities are required in the manifest
  for any filesystem code outside the app's sandboxed folders (see `docs/FILEBROWSER.md`
  and `docs/DEPLOY-XBOX.md`). Do not "simplify" file access to `StorageFolder` APIs without
  checking this — it breaks browsing external drives on Xbox.
- **No XAML controls with default Fluent Design chrome** — every interactive control must
  use a custom `ControlTemplate`/`Style` from `Theming/RetroTheme.xaml` (see
  `docs/UI-THEMING.md`, ADR-002 in `docs/DECISIONS.md`). Gamepad focus
  (`XYFocusUp/Down/Left/Right`, `IsFocusEngaged`) must still work — that's the whole reason
  XAML was chosen over Win2D.
- **Read `docs/DECISIONS.md` before proposing architecture changes** — several trade-offs
  (XAML vs Win2D, SharpCompress vs native 7z libs, no network browsing in MVP) were already
  debated and decided; don't re-litigate without new information.
- **Log everything. Every operation, every action, every exception must be logged.** Use
  the central `Log` static class (Serilog). Default level is `Verbose`/`Trace`. Never
  swallow exceptions — always log them. Log directory scans, input events, navigation,
  file operations, app lifecycle. Logs rotate daily, keeping last 5 files, stored in
  `ApplicationData.Current.LocalFolder/logs/`. See `docs/LOGGING.md`.

## Architecture at a Glance
See `docs/ARCHITECTURE.md` for the full picture. Layers (top → bottom):
XAML Views → ViewModels → Navigation (`INavigable`, `ColumnNavigator`) →
`GamepadInputService` → FileSystem (`DirectoryScanner`, `ArchiveBrowser`,
`FileOperations`).

## Key Docs

| Doc | Purpose |
|---|---|
| `docs/SPEC.md` | Functional spec, MVP scope, done criteria |
| `docs/ARCHITECTURE.md` | Layered architecture, data flow, column model |
| `docs/GAMEPAD.md` | Button mapping, `INavigable` contract, navigation rules |
| `docs/FILEBROWSER.md` | `FileEntry` model, `DirectoryScanner` (P/Invoke), sorting |
| `docs/ARCHIVES.md` | zip/7z/rar via SharpCompress, archive-as-virtual-folder |
| `docs/UI-THEMING.md` | ControlTemplate conventions, theme JSON schema |
| `docs/DEPLOY-XBOX.md` | Developer Mode, Device Portal, sideload steps |
| `docs/ROADMAP.md` | Phased implementation plan with done criteria per phase |
| `docs/ASSETS-GUIDE.md` | Asset naming, directory structure, personal icon set workflow |
| `docs/DECISIONS.md` | ADRs — why XAML, why not yazi-core, why SharpCompress, etc. |

## Planned Key Files (not all exist yet — see ROADMAP phases)

| File | Purpose |
|---|---|
| `XFiles/Navigation/INavigable.cs` | Semantic navigation contract (OnDPad/OnConfirm/OnBack/...) |
| `XFiles/Navigation/ColumnNavigator.cs` | 3-column state machine, drill-in/out |
| `XFiles/Navigation/GamepadInputService.cs` | `Windows.Gaming.Input.Gamepad` polling, edge-detection |
| `XFiles/FileSystem/DirectoryScanner.cs` | P/Invoke `FindFirstFileExFromAppW` + `GetLogicalDrives` |
| `XFiles/FileSystem/ArchiveBrowser.cs` | SharpCompress-based zip/7z/rar virtual folder |
| `XFiles/FileSystem/FileOperations.cs` | Copy/Move/Rename/Delete/Extract |
| `XFiles/ContextMenu/FileActionSheet.xaml` | Y-button context menu |
| `XFiles/Theming/AppTheme.cs` | JSON-backed theme loader |
| `XFiles/Theming/RetroTheme.xaml` | Custom ControlTemplate/Style resource dictionary |

## Build (once on Windows)
```powershell
MSBuild.exe "XFiles.sln" /p:Configuration=Debug /p:Platform=x64
```
Deploy to Xbox: see `docs/DEPLOY-XBOX.md`.

## Known Pitfalls (anticipated, from sibling project's experience)
1. **StorageFolder APIs will silently fail or throw AccessDenied** for arbitrary drive
   paths on Xbox — must use `FindFirstFileExFromAppW` P/Invoke instead (confirmed pattern
   from `dosbox-pure-uwp` + RetroArch UWP precedent).
2. **Gamepad connected before app start** does not fire `GamepadAdded` — must also
   enumerate `Gamepad.Gamepads` on startup.
3. **DPI awareness**: if any custom-drawn element is added later (e.g. Win2D for a
   thumbnail renderer), always read DPI from the render target, never hardcode 96.
4. **Async from UI thread**: don't block on `Task.Result`/`.Wait()` for any
   Windows Runtime async call — always `await`.
