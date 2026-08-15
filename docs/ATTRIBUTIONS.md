---
layout: default
title: Icon Attributions
---
# Icon Attributions

## File-Type Icons (File Explorer)

### Papirus Icon Theme

- **Source:** https://github.com/PapirusDevelopmentTeam/papirus-icon-theme
- **License:** CC-BY-SA 4.0
- **Copyright:** Papirus Development Team
- **Used for:** File-type icons in file explorer columns and preview pane
- **Local copy:** `F:\workspace\fileexplorer-icons-reference\papirus\`
- **How obtained:** `git clone --depth 1 https://github.com/PapirusDevelopmentTeam/papirus-icon-theme.git`

Full license text: https://creativecommons.org/licenses/by-sa/4.0/legalcode

## UI/Control Icons

### game-music-emu (console chiptune decode)

- **Source:** https://github.com/libgme/game-music-emu
- **License:** LGPL-2.1-or-later (static link OK — app is GPL-3.0)
- **Copyright:** game-music-emu contributors
- **Used for:** console chip formats inside `Native/RetroAudio` (NSF/SPC/GBS/VGM/SID/HES/KSS/AY/SAP)
- **How obtained:** vendored in `Native/third_party/game-music-emu-0.6.5`

### libopenmpt (tracker module decode)

- **Source:** https://github.com/OpenMPT/openmpt
- **License:** BSD-3-Clause
- **Copyright:** OpenMPT Project / libopenmpt contributors
- **Used for:** MOD-family tracker formats inside `Native/RetroAudio` (mod/xm/s3m/it + more)
- **How obtained:** vendored in `Native/third_party/libopenmpt-0.8.7`

### zlib / miniz (compressed chiptune inflate)

- **Source:** https://zlib.net / https://github.com/richgel999/miniz
- **License:** zlib / MIT
- **Used for:** `.vgz` (gzip VGM) and `.j2b` inflate in `Native/RetroAudio`
- **How obtained:** vendored in `Native/third_party/zlib-1.3.1`, `libopenmpt/include/miniz`

## Gamepad Button Icons

### Personal Icons8-derived set

- **Source:** `F:\workspace\icons8-personal-set\`
- **License:** See original Icons8 license terms
- **Used for:** UI controls, toolbar buttons, view-specific icons
- **Disk Space dialog + drive menu:** `icons8-hdd-48.png` → `Assets/Views/FileActionSheet/fileactionsheet-hdd-48.png`, `icons8-hdd-100.png` → `Assets/Views/DiskUsageDialog/diskusagedialog-hdd-100.png`
- **Settings page icons:** `icons8-hdd-48.png` → `Assets/Views/SettingsPage/settingspage-hide-drives-48.png`, `fluentui-back-arrow-48.png` → `Assets/Views/SettingsPage/settingspage-back-48.png` (bgm/volume/clear-credentials icons also derive from this set)

## Gamepad Button Icons

### XBOX BUTTONS - Premium Assets (Arks @Scissormarks)

- **Author:** Mikkel Julian "Arks" Petersen
- **Source:** https://arks.itch.io/xbox-buttons
- **License:** Premium asset pack — use with attribution
- **Credit:** "UI Elements" by "Mikkel Julian 'Arks' Petersen"
- **Social:** @ScissorMarks (Twitter), https://arks.itch.io
- **Used for:** Gamepad button legends, command hints, help overlays
- **Local copy:** `F:\workspace\assets\XBOX BUTTONS - Premium Assets\`
- **Converted to:** 64×64 PNGs in `XFiles/Assets/GamepadButtons/`

**Note:** Xbox and the Xbox logo are registered trademarks of Microsoft.

---

*Add new attributions here when adding icon sources.*
