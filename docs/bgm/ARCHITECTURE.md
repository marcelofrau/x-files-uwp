# BGM — Architecture

## Components

```
SettingsPage ──► FolderBrowserDialog (File mode) ──► picks source path
      │                                                     │
      │  "Choose Music"                                     v
      │                                        BackgroundMusicService
      │                                        .SetTrackAsync(sourcePath)
      │                                                 │
      └──► XFilesSettings keys ◄───────────────────────┤  classify:
      (BgmEnabled/BgmFileName/BgmVolume)                 ├─ standard audio → copy as-is
                                                        └─ chiptune → RenderToWavAsync → copy WAV
                                                                     (spinner while rendering)
                                                                │
                                                                v
                                                    LocalState\BGM\bgm.<ext>
                                                                │
    App.xaml.cs ──► BackgroundMusicService.InitializeAsync() ──► plays loop
                                                                      │
    MillerColumnsPage.UpdateDisplayRequest() ──► Pause() / RequestResume(cooldown 10s)
```

## 1. `BackgroundMusicService` (`XFiles/Audio/BackgroundMusicService.cs`)

Singleton (mirrors `AudioLevelService.Instance` pattern). Owns a private
`AudioGraph` — completely separate from the media player's graph, so both can
play at the same time.

### State

| Field | Purpose |
|---|---|
| `_graph` | `AudioGraph` for the BGM only |
| `_fileNode` | `AudioFileInputNode` (the playing source) |
| `_deviceOut` | `AudioDeviceOutputNode` |
| `_gapTimer` / generation | 2-3s loop-gap wait |
| `_resumeTimer` / generation | 10s cooldown before auto-resume |
| `_volume` | 0-1 `OutgoingGain` (from `BgmVolume`) |
| `_isEnabled`, `_isPaused` | state flags |

### Playback model

- Source is always a file under `LocalState\BGM\` (`bgm.<ext>` for standard
  audio, `bgm.wav` for chiptune). After the initial copy nothing outside
  LocalState is read.
- `AudioFileInputNode.LoopCount = 1` + `FileCompleted` handler:
  - on completion, wait `LoopGapMs = 2500` (2-3s spec), then
    `Seek(TimeSpan.Zero)` + `Start()`.
  - generation guard: a `Pause()`/`Stop()`/new-track during the gap cancels
    the pending restart.
  - Note: `LoopCount = null` would loop seamlessly (no gap) — we deliberately
    use `1` + FileCompleted to produce the requested silence gap.
- Volume: `_fileNode.OutgoingGain = _volume`.

### API

| Method | Behavior |
|---|---|
| `InitializeAsync()` | Called once at launch. Reads settings; if enabled and `LocalState\BGM\` copy exists → `Play()`. Failure → disabled, logged. |
| `SetTrackAsync(sourcePath)` | Classify (`MusicFormatClassifier`). Standard → copy as-is. Chiptune → `RetroAudioPlayer.RenderToWavAsync(path, data, ext, 0)` then copy the rendered WAV. Delete old copy. Set `BgmEnabled=true`, persist `BgmFileName`, `Play()`. |
| `Pause()` | Stop playback immediately (cancels any pending gap-restart). State kept. |
| `RequestResume()` | Start/arm the 10s cooldown timer; on fire → `Resume()`. Re-arming resets the window. |
| `Resume()` | If enabled + track exists + not paused-by-user → `Play()`. |
| `Stop()` / `ClearTrack()` | Stop and optionally delete the LocalState copy + disable. |
| `SetVolume(float)` | Apply `OutgoingGain` immediately, persist `BgmVolume`. |
| `SourceName`, `IsPlaying`, `IsEnabled` | Read-only state for the Settings descriptions. |

### Dedup note

`RetroAudioPlayer.RenderToWavAsync` already dedups via the in-flight task
dictionary — re-picking the same chiptune reuses the cached WAV instantly.

## 2. File picker (`FolderBrowserDialog`, generic)

`FolderBrowserDialog` is a **generic** picker dialog: one control, two modes.
The BGM feature is its first File-mode consumer; any future "pick a file"
needs (e.g. a ROM or playlist picker) reuse it the same way.

- New `enum PickerMode { Folder, File }` (renamed from the original
  `FolderBrowserDialogMode` — the control is no longer folder-only).
- New overload `ShowAsync(string initialPath, PickerMode mode,
  IReadOnlyList<string> fileExtensions = null)`. The one-argument
  `ShowAsync(path)` overload still means Folder mode (Move uses it, unchanged).
- `fileExtensions` is the **generic extension filter**: dot-prefixed strings
  (`".mp3", ".psf", …`). `null` = list **all** files. BGM passes
  `MusicFormatClassifier.MusicExtensions` (standard audio ∪ chiptune).
- `File` mode changes in `LoadDirectory`:
  - `TitleText` = "Select file".
  - scan includes **files** too (not only directories), filtered by
    `fileExtensions` (or all files when the filter is null).
  - the "Move Here" virtual entry is **omitted** in File mode.
  - footer A label: "Navigate" for directories, "Select File" for a selected
    file.
  - item click / confirm: directory → navigate in; file → `Close(path)`.
- Folders (`..`, drives, subdirs) behave exactly as today.
- File entries get a best-effort icon: chiptune → `filetype-audio-x-generic`,
  known audio formats → `filetype-audio-*-24.png`, PDF → application-pdf,
  everything else → `file-generic-24.png`.
- Folder mode behavior (Move) is 100% unchanged.

### Gamepad routing

The picker instance lives on `SettingsPage` (`x:Name="BgmPickerControl"`).
`SettingsPage.HandleDPad` forwards to it while open (same pattern as
`AlertDialogControl` at line ~118): D-pad moves selection, A confirms, B
cancels. The existing `InputRouter` already delivers all gamepad keys to
`SettingsPageControl.HandleDPad` while Settings is open.

## 3. Settings UI (`SettingsPage`)

Three new menu items (order after "Clear Portal Credentials"):

| Item | Action | Description text |
|---|---|---|
| Background Music | `bgm-toggle` | `On: <BgmFileName>` / `Off` |
| Choose Music File | `bgm-pick` | opens picker (File mode) |
| BGM Volume | `bgm-volume` | `Current: 10%` … `100%` |

- `bgm-pick`: on confirm from the picker → show spinner overlay
  (`ProgressRing`, mirrored from the media player spinner) → call
  `SetTrackAsync` → hide spinner → refresh menu descriptions.
- `bgm-volume`: cycles the 5 levels, persists + applies.
- Menu refresh pattern mirrors the existing clear-cache/log-level actions.

## 4. Pause/resume hook (`MillerColumnsPage`)

Single choke point: `UpdateDisplayRequest()` already computes exactly
"is any media engaged":

```csharp
bool mediaEngaged = _isMediaPlayerActive
    || AudioFullScreenPanel.Visibility == Visibility.Visible
    || VideoFullScreenPanel.Visibility == Visibility.Visible;
```

- On `false → true` edge → `BackgroundMusicService.Instance.Pause()`.
- On `true → false` edge → `BackgroundMusicService.Instance.RequestResume()`
  (10s cooldown).
- `UpdateDisplayRequest` is reached from every media start/stop path
  (inline `PlayerStateChanged`, fullscreen open/close handlers), so no extra
  call sites are needed. A `_bgmWasMediaEngaged` previous-state field keeps the
  edges idempotent.

## 5. Startup (`App.xaml.cs`)

After the portal block, fire-and-forget:

```csharp
_ = BackgroundMusicService.Instance.InitializeAsync();
```

Runs on a background thread (via `Task.Run`) so it never delays first paint;
AudioGraph creation is marshaled to the UI thread internally when needed.

## 6. Pure logic + tests

- `MusicFormatClassifier` (`XFiles/FileSystem/MusicFormatClassifier.cs`) —
  pure static, no UWP types:
  - `StandardAudioExtensions` set, `ChiptuneExtensions` set (mirror of
    `RetroAudioPlayer`'s lists — kept in sync per project rule #9), 
  - `IsMusicFile(ext)`, `IsStandardAudio(ext)`, `IsChiptune(ext)`.
  - `PercentToGain(int)`/volume-level cycling helper.
- Linked into `tests/XFiles.Tests.csproj` (existing `<Compile Include=...>` +
  `Link=` pattern) and covered by xUnit tests.

## Risks / Xbox validation

1. **Two simultaneous AudioGraphs** on Xbox — each graph is independent, but
   the combination is unproven on hardware; must be tested with BGM +
   video/audio/chiptune.
2. **In-graph resample** — a 44100 Hz (or USF 22047 Hz) BGM hits the known
   44100≠48000 resample path on Xbox (same WRN as media); acceptable, but
   worth a listen for artifacts.
3. **Session lock during pick-time render** — the chiptune render holds the
   RetroAudio lock (one emulator per process, memory #113); Settings has no
   concurrent media playback, and the spinner covers the wait.
4. **Loop seam on MP3** — encoder delay/padding can click at the loop point;
   file-quality issue, not code. WAV/FLAC loop cleanly.
