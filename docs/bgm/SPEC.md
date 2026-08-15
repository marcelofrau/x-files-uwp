---
layout: default
title: BGM — Functional Specification
---
# BGM — Functional Specification

## Overview

Background music that plays in a loop while the user browses the file system.
The track is chosen once in Settings, copied into the app's LocalState, and
played back by a dedicated AudioGraph — so after the initial copy no external
drive access ever happens for the BGM.

## User stories

### US1 — Enable/disable
As a user I can turn the background music on and off from Settings without
losing my chosen track.

- Settings item "Background Music" shows the current state
  (`On: <file>` / `Off`).
- Pressing A toggles the state.
- The toggle persists across app restarts.

### US2 — Choose the track
As a user I can pick any music file with a gamepad-friendly file browser.

- Picker looks and behaves like the Move destination picker (Miller columns,
  gamepad navigation, A = confirm, B = back/cancel).
- Directory navigation works (including `..` and the drives root).
- Only music files are selectable:
  - standard audio: `.mp3 .wav .flac .ogg .m4a .wma .aac`
  - chiptune: all `RetroAudioPlayer.ChiptuneExtensions`
    (`.psf .minipsf .usf .miniusf .spc .gbs .nsf .nsfe .vgm .vgz .gym .sid
    .hes .kss .ay .sap` + libopenmpt tracker formats `.mod .xm .s3m .it ...`)
- On confirm the source is copied to `LocalState\BGM\`:
  - standard audio → copied as-is (original extension).
  - chiptune → rendered to WAV via `RetroAudioPlayer.RenderToWavAsync`, the
    WAV copied as `bgm.wav`. A spinner is shown during the render
    (1-16s depending on format/track length).
- Selecting a new track replaces the previous copy (old file deleted).
- The pick action enables BGM automatically.

### US3 — Loop with gap
As a user I hear the track repeat indefinitely with a short silence between
repetitions.

- Loop is infinite.
- There is a 2-3s silence gap between the end of one iteration and the start
  of the next.

### US4 — Volume
As a user I can adjust the BGM volume from Settings.

- Settings item "BGM Volume" shows the current level.
- Pressing A cycles `10% → 25% → 50% → 75% → 100%` (wraps).
- Volume persists and applies immediately (including mid-play).

### US5 — Coexistence with the media player
As a user I expect the BGM to step aside while I play a track or video.

- Opening any media playback (inline player, fullscreen audio, fullscreen
  video) pauses the BGM.
- When all playback stops, the BGM resumes after a **10s cooldown**.
- A new playback start during the cooldown cancels/re-arms the resume timer
  (BGM stays paused while media activity keeps happening).
- The BGM state (enabled/disabled, chosen file) is untouched by media playback.

### US6 — Persistence / startup
As a user I expect the BGM to resume automatically at app launch.

- On launch the service reads `BgmEnabled` + `BgmFileName`.
- If enabled and the LocalState copy exists, playback starts (looping,
  with the configured volume), after a short startup delay so it never blocks
  first paint.
- If the file is missing (LocalState wiped), the setting is treated as off.

## Non-functional requirements

- **Gamepad-first**: all new UI is navigable by D-pad + A/B; no default Fluent
  chrome (BladeTheme templates only).
- **Two AudioGraphs**: BGM graph runs concurrently with the media player graph.
  Needs Xbox validation (Xbox audio device is 48000 Hz; a 44100 Hz BGM triggers
  the same in-graph resample warning as media — acceptable).
- **No external I/O at playback time**: after the initial copy, playback reads
  only `LocalState\BGM\`.
- **One-shot chiptune render**: the pick-time render holds the RetroAudio
  session lock (one emulator live per process); media playback is not
  simultaneous with Settings, so no conflict. After the render, BGM plays the
  WAV and never touches the lock again.
- **Logging**: every state transition and failure goes through the central
  `Log` class (prefix `BackgroundMusic.`).

## Acceptance criteria

1. From Settings I can enable/disable BGM; the state survives restart.
2. I can pick an mp3 from an external drive; it plays in a loop with a 2-3s gap;
   the external drive can then be disconnected and the music keeps playing.
3. I can pick a chiptune (e.g. `.psf`); a spinner shows during the render;
   afterwards it loops like any other track.
4. Volume cycles 10/25/50/75/100 and is audible/applied immediately.
5. Opening a video pauses the BGM; closing it resumes the BGM after ~10s.
6. BGM autoplays at launch when enabled, without delaying first paint.
7. No media-player regression: mp3/video/chiptune playback behaves exactly as
   before the feature (verified on Xbox).
