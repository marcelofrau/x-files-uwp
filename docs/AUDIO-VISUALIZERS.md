# Audio Visualizers — Win2D-Based

## Overview

Fullscreen audio visualizer modes for the music player, rendered via Win2D (`Win2D.uwp`
1.26.0). Select button (View) cycles between modes while keeping the existing VU meter as
one of the modes.

**Does not break existing flow.** Select button in audio fullscreen triggers
`OnSelectVisualizer()` which advances the mode.

## ADR-009: Win2D for Audio Visualizers (extends ADR-002)

**Context**: ADR-002 decides XAML with custom `ControlTemplate` for the file browser UI.
This remains correct — no D2D for buttons, columns, dialogs. Audio visualizers are a
different case: pixel-perfect rendering, per-frame animation — exactly what ADR-002 noted
"not needed for a file browser."

**Decision**: use Win2D exclusively for audio visualizers. File browser UI stays 100% XAML.

**Reason**:
- Win2D is a lightweight D3D11 wrapper (NuGet: `Win2D.uwp` 1.26.0)
- HLSL pixel shaders via `PixelShaderEffect` (ShaderModel 4.0, level 9.1+)
- Xbox One supports D3D11 feature level 11.0+ — compatible
- Zero impact on file browser UI — visualizers are isolated

---

## Win2D UWP Gotchas ( lessons learned)

> **Read this before writing any new visualizer.** These are API pitfalls that cost us
> multiple build cycles.

### 1. Class names — NOT what you'd expect

Win2D UWP effect classes do **not** have the `Canvas` prefix. They live in
`Microsoft.Graphics.Canvas.Effects`:

| ❌ Wrong | ✅ Correct | Namespace |
|---|---|---|
| `CanvasBlendEffect` | `BlendEffect` | `Microsoft.Graphics.Canvas.Effects` |
| `CanvasBlendMode` | `BlendEffectMode` | `Microsoft.Graphics.Canvas.Effects` |
| `CanvasImageBlendEffect` | `BlendEffect` | `Microsoft.Graphics.Canvas.Effects` |
| `CanvasImageBlendMode` | `BlendEffectMode` | `Microsoft.Graphics.Canvas.Effects` |
| `CanvasArithmeticBlendEffect` | `ArithmeticCompositeEffect` | `Microsoft.Graphics.Canvas.Effects` |
| `CanvasGaussianBlurEffect` | `GaussianBlurEffect` | `Microsoft.Graphics.Canvas.Effects` |
| `CanvasColorSourceEffect` | `ColorSourceEffect` | `Microsoft.Graphics.Canvas.Effects` |

**Rule**: If you see a `Canvas` prefix on an effect class, it's wrong for UWP Win2D.

### 2. Arithmetic blending = `ArithmeticCompositeEffect`, not `BlendEffect`

`BlendEffectMode` has no `Arithmetic` member. For weighted arithmetic blending
(fade, trail, ghosting), use `ArithmeticCompositeEffect`:

```csharp
// WRONG — won't compile:
var fade = new CanvasBlendEffect { Mode = CanvasBlendMode.Arithmetic ... };

// ✅ CORRECT:
var fade = new ArithmeticCompositeEffect
{
    Source1 = trailFrame,
    Source2 = trailFrame,
    Source1Amount = 0.85f,   // keep 85% of trail
    Source2Amount = 0f,
    MultiplyAmount = 0f,
    Offset = 0f
};
```

Formula: `result = S1 * S1Amount + S2 * S2Amount + S1*S2*MultiplyAmount + Offset`

### 3. `IBuffer.CopyTo()` does not exist

`Windows.Storage.Streams.IBuffer` has no `CopyTo` method in UWP. Use `DataReader`:

```csharp
// ❌ Wrong — IBuffer has no CopyTo or AsInputStream without extra imports:
byte[] data = new byte[buffer.Length];
buffer.CopyTo(data);  // CS1061
var reader = new DataReader(buffer.AsInputStream());  // CS1061

// ✅ Correct — CryptographicBuffer (no extra using needed):
byte[] data;
Windows.Security.Cryptography.CryptographicBuffer.CopyToByteArray(buffer, out data);

### 4. `Vector2` needs `System.Numerics`

Win2D methods that take `Vector2` (e.g., `DrawImage` with offset) use
`System.Numerics.Vector2`, NOT a Win2D-specific type:

```csharp
using System.Numerics;  // ← required for Vector2

ds.DrawImage(image, new Vector2(0, 0), ...);
```

### 5. `Math.Min` / `Math.Max` with `byte` — ambiguity trap

`byte` arguments create ambiguity between `Math.Min(byte,byte)` and `Math.Min(int,int)`:

```csharp
// ❌ Wrong — CS0121 ambiguous:
byte a = (byte)Math.Min(255, (byte)(value * 255));

// ✅ Correct — cast to int:
byte a = (byte)Math.Min(255, (int)(value * 255));
```

### 6. `CanvasAnimatedControl` is sealed

Cannot inherit from it. Use **composition** — host it inside a `UserControl`:

```csharp
// ✅ AudioVisualizerBase approach:
public sealed class AudioVisualizerBase : UserControl
{
    private readonly CanvasAnimatedControl _canvas;

    public AudioVisualizerBase()
    {
        _canvas = new CanvasAnimatedControl { ClearColor = Colors.Black };
        _canvas.Draw += OnCanvasDraw;
        _canvas.Update += OnCanvasUpdate;
        _canvas.SizeChanged += OnCanvasSizeChanged;
        Content = _canvas;
    }
}
```

### 7. Thread safety — `Draw` and `Update` fire on background threads

`OnCanvasDraw` and `OnCanvasUpdate` in `CanvasAnimatedControl` run on composition
threads, **NOT** the UI thread. Never access XAML properties from them:

```csharp
// ❌ Wrong — crashes or wrong value:
float w = (float)ActualWidth;  // XAML property from background thread

// ✅ Correct — cache from SizeChanged:
private float _cachedWidth, _cachedHeight;
private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e)
{
    _cachedWidth = (float)e.NewSize.Width;
    _cachedHeight = (float)e.NewSize.Height;
}
```

Device initialization must happen on first Draw call (the device is valid there):

```csharp
private void OnCanvasDraw(ICanvasAnimatedControl sender, CanvasAnimatedDrawEventArgs args)
{
    if (!_initialized)
    {
        _visualizer.Initialize(args.DrawingSession.Device);  // ← safe here
        _visualizer.Resize(_cachedWidth, _cachedHeight);
        _initialized = true;
    }
    args.DrawingSession.DrawImage(_visualizer.GetImage());
}
```

### 8. `ICanvasImage` return type for visualizers

`GetImage()` returns `ICanvasImage`, not `CanvasEffect`. This allows returning any
Win2D effect tree (BlendEffect, ArithmeticCompositeEffect, etc.) or a
`CanvasRenderTarget`.

### 9. Offscreen render targets — recreate on resize

```csharp
if (_offscreen == null || _offscreen.Size.Width != _width || _offscreen.Size.Height != _height)
{
    _offscreen?.Dispose();
    _offscreen = new CanvasRenderTarget(_device, _width, _height, 96);
}
```

Always dispose + recreate, never try to resize an existing target.

### 10. PixelShaderEffect array uniforms — indexed property names

Setting array uniforms on `PixelShaderEffect` uses indexed names:

```csharp
var shader = new PixelShaderEffect(bytecode);
for (int i = 0; i < 26; i++)
    shader.Properties[$"uBandLevels[{i}]"] = levels[i];
```

### 11. Full required usings for a visualizer

```csharp
using System;
using System.Numerics;                            // Vector2
using Microsoft.Graphics.Canvas;                  // CanvasDevice, CanvasRenderTarget
using Microsoft.Graphics.Canvas.Effects;          // BlendEffect, GaussianBlurEffect, etc.
using Microsoft.Graphics.Canvas.Geometry;         // CanvasGeometry
using Windows.Foundation;                         // Rect, Size
using Windows.UI;                                 // Color, Colors
using Windows.Storage.Streams;                    // DataReader (for shader loading)
```

### 12. Glow blur — use lazy effect chain, NOT a second render target

Using a `CanvasRenderTarget` as a blur source **after its drawing session is disposed** throws
`System.ArgumentException: Effect source #0 is null`. The render target becomes invalid for
read-back once its GPU session ends.

**DO NOT** do this:
```csharp
// ❌ WRONG — glowBuffer reads _offscreen after its session is disposed
using (var ds = _offscreen.CreateDrawingSession())
{
    DrawContent(ds);
}
using (var ds = _glowBuffer.CreateDrawingSession())
{
    var blur = new GaussianBlurEffect { Source = _offscreen, BlurAmount = 8f };
    ds.DrawImage(blur);  // 💥 ArgumentException
}
return new BlendEffect { Background = _offscreen, Foreground = _glowBuffer, ... };
```

**DO** this instead — return a lazy `BlendEffect` with `GaussianBlurEffect` as foreground.
The caller's `DrawImage` evaluates the entire chain in one GPU pass:
```csharp
// ✅ CORRECT — lazy effect chain, no second render target needed
using (var ds = _offscreen.CreateDrawingSession())
{
    DrawContent(ds);
}
var blur = new GaussianBlurEffect
{
    Source = _offscreen,
    BlurAmount = 8f,
    BorderMode = EffectBorderMode.Soft
};
return new BlendEffect
{
    Background = _offscreen,
    Foreground = blur,           // blur reads _offscreen when drawn by caller
    Mode = BlendEffectMode.Screen
};
```

This eliminates the `_glowBuffer` field entirely — only one `CanvasRenderTarget` (`_offscreen`)
is needed per visualizer.

---

## Creating a New Visualizer — Checklist

1. Create `XFiles/Visualizers/Visualizers/YourVisualizer.cs`
   - Implement `IAudioVisualizer` (see Gotchas #8 for return type)
   - Use correct class names (see Gotchas #1–3)
   - Use `System.Numerics.Vector2` (see Gotchas #4)
   - Cast `byte` math to `int` for `Math.Min/Max` (see Gotchas #5)
2. Add to `XFiles.csproj`:
   ```xml
   <Compile Include="Visualizers\Visualizers\YourVisualizer.cs" />
   ```
3. Add to `VisualizerRegistry.cs`:
   - Add `typeof(Visualizers.YourVisualizer)` to `VisualizerTypes[]`
   - Add case in `Resolve()` switch
4. Add enum value in `AudioFullscreenMode.cs`
5. Add label in `MillerColumnsPage._fsModeOrder[]`
6. Add HLSL reference in `Shaders/` (optional, for future GPU path)
7. Test: cycle through all modes, verify no crash, audio reactivity works

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
├── AudioVisualizerBase.cs          # UserControl hosting CanvasAnimatedControl
├── VisualizerRegistry.cs           # Registry of available visualizers
├── AudioFullscreenMode.cs          # Enum: Default, RadialSpectrum, Waveform, Plasma
├── Visualizers/
│   ├── RadialSpectrumVisualizer.cs # Mode 1 — 26 radial bars + peaks + glow
│   ├── WaveformVisualizer.cs       # Mode 2 — time-domain line + trail ghosting
│   └── PlasmaVisualizer.cs         # Mode 3 — 3 sin/cos waves + HSL color + vignette
└── Shaders/
    ├── RadialSpectrum.hlsl         # HLSL reference (not compiled at runtime)
    ├── Waveform.hlsl               # HLSL reference
    └── Plasma.hlsl                 # HLSL reference
```

### AudioData.cs

```csharp
public readonly struct AudioData
{
    public const int BandCount = 26;
    public const int FftBinCount = 1024;

    public readonly float[] BandLevels;   // 26 — smoothed 0.0–1.0 per band
    public readonly float[] BandPeaks;    // 26 — peak hold per band
    public readonly float[] Magnitudes;   // 1024 — raw FFT magnitudes
    public readonly float[] Waveform;     // 2048 — PCM time-domain samples
    public readonly int WaveformCount;    // valid sample count
    public readonly float Beat;           // 0.0–1.0 beat detector
    public readonly float Time;           // accumulated seconds

    public static AudioData FromService(AudioLevelService service, float time);
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
    ICanvasImage GetImage();    // ← ICanvasImage, NOT CanvasEffect
    void Resize(float width, float height);
}
```

### AudioVisualizerBase.cs

- Inherits `UserControl` (composition, NOT inheritance — `CanvasAnimatedControl` is sealed)
- Hosts a `CanvasAnimatedControl` internally
- `AttachService(AudioLevelService)` — feeds audio data
- `Activate(IAudioVisualizer)` / `Deactivate()` — lifecycle
- `OnCanvasUpdate` (60fps) — reads `AudioLevelService`, creates `AudioData`, calls `Update()`
- `OnCanvasDraw` — calls `GetImage()` and draws result; initializes device on first draw
- Size cached from `SizeChanged` event (never read XAML properties from background thread)

### VisualizerRegistry.cs

```csharp
public static class VisualizerRegistry
{
    // Index matches AudioFullscreenMode enum order (minus Default)
    // RadialSpectrum=0, Waveform=1, Plasma=2

    public static IAudioVisualizer Create(int index);
    public static IAudioVisualizer Resolve(AudioFullscreenMode mode);
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
| `XFiles.csproj` | Add `<PackageReference Include="Win2D.uwp" />` + all visualizer `<Compile>` entries | Low |
| `XFiles/Audio/AudioLevelService.cs` | Add `Magnitudes[]`, `Waveform[]`, `Beat` + beat detector | Low |
| `XFiles/Controls/MillerColumnsPage.xaml` | Add `AudioVisualizerBase`, `FsModeOSD` | Low |
| `XFiles/Controls/MillerColumnsPage.xaml.cs` | Add `AudioFullscreenMode` cycling, `OnSelectVisualizer()`, `ApplyAudioVisualizerMode()` | Medium |
| `XFiles/Navigation/GamepadInputService.cs` | In audio fullscreen, Select → `OnSelectVisualizer()` | Low |
| `XFiles/Navigation/INavigable.cs` | Add `void OnSelectVisualizer()` | Low |

### New files

| File | Purpose |
|---|---|
| `XFiles/Visualizers/AudioData.cs` | Snapshot struct |
| `XFiles/Visualizers/IAudioVisualizer.cs` | Interface (`ICanvasImage GetImage()`) |
| `XFiles/Visualizers/AudioVisualizerBase.cs` | UserControl hosting `CanvasAnimatedControl` |
| `XFiles/Visualizers/AudioFullscreenMode.cs` | Enum: Default, RadialSpectrum, Waveform, Plasma |
| `XFiles/Visualizers/VisualizerRegistry.cs` | Registry (maps mode → index → type) |
| `XFiles/Visualizers/Visualizers/RadialSpectrumVisualizer.cs` | Mode 1 |
| `XFiles/Visualizers/Visualizers/WaveformVisualizer.cs` | Mode 2 |
| `XFiles/Visualizers/Visualizers/PlasmaVisualizer.cs` | Mode 3 |
| `XFiles/Visualizers/Shaders/RadialSpectrum.hlsl` | HLSL reference (not compiled at runtime) |
| `XFiles/Visualizers/Shaders/Waveform.hlsl` | HLSL reference |
| `XFiles/Visualizers/Shaders/Plasma.hlsl` | HLSL reference |

> **IMPORTANT**: Old-style `.csproj` requires explicit `<Compile Include>` for every `.cs`
> file. New visualizer files MUST be added to `XFiles.csproj` or the compiler won't see
> them (CS0234 "type does not exist").

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
