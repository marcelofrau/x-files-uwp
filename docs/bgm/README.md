# Background Music (BGM)

Gamepad-friendly background music for the file browser. A user-chosen track
plays in a loop while browsing, stored inside the app's LocalState so no
external-drive spinup is needed. Configured from Settings: on/off toggle,
file picker, volume.

Status: **Implemented** (build 1.8.0.1288, 188 tests green) — pending desktop/Xbox validation. See `IMPLEMENTATION.md` for the checklist.

## Feature at a glance

- **Source**: any audio file (mp3/wav/flac/ogg/m4a/wma/aac) **or** any chiptune
  (PSF/USF/NSF/VGM/etc. via RetroAudio.dll).
- **Storage**: the chosen file is copied to `LocalState\BGM\`, so playback never
  touches the external drive again. Chiptunes are rendered to WAV once at pick
  time (spinner shown) and the WAV is what gets copied.
- **Loop**: infinite, with a 2-3s silence gap between iterations.
- **Coexistence with the media player**: opening a track/video in the player
  pauses the BGM; when the player closes, the BGM resumes after a 10s cooldown.
- **Settings keys** (SQLite via `XFilesSettings`):
  - `BgmEnabled` (bool)
  - `BgmFileName` (string — name of the copy in LocalState)
  - `BgmVolume` (int percent, 10/25/50/75/100)

## Docs

| Doc | Purpose |
|---|---|
| `SPEC.md` | Functional requirements, user stories, acceptance criteria |
| `ARCHITECTURE.md` | Service design, picker, settings wiring, lifecycle |
| `IMPLEMENTATION.md` | Step-by-step checklist with status (tracking) |

## Cross-cutting notes

- Follows the gamepad-first rule: every interactive control uses the existing
  custom templates (`RetroListViewStyle`, `BladeTheme`), no default Fluent chrome.
- The BGM uses its **own** `AudioGraph`, separate from `AudioLevelService`
  (the media player) — two graphs run simultaneously.
- All code/comments/commits in English.
