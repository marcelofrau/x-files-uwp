# Audio Visualizers — Shader-Based

## Overview

Fullscreen audio visualizer modes for the music player, rendered via Win2D + custom HLSL
pixel shaders. Select button (View) cycles between modes while keeping the existing VU
meter as one of the modes.

**Does not break existing flow.** Select button in audio fullscreen currently calls
`OnSettings()` which returns immediately (`if (IsAnyFullscreen) return`). Repurposing it
for visualizer cycling has zero impact on existing playback, VU meter, or transport
controls.

## ADR-009: Win2D for Audio Visualizers (extends ADR-002)

**Context**: ADR-002 decides XAML with custom `ControlTemplate` for the file browser UI.
This remains correct — no D2D for buttons, columns, dialogs. Audio visualizers are a
different case: pixel-perfect rendering, custom HLSL shaders, per-frame control — exactly
what ADR-002 noted "not needed for a file browser."

**Decision**: use Win2D (`CanvasCustomControl` + `PixelShaderEffect`) exclusively for
audio visualizers. File browser UI stays 100% XAML.

**Reason**:
- Win2D is a lightweight D3D11 wrapper, already a transitive dependency (via
  UWPAudioVisualizer)
- HLSL pixel shaders via `PixelShaderEffect` (ShaderModel 4.0, level 9.1+)
- Xbox One supports D3D11 feature level 11.0+ — compatible
- Zero impact on file browser UI — visualizers are isolated
- `CanvasCustomControl` is a native UWP `FrameworkElement` — integrates with XAML layout

**Accepted risk**: Win2D on Xbox needs hardware validation. Shader compilation is cached
after first frame.

---

## Mode Cycling

```
enum AudioFullscreenMode
{
    Default,           // album art + VU meter + metadata (current)
    RadialSpectrum,    // visualizer 1
    Waveform,          // visualizer 2
    Plasma             // visualizer 3
}
```

**Cycle**: Default → RadialSpectrum → Waveform → Plasma → Default

Select button in audio fullscreen triggers `OnSelectVisualizer()` which advances the
mode. OSD overlay shows mode name for 2 seconds with fade-out.

---

## Audio Data Pipeline

### AudioLevelService additions

```csharp
// New properties:
public float[] Magnitudes => _magnitudes;   // 512 FFT magnitude bins (0.0–1.0)
public float[] Waveform => _waveformBuffer; // 512 PCM time-domain samples (–1.0–1.0)
public float Beat { get; private set; }     // beat detector output (0.0–1.0)
```

### Beat detector

Simple energy-based detector: compare instantaneous energy to moving average.
When energy > 1.5× average of last N frames → beat = 1.0, then exponential decay.

```csharp
private float _beatDecay = 0.92f;
private float _energyHistory = 0f;
private float _energyInstant = 0f;
private const float BeatThreshold = 1.5f;

// In FFT callback:
float energy = 0f;
for (int i = 0; i < BandCount; i++)
    energy += _bandLevels[i];
energy /= BandCount;

_energyHistory = _energyHistory * 0.95f + energy * 0.05f;
Beat = (energy > _energyHistory * BeatThreshold) ? 1.0f : Beat * _beatDecay;
```

### Thread safety

Arrays are copied from audio thread to UI thread via defensive copy each frame.
Floats are atomically written on x86/x64 — no torn reads for scalar values.
Beat is a single float — naturally atomic.

---

## Visualizer Architecture

```
XFiles/Visualizers/
├── AudioData.cs                    # Snapshot struct of audio data
├── IAudioVisualizer.cs             # Lifecycle interface
├── AudioVisualizerBase.cs          # CanvasCustomControl base
├── VisualizerRegistry.cs           # Registry of available visualizers
├── Visualizers/
│   ├── RadialSpectrumVisualizer.cs # Mode 1
│   ├── WaveformVisualizer.cs       # Mode 2
│   └── PlasmaVisualizer.cs         # Mode 3
└── Shaders/
    ├── RadialSpectrum.hlsl         # Pixel shader
    ├── Waveform.hlsl               # Pixel shader
    └── Plasma.hlsl                 # Pixel shader
```

### AudioData.cs

```csharp
public readonly struct AudioData
{
    public readonly float[] BandLevels;  // 26
    public readonly float[] BandPeaks;   // 26
    public readonly float[] Magnitudes;  // 512
    public readonly float[] Waveform;    // 512
    public readonly float Beat;          // 0–1
    public readonly float Time;          // accumulated seconds
}
```

### IAudioVisualizer.cs

```csharp
public interface IAudioVisualizer : IDisposable
{
    string Name { get; }        // "Radial Spectrum"
    string Id { get; }          // "radial-spectrum"
    void Initialize(CanvasDevice device);
    void Update(AudioData data, TimeSpan elapsed);
    CanvasEffect GetEffect(CanvasRenderTarget target);
    void Resize(Size size);
}
```

### AudioVisualizerBase.cs

- Inherits `Microsoft.Graphics.Canvas.UI.Xaml.CanvasCustomControl`
- `Loaded` → creates `CanvasDevice`, calls `Initialize()` on concrete visualizer
- `OnUpdate` (60fps timer) → reads `AudioLevelService`, creates `AudioData`, calls `Update()`
- `OnDraw` → calls `GetEffect()` and applies to canvas
- `AudioLevelService` injected by `MillerColumnsPage`

### VisualizerRegistry.cs

```csharp
public static class VisualizerRegistry
{
    public static IReadOnlyList<Type> VisualizerTypes { get; } = new[]
    {
        typeof(RadialSpectrumVisualizer),
        typeof(WaveformVisualizer),
        typeof(PlasmaVisualizer),
    };

    public static IAudioVisualizer Create(int index)
        => (IAudioVisualizer)Activator.CreateInstance(VisualizerTypes[index]);
}
```

---

## Visualizer Specs

### 1. Radial Spectrum

26 bars arranged in a circle, each bar = 1 frequency band. Height reactive to
`BandLevels[i]`. Dark background with subtle particles.

**Layout:**
- Inner radius: 15% of smallest dimension
- Outer radius: 40% + bar height (bars grow outward)
- Colors: HSL gradient per band (blue 240° → green 120° → yellow 60° → red 0°)
- Peak indicator: thin white line at top of each bar (`BandPeaks[i]`)
- Glow: extra pass of `GaussianBlurEffect` with opacity 0.3
- Background: `float4(0.02, 0.02, 0.03, 1.0)` (near-black)

**Shader (`RadialSpectrum.hlsl`):**
- Input: `pos` (SV_Position), uniforms (time, resolution, bandLevels[26], bandPeaks[26])
- Convert to polar coordinates (angle, radius)
- Map angle → band index (0–25)
- Test if radius is in bar range → band color
- Subtle fade-out at bar edges

**Performance:** lightweight — 26 radius tests per pixel, no textures.

---

### 2. Waveform

Continuous line representing the time-domain waveform. Vertically mirrored
(symmetry. Trail/feedback creates "light painting" effect.

**Layout:**
- Line plotted from `Waveform[512]` as connected points
- Mirroring: inverted copy below center (horizontal symmetry)
- Thickness: 2–3px
- Color: gradient left (cyan) to right (magenta), modulated by amplitude
- Trail: previous frame at opacity 0.15 (ghosting)
- Glow: `CanvasStrokeProperties` with strokeWidth 6px, similar color, opacity 0.2
- Background: radial gradient dark (center: `#0a0a0f`, edge: `#020203`)

**Shader (`Waveform.hlsl`):**
- Input: `pos`, uniforms (time, resolution, waveform[512], beat)
- For each pixel x: interpolate corresponding `waveform` value
- Distance test on y → draw line if `abs(pos.y - waveformValue) < thickness`
- Color = lerp(cyan, magenta, x / width) × (0.8 + 0.2 × beat)
- Mirror: same test for `pos.y = height - pos.y`

**Performance:** lightweight-medium — linear interpolation, no complex loops.

---

### 3. Plasma

Colorful plasma reactive to audio, MilkDrop-inspired. Sin/cos waves with parameters
modulated by `BandLevels`. `Beat` controls intensity.

**Layout:**
- 3 overlapping waves: `sin(x × freq1 + time)`, `cos(y × freq2 + time × 0.7)`,
  `sin((x+y) × freq3 + time × 1.3)`
- Frequencies modulated by `BandLevels[0–5]` (bass), `[10–15]` (mid), `[20–25]` (treble)
- Colors: HSL rotation — `hue = f(x, y, time, beat)`
- Saturation: 0.7 + 0.3 × `beat` (pulse on beat)
- Brightness: 0.6 + 0.4 × `magnitude_avg`
- Vignette effect (dark edges)
- Background: plasma fills 100% of canvas

**Shader (`Plasma.hlsl`):**
- Input: `pos`, uniforms (time, resolution, bandLevels[26], beat)
- Normalize `pos` to 0–1
- 3 sine components with different frequencies
- `float plasma = (sin1 + sin2 + sin3) / 3.0`
- Colors: `hue = frac(plasma + time × 0.1)` → RGB via HSL
- Modulation: `brightness *= 0.7 + 0.3 × beat`

**Performance:** medium — 3 sin/cos per pixel, GPU handles easily.

---

## UI Integration (MillerColumnsPage)

### Layout

```xml
<Grid x:Name="AudioFullScreenPanel">
    <!-- Default mode: album art + VU meter -->
    <Grid x:Name="FsDefaultContent" Visibility="Visible">
        <!-- current layout, unchanged -->
    </Grid>

    <!-- Visualizer mode: Win2D canvas -->
    <canvas:CanvasAnimatedControl x:Name="FsVisualizerCanvas"
                                   Visibility="Collapsed"
                                   Draw="FsVisualizerCanvas_Draw"
                                   Update="FsVisualizerCanvas_Update" />

    <!-- Transport controls (always visible) -->
    <Grid Grid.Row="1" x:Name="FsTransportControls">
        <!-- current layout, unchanged -->
    </Grid>

    <!-- OSD overlay (mode name, auto-fade) -->
    <TextBlock x:Name="FsModeOSD"
               Visibility="Collapsed"
               Style="{StaticResource OsdText}" />
</Grid>
```

### Cycling logic

```csharp
private AudioFullscreenMode _currentVisualizerMode = AudioFullscreenMode.Default;
private IAudioVisualizer _currentVisualizer;

private void OnSelectVisualizer()
{
    _currentVisualizerMode = (_currentVisualizerMode + 1) % (AudioFullscreenMode.Plasma + 1);

    if (_currentVisualizerMode == AudioFullscreenMode.Default)
    {
        FsDefaultContent.Visibility = Visibility.Visible;
        FsVisualizerCanvas.Visibility = Visibility.Collapsed;
        _currentVisualizer?.Dispose();
        _currentVisualizer = null;
    }
    else
    {
        FsDefaultContent.Visibility = Visibility.Collapsed;
        FsVisualizerCanvas.Visibility = Visibility.Visible;
        ActivateVisualizer(_currentVisualizerMode);
    }

    ShowModeOSD(_currentVisualizerMode);
}
```

### OSD

- Shows mode name: "Radial Spectrum", "Waveform", "Plasma"
- Style: white semi-transparent text, centered, Oxanium 24pt
- Appears, waits 2s, fades out 0.5s, `Visibility="Collapsed"`

---

## File Changes

### Modified files

| File | Change | Risk |
|---|---|---|
| `XFiles.csproj` | Add `<PackageReference Include="Win2D.uwp" />` | Low |
| `XFiles/Audio/AudioLevelService.cs` | Add `Magnitudes[]`, `Waveform[]`, `Beat` + beat detector | Low |
| `XFiles/Controls/MillerColumnsPage.xaml` | Add `CanvasAnimatedControl`, `FsModeOSD` | Low |
| `XFiles/Controls/MillerColumnsPage.xaml.cs` | Add `AudioFullscreenMode` enum, `_currentVisualizer`, `OnSelectVisualizer()`, toggle default/visualizer, `_visualizerTimer` | Medium |
| `XFiles/Navigation/GamepadInputService.cs` | In audio fullscreen, Select → `OnSelectVisualizer()` | Low |
| `XFiles/Navigation/INavigable.cs` | Add `void OnSelectVisualizer()` | Low |

### New files

| File | Purpose |
|---|---|
| `XFiles/Visualizers/AudioData.cs` | Snapshot struct |
| `XFiles/Visualizers/IAudioVisualizer.cs` | Interface |
| `XFiles/Visualizers/AudioVisualizerBase.cs` | CanvasCustomControl base |
| `XFiles/Visualizers/VisualizerRegistry.cs` | Registry |
| `XFiles/Visualizers/Visualizers/RadialSpectrumVisualizer.cs` | Visualizer 1 |
| `XFiles/Visualizers/Visualizers/WaveformVisualizer.cs` | Visualizer 2 |
| `XFiles/Visualizers/Visualizers/PlasmaVisualizer.cs` | Visualizer 3 |
| `XFiles/Visualizers/Shaders/RadialSpectrum.hlsl` | HLSL shader |
| `XFiles/Visualizers/Shaders/Waveform.hlsl` | HLSL shader |
| `XFiles/Visualizers/Shaders/Plasma.hlsl` | HLSL shader |

### Docs

| File | Change |
|---|---|
| `docs/DECISIONS.md` | Add ADR-009 |
| `docs/AUDIO-VISUALIZATION.md` | Add "Shader Visualizers" section |
| `docs/AUDIO-VISUALIZERS.md` | This file (complete visualizer documentation) |

---

## Future Visualizers (next phases)

| # | Name | Concept | Complexity |
|---|---|---|---|
| 4 | **Starfield** | Star field that pulses/explodes with beat. Speed = bass. Color = avg frequency. | Medium |
| 5 | **Spiral Spectrum** | Spiral of points where distance from center = magnitude, angle = time. Rotating colors. | Medium |
| 6 | **Mirror Tunnel** | Infinite tunnel with 4x mirroring. Walls react to bandLevels. | High |
| 7 | **Fire Particles** | 2D particle system with physics. Beat = explosion. Color = heatmap. | High |
| 8 | **Matrix Rain** | Matrix-style character rain. Speed = bass. Density = volume. | Medium |
| 9 | **Lissajous** | Lissajous figures parametrized by bandLevels. Color by time. | Low |
| 10 | **Terrain Generator** | Demoscene-style 2D terrain. Height = magnitudes. Scroll = time. | Medium |
| 11 | **Gradient Noise** | Perlin/Simplex noise reactive to audio. Colors modulated by frequency. | Medium |
| 12 | **Orbiting Circles** | Circles orbiting center. Radius = bandLevel. Speed = beat. | Low |

---

## Implementation Phases

### Phase 10A — Foundation
- Win2D NuGet package
- `AudioLevelService`: expose `Magnitudes`, `Waveform`, `Beat`
- `AudioData`, `IAudioVisualizer`, `AudioVisualizerBase`, `VisualizerRegistry`
- Test: 1 functional visualizer (Radial Spectrum)

### Phase 10B — Integration
- Select cycling in `MillerColumnsPage`
- Mode OSD
- XAML layout with toggle Default/Visualizer
- `GamepadInputService` routing

### Phase 10C — Remaining Visualizers
- Waveform visualizer
- Plasma visualizer
- Test: cycling between all modes works

### Phase 10D — Polish & Xbox Validation
- Test on real Xbox hardware
- Performance tuning
- Beat detector calibration
- Doc: finalize `AUDIO-VISUALIZERS.md`

---

## Testing

- **Xbox hardware**: Win2D renders without crash, shaders compile, 60fps sustained
- **Cycling**: Select correctly alternates between all modes + Default
- **Audio reactivity**: visualizers respond to audio (bars move, waveform oscillates, plasma pulses)
- **Track navigation**: next/prev keeps visualizer in current mode
- **Volume change**: visualizers react to volume changes
- **Empty audio**: visualizers don't crash without audio (static mode)
