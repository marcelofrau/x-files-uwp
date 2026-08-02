# Audio Visualizers — Win2D-Based

## Overview

Fullscreen audio visualizer modes for the music player, rendered via Win2D (`Win2D.uwp`
1.26.0). **Shipped: 31 Win2D visualizers** registered in `VisualizerRegistry`, plus the
`Default` mode (album art + VU meter + metadata). View button (short) cycles modes;
View long-press opens the visualizer picker.

- **View (short)** in audio fullscreen → `OnSelectVisualizer()` → advance mode
- **View (hold ~500ms)** → `OnSelectVisualizerMenu()` → picker overlay

## ADR-009: Win2D for Audio Visualizers (extends ADR-002)

**Context**: ADR-002 decides XAML with custom `ControlTemplate` for the file browser UI.
This remains correct — no D2D for buttons, columns, dialogs. Audio visualizers are a
different case: pixel-perfect rendering, per-frame animation — exactly what ADR-002 noted
"not needed for a file browser."

**Decision**: use Win2D exclusively for audio visualizers. File browser UI stays 100% XAML.

**Reason**:
- Win2D is a lightweight D3D11 wrapper (NuGet: `Win2D.uwp` 1.26.0)
- HLSL pixel shaders via `PixelShaderEffect` (ShaderModel 4.0, level 9.1+)
- Xbox supports D3D11 feature level 11.0+ — compatible
- Zero impact on file browser UI — visualizers are isolated

---

## Win2D UWP Gotchas (lessons learned)

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

`Windows.Storage.Streams.IBuffer` has no `CopyTo`. Use `CryptographicBuffer`:

```csharp
byte[] data;
Windows.Security.Cryptography.CryptographicBuffer.CopyToByteArray(buffer, out data);
```

### 4. `Vector2` needs `System.Numerics`

```csharp
using System.Numerics;  // ← required for Vector2
ds.DrawImage(image, new Vector2(0, 0), ...);
```

### 5. `Math.Min` / `Math.Max` with `byte` — ambiguity trap

```csharp
byte a = (byte)Math.Min(255, (int)(value * 255));  // cast to int — CS0121 otherwise
```

### 6. `CanvasAnimatedControl` is sealed

Cannot inherit. Use **composition** — host inside a `UserControl`
(`AudioVisualizerBase`), with a `CanvasAnimatedControl` field, `Draw`/`Update`
handlers, `SizeChanged` → `Content`.

### 7. Thread safety — `Draw` and `Update` fire on background threads

Never access XAML properties from them. Cache dimensions from `SizeChanged`.
Device initialization happens on first `Draw` (the device is valid there).

### 8. `ICanvasImage` return type for visualizers

`GetImage()` returns `ICanvasImage`, not `CanvasEffect` — allows any Win2D effect tree
(BlendEffect, ArithmeticCompositeEffect, `CanvasRenderTarget`, ...).

### 9. Offscreen render targets — recreate on resize

Always dispose + recreate, never resize an existing target:

```csharp
if (_offscreen == null || _offscreen.Size.Width != _width || _offscreen.Size.Height != _height)
{
    _offscreen?.Dispose();
    _offscreen = new CanvasRenderTarget(_device, _width, _height, 96);
}
```

### 10. PixelShaderEffect array uniforms — indexed property names

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

Using a `CanvasRenderTarget` as a blur source **after its drawing session is disposed**
throws `System.ArgumentException: Effect source #0 is null`.

**DO** — lazy `BlendEffect` with `GaussianBlurEffect` as foreground; caller's `DrawImage`
evaluates the whole chain in one GPU pass:

```csharp
using (var ds = _offscreen.CreateDrawingSession()) { DrawContent(ds); }
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

One `CanvasRenderTarget` (`_offscreen`) per visualizer; no `_glowBuffer` field.

---

## Audio Data Pipeline

`AudioLevelService` (see `AUDIO-VISUALIZATION.md`):

| Property | Size | Notes |
|---|---|---|
| `Magnitudes[]` | 1024 | FFT magnitudes, 0.0–1.0 (`FftSize = 2048`, magnitudes = `FftSize/2`) |
| `Waveform[]` | 2048 | PCM time-domain samples, –1.0–1.0 |
| `BandLevels[]` | 26 | smoothed per-band (bar count for VU + most visualizers) |
| `BandPeaks[]` | 26 | peak-hold per band |
| `Beat` | float | energy-based beat detector, 0.0–1.0 |

`AudioData` is a readonly snapshot struct; `AudioData.FromService()` does a defensive
copy from the audio thread to the UI/visualizer thread. Scalars are naturally atomic
(floats on x86/x64).

---

## Architecture

```
XFiles/Visualizers/
├── AudioData.cs                    # Snapshot struct (26 bands, 1024 mags, 2048 waveform, beat, time)
├── IAudioVisualizer.cs             # Lifecycle interface (Initialize/Update/GetImage/Resize)
├── AudioVisualizerBase.cs          # UserControl hosting CanvasAnimatedControl
├── VisualizerRegistry.cs           # 31 visualizer types + Create(index)/Resolve(mode)
├── AudioFullscreenMode.cs          # Enum: Default + 31 modes
├── PostProcessPipeline.cs          # feedback/bloom/vignette/scanlines/grain/C.A. pipeline
├── Visualizers/                    # 31 IAudioVisualizer implementations
└── Shaders/                        # HLSL reference (not compiled at runtime)
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

### VisualizerRegistry.cs

```csharp
public static class VisualizerRegistry
{
    public static IAudioVisualizer Create(int index);          // registry index → instance
    public static IAudioVisualizer Resolve(AudioFullscreenMode mode);
}
```

Index order matches the `AudioFullscreenMode` enum (after `Default`) — `Create(i)`
returns the visualizer for mode `i`. **New visualizers must be added to both**
`VisualizerTypes[]` in the registry and the `AudioFullscreenMode` enum.

### PostProcessPipeline.cs

Cross-visualizer post-processing applied to the final frame in
`AudioVisualizerBase`: feedback trail (`ArithmeticCompositeEffect`), bloom
(`GaussianBlurEffect` + blend), vignette, slide/rotation, chromatic aberration,
scanlines, noise grain. Tunable per visualizer or via picker settings. `Draw(...)`
runs the chain in a single GPU pass.

---

## The 31 Visualizers

| # | Mode (`AudioFullscreenMode`) | Visualizer class | Concept |
|---|---|---|---|
| 1 | `RadialSpectrum` | `RadialSpectrumVisualizer` | 26 radial bars + peaks + glow |
| 2 | `Waveform` | `WaveformVisualizer` | time-domain line + trail ghosting |
| 3 | `Plasma` | `PlasmaVisualizer` | 3 sin/cos waves, HSL rotation, vignette |
| 4 | `Starfield` | `StarfieldVisualizer` | stars pulsing/exploding with beat |
| 5 | `SpiralSpectrum` | `SpiralSpectrumVisualizer` | spiral, distance=magnitude, angle=time |
| 6 | `MirrorTunnel` | `MirrorTunnelVisualizer` | infinite tunnel, 4x mirroring, band-reactive walls |
| 7 | `FireParticles` | `FireParticlesVisualizer` | 2D particle physics, beat=explosion, heatmap |
| 8 | `Lissajous` | `LissajousVisualizer` | Lissajous figures, param by bands, color by time |
| 9 | `TerrainGenerator` | `TerrainGeneratorVisualizer` | demoscene terrain, height=magnitudes |
| 10 | `OrbitingCircles` | `OrbitingCirclesVisualizer` | circles orbiting, radius=band, speed=beat |
| 11 | `IsometricEqualizer` | `IsometricEqualizerVisualizer` | 3D iso grid of bars |
| 12 | `NeonGlare` | `NeonGlareVisualizer` | neon glow bars + blur |
| 13 | `Kaleidoscope` | `KaleidoscopeVisualizer` | mirrored kaleidoscope of content |
| 14 | `ParticleBurst` | `ParticleBurstVisualizer` | particle bursts per beat |
| 15 | `RipplePulse` | `RipplePulseVisualizer` | expanding ripples from center |
| 16 | `FeedbackTrail` | `FeedbackTrailVisualizer` | heavy feedback ghosting (light painting) |
| 17 | `VoxelMatrix` | `VoxelMatrixVisualizer` | 3D voxel matrix, depth-sorted cells |
| 18 | `AnalogVUMeter` | `AnalogVUMeterVisualizer` | retro analog needle VU |
| 19 | `CircularRadialSpectrum` | `CircularRadialSpectrumVisualizer` | circular variant of radial spectrum |
| 20 | `RetroOscilloscope` | `RetroOscilloscopeVisualizer` | CRT-style oscilloscope, phosphor glow |
| 21 | `InfernoCore` | `InfernoCoreVisualizer` | pulsing fire core, beat-driven |
| 22 | `WaveformTunnel` | `WaveformTunnelVisualizer` | waveform mapped into a tunnel |
| 23 | `GeissFluid` | `GeissVisualizer` | Geiss-style liquid fluid |
| 24 | `Xbox360Boot` | `Xbox360BootVisualizer` | 360 boot wedge morph |
| 25 | `InvertedBars` | `InvertedBarsVisualizer` | bars mirrored/centered inversion |
| 26 | `ThreeDO` | `ThreeDOVisualizer` | 3D bars/planes wireframe |
| 27 | `ThreeDWave` | `ThreeDWaveVisualizer` | 3D height-field wave |
| 28 | `ComancheTerrain` | `ComancheVisualizer` | Comanche-style scrolling terrain |
| 29 | `SynthwaveVuMeter` | `SynthwaveVuMeterVisualizer` | synthwave sun + VU grid |
| 30 | `ClassicVUMeter` | `ClassicVUMeterVisualizer` | nostalgic green/yellow/red LED VU + peak bars |
| 31 | `NightCity` | `NightCityVisualizer` | After Dark "Starry Night" style: dark hills + dense town silhouette, rippling river reflection, puffy clouds, moon, twinkling stars, music-reactive random window lights |

### Signature visualizers

- **Radial Spectrum**: 26 bars in a circle; inner radius 15%, outer 40% + bar height;
  HSL gradient per band (blue→green→yellow→red); thin peak line; blur glow pass.
- **Waveform**: mirrored time-domain line; cyan→magenta gradient; 0.15-opacity trail;
  radial-gradient dark background.
- **Plasma**: 3 overlapping waves (`sin`/`cos`, freqs from bass/mid/treble bands);
  HSL rotation hue; saturation pulses with beat; brightness with magnitude; vignette.

---

## Creating a New Visualizer — Checklist

1. Create `XFiles/Visualizers/Visualizers/YourVisualizer.cs` implementing
   `IAudioVisualizer` (see gotchas #8 for return type, #4 for `Vector2`).
2. Add the `<Compile Include>` entry to `XFiles.csproj` — **required**, old-style
   csproj won't pick up new `.cs` files (CS0234 otherwise).
3. Add `typeof(Visualizers.YourVisualizer)` to `VisualizerTypes[]` in
   `VisualizerRegistry.cs`.
4. Add the enum value to `AudioFullscreenMode.cs` (position must match registry index).
5. Add the label in `MillerColumnsPage` visualizer list / OSD.
6. Add HLSL reference in `Shaders/` (optional).
7. Test: cycle through all modes, verify no crash, audio reactivity, empty-audio static.

---

## Mode Cycling

`Default` → mode 1 → mode 2 → ... → mode 31 → `Default`. View (short) advances one;
View (hold) opens picker. OSD shows mode name 2s with fade-out. Track next/prev keeps
the current mode. Volume changes are reflected (visualizers read magnitudes).
Empty audio → static mode (no crash).

## Status

Shipped and validated on Xbox hardware (Phase 10D). All Win2D rendering runs at 60fps;
post-processing pipeline (feedback/bloom) is on by default. See `AUDIO-VISUALIZATION.md`
for the audio graph + FFT pipeline.
