# Audio Visualization — VU Meter (Spectrum Analyzer)

## Overview

Real-time spectrum analyzer visualization for the built-in audio player, inspired by
Winamp's classic VU meter. 26 bars × 12 segments each, with peak hold indicators and
smooth ballistic decay. Works on both local and external USB drives on Xbox, including
a stream-based fallback when `StorageFile` APIs are unavailable.

## Architecture

```
AudioLevelService (single engine: playback + analysis)
  │
  ├── Path A: StorageFile (when accessible)
  │   ├── AudioGraph + AudioFileInputNode (decodes via MediaFoundation)
  │   └── Works on: app sandbox, drives where StorageFile APIs succeed
  │
  └── Path B: Stream fallback (when StorageFile fails — Xbox external drives)
      ├── FileStream → IRandomAccessStream → MediaSource.CreateFromStream()
      ├── AudioGraph + MediaSourceAudioInputNode (decodes via same MediaFoundation)
      └── Works on: ANY drive accessible by managed FileStream (proven by ID3 reading)
  │
  ├── AudioDeviceOutputNode (always created — provides hardware clock + audio output)
  ├── AudioFrameOutputNode → GetFrame() → raw PCM float buffer (real-time)
  ├── FFT (Cooley-Tukey, 1024-point) → frequency bins
  ├── Band mapper → 26 logarithmic frequency bands
  ├── Ballistic: decay 0.85f, peak hold 1.5s, peak decay 0.92f
  ├── Sensitivity: power curve exponent 1.6 (compresses loud levels)
  └── Exposes: float[] BandLevels[26], float[] BandPeaks[26]

VuMeterBar (UserControl)
  ├── DispatcherTimer @ 60fps reads from AudioLevelService
  ├── 26 bars × 12 segments (Rectangle elements per bar)
  ├── Colors: green (#93C43C) → yellow (#E0C040) → red (#E04040)
  ├── Segment height: 5px, gap: 1px uniform
  ├── Peak indicator: thin white line at peak position
  └── Layout: Grid with fixed 10px columns, HorizontalAlignment="Center"

Integration:
  ├── Preview pane (MediaPreviewControl) — small VuMeterBar, playback via AudioLevelService
  ├── Fullscreen audio (MillerColumnsPage) — large VuMeterBar, playback via same AudioLevelService
  └── No MediaPlayer for audio — AudioGraph handles everything
```

## Key APIs

| API | Namespace | Purpose |
|---|---|---|
| `AudioGraph` | `Windows.Media.Audio` | Node-based audio processing pipeline |
| `AudioGraphSettings` | `Windows.Media.Audio` | Configuration (quantum size, encoding) |
| `AudioFileInputNode` | `Windows.Media.Audio` | Reads audio file (StorageFile path) |
| `MediaSourceAudioInputNode` | `Windows.Media.Audio` | Reads audio from MediaSource (stream path) |
| `AudioDeviceOutputNode` | `Windows.Media.Audio` | Hardware clock + speaker output |
| `AudioFrameOutputNode` | `Windows.Media.Audio` | Taps raw PCM frames for FFT |
| `AudioFrame` | `Windows.Media` | Container for PCM data |
| `IMemoryBufferByteAccess` | COM interop | Access raw bytes in AudioFrame buffer |
| `MediaSource` | `Windows.Media.Core` | Creates audio source from stream |
| `FileStream` | `System.IO` | Managed file I/O (bypasses StorageFile restrictions) |

## Xbox Storage Fallback

On Xbox, `StorageFile.GetFileFromPathAsync()` and `StorageFolder.GetFolderFromPathAsync()`
fail with `E_ACCESSDENIED` for files on external USB drives, even with
`broadFileSystemAccess` declared in the manifest.

**Solution**: `MediaSourceAudioInputNode` — the bridge between streams and AudioGraph.

```csharp
// 1. Open file via managed I/O (works on all drives)
var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
var stream = fileStream.AsRandomAccessStream();

// 2. Create MediaSource from stream (same API used by MediaPlayer)
var mediaSource = MediaSource.CreateFromStream(stream, "audio/mpeg");

// 3. Create AudioGraph node from MediaSource
var nodeResult = await audioGraph.CreateMediaSourceAudioInputNodeAsync(mediaSource);

// 4. Connect to device output (speakers) + frame output (FFT)
nodeResult.Node.AddOutgoingConnection(deviceOutputNode);  // playback
nodeResult.Node.AddOutgoingConnection(frameOutputNode);   // VU meter
```

Both paths share the same AudioGraph, AudioDeviceOutputNode, and AudioFrameOutputNode.
VU meter works identically regardless of which storage path is used.

## Frequency Band Mapping

26 bars representing logarithmically-spaced frequency bands from ~40Hz to ~20kHz:

| Bar | Approx Center (Hz) | Character |
|---|---|---|
| 1–4 | 40–160 | Sub-bass, bass |
| 5–8 | 250–1000 | Low-mid, presence |
| 9–13 | 1600–10000 | Mid, treble |
| 14–18 | 12500–18000 | Upper treble |
| 19–23 | 19000–20000 | Air, near-inaudible |
| 24–26 | 20000+ | Ultrasonic (visual reference only) |

Upper bands get progressively narrower, matching human auditory perception.

## Ballistics

| Parameter | Value | Notes |
|---|---|---|
| Rise time | ~10ms (instant) | Bar jumps to new level immediately |
| Fall/decay | `*= 0.85f` per quantum | Exponential, ~300ms to settle |
| Peak hold | 1500ms | Thin white line holds at max |
| Peak decay | `*= 0.92f` after hold | Smooth drop after hold expires |
| Sensitivity | `pow(normalized, 1.6)` | Compresses loud levels, prevents red saturation |
| Quantum size | 10ms (480 samples @ 48kHz) | Default AudioGraph quantum |

## FFT Configuration

| Parameter | Value |
|---|---|
| Algorithm | Cooley-Tukey radix-2 DIT |
| Window size | 1024 samples |
| Window function | Hamming |
| Overlap | None (each quantum is independent) |
| Output bins | 512 (N/2) |
| Frequency resolution | ~46.9 Hz/bin @ 48kHz |
| Amplitude scaling | dB (20*log10), power-curved to 0.0–1.0 |

## Performance

1. **Zero allocations in hot path.** FFT input/output buffers and band arrays are
   pre-allocated in the service. Callbacks only write to existing arrays.

2. **UI decoupled from audio thread.** `AudioLevelService` computes levels on the
   AudioGraph thread. `VuMeterBar` reads them via 60fps `DispatcherTimer`. No
   cross-thread mutation — only atomic float reads.

3. **`unsafe` for buffer access.** `IMemoryBufferByteAccess` COM interface required
   to read raw bytes from `AudioFrame`. Standard UWP pattern —
   `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` in csproj.

4. **XAML element count.** 26 bars × 12 segments = 312 Rectangles + 26 peak indicators.
   Minimal overhead. Direct `Fill` manipulation (no data binding) for 60fps.

5. **SemaphoreSlim concurrency guard.** Prevents race conditions when multiple
   `LoadAndPlay`/`StartAnalysis` calls overlap. Queues the latest request.

## Files

| File | Purpose |
|---|---|
| `XFiles/Audio/IMemoryBufferAccess.cs` | COM interface for PCM buffer access |
| `XFiles/Audio/AudioLevelService.cs` | AudioGraph + FFT + band calculation + stream fallback |
| `XFiles/Audio/FftHelper.cs` | Cooley-Tukey FFT implementation |
| `XFiles/Controls/VuMeterBar.xaml` | XAML layout (Grid-based, 26 columns) |
| `XFiles/Controls/VuMeterBar.xaml.cs` | Timer-driven visual update logic |

## Integration

### Preview Pane (MediaPreviewControl)

- `AudioLevelService` created once per control instance
- `LoadFile()` loads metadata only (ID3 tags), no playback started
- `TogglePlayPause()` creates AudioGraph on first press via `LoadAndPlay()`
- VuMeterBar attached via `AttachService()`, detached on stop

### Fullscreen Audio (MillerColumnsPage)

- Single `AudioLevelService` instance for both playback + VU meter
- `OpenAudioFullscreen()` creates service, calls `LoadAndPlay()`, seeks to position
- `NavigateAudioTrack()` stops current, creates new service for next track
- `ToggleAudioFullscreenPlayPause()` delegates to service
- `CloseAudioFullscreen()` stops + disposes service
- Volume via `SetVolume()` → `AudioDeviceOutputNode.OutgoingGain`

## Gotchas

1. **No audio device = AudioGraph creation fails.** Xbox always has audio output,
   but handle gracefully. VU meter shows no bars.

2. **`IMemoryBufferByteAccess` requires `unsafe`.** Must set
   `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` in `.csproj`.

3. **Stereo interleaving.** Raw buffer is `[L0, R0, L1, R1, ...]`. We take max of
   both channels per sample pair for amplitude.

4. **Thread safety.** `BandLevels`/`BandPeaks` written by audio thread, read by UI
   thread. Floats are atomically written on x86/x64 — no torn reads.

5. **`MediaSourceAudioInputNode` has no `MediaEnded` event.** Unlike
   `AudioFileInputNode.FileCompleted`, the stream-based node doesn't fire completion
   events. Completion detection for stream path is deferred.

6. **Power curve sensitivity.** Exponent 1.6 compresses loud levels so bars don't
   saturate at red on every track. Lower exponent = more sensitive, higher = less.

## Failed Approaches

### NAudio — Incompatible with UWP

NAudio transitively depends on `NAudio.WinForms` (Windows Forms). No UAP TFM exists
for any version. NuGet restore fails with NU1202. Do not use.

### Pre-decode Buffer Approach

AudioGraph file node decodes the entire file into memory on Xbox. Produces hundreds
of millions of samples from a 3-minute MP3. Buffer never stops growing.
Use real-time mode only — one frame per quantum.

### AudioGraph with `CreateFileInputNodeAsync` Only

On Xbox external drives, `StorageFile.GetFileFromPathAsync()` fails with
`E_ACCESSDENIED` even with `broadFileSystemAccess`. `StorageFolder.GetFolderFromPathAsync()`
also fails. Only `FileStream` (managed I/O) works. Solution: `MediaSourceAudioInputNode`
via `CreateMediaSourceAudioInputNodeAsync(MediaSource)`.

### AudioGraph APIs That Don't Exist on UWP

- `AudioEncodingProperties.CreateFloat()` — does not exist
- `AudioFrame(AudioEncodingProperties)` — constructor is `AudioFrame(uint capacityInBytes)`
- `AudioGraph.DesiredSamplesPerQuantum` — does not exist
- `LockBuffer(Write)` returns zeros when read — use `AudioBufferAccessMode.Read`
- `QuantumProcessed` — async, irregular. Use `QuantumStarted` for `GetFrame()`
- `AudioFileInputNode.Position` — read-only. Use `Seek(TimeSpan)` instead
