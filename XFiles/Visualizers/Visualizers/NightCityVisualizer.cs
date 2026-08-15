using System;
using System.Collections.Generic;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Windows.Foundation;
using Windows.UI;

namespace XFiles.Visualizers.Visualizers
{
    /// <summary>
    /// After Dark "Starry Night" style scene: deep night sky, soft glowing moon,
    /// drifting clouds, layered hill silhouettes, a sparse skyline with warm
    /// window glow, and a reflecting shoreline with rippling water.
    ///
    /// Composition intent (vs. original NightCityVisualizer):
    /// - 3 depth layers instead of 4, fewer/taller buildings -> reads as skyline
    ///   silhouette, not a literal NYC block.
    /// - No street, no cars. Horizon goes straight into water.
    /// - Parallax is audible: every layer's scroll speed reacts a bit to bass,
    ///   near layers react more (classic Streets of Rage depth cue).
    /// - Window lights are soft round glows (radial sprite), not hard squares.
    /// </summary>
    public sealed class NightCityVisualizer : IAudioVisualizer
    {
        public string Name => "Night City";
        public string Id => "night-city";

        private CanvasDevice _device;
        private float _width, _height, _time;

        private const int StarCount = 220;
        private const int MaxBuildings = 110;
        private const int LayerCount = 3;
        private const int CloudPuffCount = 14;
        private const int ShootingStarCount = 3;
        private const int ReflectStrips = 6;

        // Horizon sits higher than before (0.60 -> 0.66): more sky, less city.
        private const float HorizonFraction = 0.66f;
        private const float AudioSmooth = 0.22f;

        // Moon sits upper-right; building shading lights the face pointing at
        // it, so the two must share the same horizontal position.
        private const float MoonXFraction = 0.76f;

        // --- Stars -------------------------------------------------------
        private readonly float[] _starX = new float[StarCount];
        private readonly float[] _starY = new float[StarCount];
        private readonly float[] _starSize = new float[StarCount];
        private readonly float[] _starPhase = new float[StarCount];
        private readonly float[] _starSpeed = new float[StarCount];
        private readonly float[] _starDepth = new float[StarCount]; // 0 = far, 1 = near -> subtle parallax drift
        private float _starDrift; // unbounded accumulator, never wraps (no position reset)

        // --- Buildings -----------------------------------------------------
        private readonly float[] _bX = new float[MaxBuildings];
        private readonly float[] _bW = new float[MaxBuildings];
        private readonly float[] _bH = new float[MaxBuildings];
        private readonly int[] _bLayer = new int[MaxBuildings];
        private int _buildingCount;

        private readonly float[] _layerOffset = new float[LayerCount];
        // Base scroll speed per layer (far -> near), very subtle: near layer
        // takes ~67s to cross one screen at rest. Multiplied by audio at draw.
        private readonly float[] _layerSpeed = { 0.00008f, 0.00015f, 0.00025f };
        // How much bass pushes each layer's speed. Near layers punch harder.
        private readonly float[] _layerBassKick = { 0.10f, 0.20f, 0.35f };
        // Hard cap so even a full bass hit never makes the skyline race.
        private readonly float[] _layerMaxSpeed = { 0.00012f, 0.00022f, 0.00035f };

        // --- Clouds ----------------------------------------------------
        private readonly float[] _cloudX = new float[CloudPuffCount];
        private readonly float[] _cloudY = new float[CloudPuffCount];
        private readonly float[] _cloudS = new float[CloudPuffCount];
        private readonly float[] _cloudV = new float[CloudPuffCount];
        private readonly float[] _cloudA = new float[CloudPuffCount];

        // --- Shooting stars ----------------------------------------------
        private readonly float[] _shootX = new float[ShootingStarCount];
        private readonly float[] _shootY = new float[ShootingStarCount];
        private readonly float[] _shootVX = new float[ShootingStarCount];
        private readonly float[] _shootVY = new float[ShootingStarCount];
        private readonly float[] _shootLife = new float[ShootingStarCount];
        private float _shootTimer = 4f;

        private float _smoothBass, _smoothMid, _smoothTreble, _smoothBeat;
        private readonly float[] _smoothBands = new float[26];
        private Random _rng;

        private CanvasLinearGradientBrush _skyBrush;
        private CanvasRenderTarget _sceneTarget;
        private CanvasRenderTarget _cloudSprite;
        private CanvasRenderTarget _glowSprite; // soft round sprite used for stars & windows
        private bool _skylineDirty = true;

        // Per-building pre-baked window masks: static (always-on) and reactive
        // (VU-group, tinted at draw time). Replaces thousands of per-window
        // sprite draws with two texture draws per building.
        private readonly CanvasRenderTarget[] _winStatic = new CanvasRenderTarget[MaxBuildings];
        private readonly CanvasRenderTarget[] _winReactive = new CanvasRenderTarget[MaxBuildings];

        // Serializes resource rebuild (GenerateSkyline/BakeWindowMasks on the
        // render thread) against teardown (Dispose on the UI thread). Without
        // it, Dispose nulls _winStatic[i]/_device while BakeWindowMasks is
        // mid-assignment -> NullReferenceException on the next CreateDrawingSession.
        private readonly object _resLock = new object();

        // Grid step shrinks toward the back: distant layers pack more, smaller
        // lights per facade (city glow), front layers space them out a bit.
        private static readonly float[] WindowGridStep = { 5f, 6f, 6.5f };
        private static readonly float[] WindowLitBase = { 0.95f, 0.97f, 0.98f };
        private static readonly float[] WindowRadScale = { 0.34f, 0.70f, 1.0f };
        private static readonly float[] LayerAlphaFactor = { 0.55f, 0.80f, 1.0f };

        // Experimental building shading: set ShadingStrength to 0 to disable.
        // 0.4 keeps the moon-lit face a whisper instead of a painted stripe.
        private static readonly float ShadingStrength = 0.4f;
        private static readonly float ShadingDepthFar = 0.35f;
        private static readonly float ShadingDepthMid = 0.65f;
        private static readonly float ShadingDepthNear = 1f;

        // Reused per-frame gradient brushes for building shading. StartPoint /
        // EndPoint / Opacity are mutated per piece, so no per-building allocs
        // on the render thread (allocation is costly under NoGCRegion).
        private CanvasLinearGradientBrush _shadeSide;
        private CanvasLinearGradientBrush _shadeDark;
        private CanvasLinearGradientBrush _shadeAo;
        private bool _drawErrorLogged;

        // --- Foreground band (avenue, bridge, shore, trees) -----------------
        // Scrolls SLOWER than the near building layer so the waterline reads
        // as a deeper plane than the skyline (buildings still pan fastest).
        private float _fgOffset;
        private const float FgSpeed = 0.00016f;
        private const float FgBassKick = 0.28f;
        private const float FgMaxSpeed = 0.00026f;
        private int _fgBridgeType;
        private const int BridgeTypeCount = 4;

        private enum BridgeType { CableStayed, Arch, Suspension, Bowstring }

        private enum RoofStyle { Flat, Antenna, Spire, Setback, WaterTower, Dome, Dish, Billboard, Crenellation, Tanks, CellTower }

        public void Initialize(CanvasDevice device)
        {
            _device = device;
            _rng = new Random();

            for (int i = 0; i < StarCount; i++)
            {
                _starX[i] = (float)_rng.NextDouble();
                _starY[i] = (float)_rng.NextDouble() * 0.55f;
                _starDepth[i] = (float)_rng.NextDouble();
                _starSize[i] = 0.5f + _starDepth[i] * 1.3f;
                _starPhase[i] = (float)_rng.NextDouble() * MathF.PI * 2f;
                _starSpeed[i] = 0.6f + (float)_rng.NextDouble() * 1.8f;
            }
            InitClouds();
        }

        private void InitClouds()
        {
            for (int i = 0; i < CloudPuffCount; i++)
            {
                _cloudX[i] = (float)_rng.NextDouble();
                _cloudY[i] = 0.02f + (float)_rng.NextDouble() * 0.36f;
                _cloudS[i] = 0.6f + (float)_rng.NextDouble() * 1.5f;
                _cloudV[i] = 0.002f + (float)_rng.NextDouble() * 0.006f;
                _cloudA[i] = 0.05f + (float)_rng.NextDouble() * 0.09f;
            }
        }

        public void Update(AudioData data, TimeSpan elapsed)
        {
            float dt = (float)elapsed.TotalSeconds;
            _time = data.Time;

            if (data.BandLevels != null && data.BandLevels.Length > 0)
            {
                int n = data.BandLevels.Length;
                for (int i = 0; i < 26 && i < n; i++)
                    _smoothBands[i] += (data.BandLevels[i] - _smoothBands[i]) * AudioSmooth;

                // Guarded band averaging: previously m6/t6 could resolve to 0
                // when BandLevels had fewer than ~16-26 entries, producing a
                // 0/0 = NaN that poisoned _smoothMid/_smoothTreble forever
                // (EMA never recovers from NaN). Clamp divisor to >= 1.
                float bass = 0f, mid = 0f, treble = 0f;
                int b6 = Math.Clamp(n, 0, 6);
                int m6 = Math.Clamp(n - 10, 0, 6);
                int t6 = Math.Clamp(n - 20, 0, 6);

                for (int i = 0; i < b6; i++) bass += data.BandLevels[i];
                for (int i = 10; i < 10 + m6; i++) mid += data.BandLevels[i];
                for (int i = 20; i < 20 + t6; i++) treble += data.BandLevels[i];

                bass /= Math.Max(1, b6);
                mid /= Math.Max(1, m6);
                treble /= Math.Max(1, t6);

                _smoothBass += (bass - _smoothBass) * AudioSmooth;
                _smoothMid += (mid - _smoothMid) * AudioSmooth;
                _smoothTreble += (treble - _smoothTreble) * AudioSmooth;
                _smoothBeat += (data.Beat - _smoothBeat) * 0.4f;
            }

            for (int i = 0; i < CloudPuffCount; i++)
            {
                _cloudX[i] += _cloudV[i] * (0.7f + _smoothBass * 0.8f) * dt;
                if (_cloudX[i] > 1.25f) _cloudX[i] = -0.25f;
            }

            UpdateShootingStars(dt);

            for (int l = 0; l < LayerCount; l++)
            {
                float speed = _layerSpeed[l] * (1f + _smoothBass * _layerBassKick[l]);
                speed = Math.Min(speed, _layerMaxSpeed[l]);
                _layerOffset[l] += speed * dt * 60f; // dt-normalized, tuned to feel like the old per-frame constant
                if (_layerOffset[l] > 1f) _layerOffset[l] -= 1f;
            }

            // Foreground band drifts slower than the buildings; each full wrap
            // swaps the bridge archetype so new structures enter from the
            // right (no mid-frame pop).
            float fgSpeed = Math.Min(FgSpeed * (1f + _smoothBass * FgBassKick), FgMaxSpeed);
            _fgOffset += fgSpeed * dt * 60f;
            if (_fgOffset >= 1f)
            {
                _fgOffset -= 1f;
                _fgBridgeType = (_fgBridgeType + 1) % BridgeTypeCount;
            }

            // Stars drift barely at all; accumulator is unbounded so the tiny
            // movement never snaps back (no position reset on layer wrap).
            float nearSpeed = Math.Min(
                _layerSpeed[LayerCount - 1] * (1f + _smoothBass * _layerBassKick[LayerCount - 1]),
                _layerMaxSpeed[LayerCount - 1]);
            _starDrift += nearSpeed * dt * 60f * 0.04f;
        }

        private void UpdateShootingStars(float dt)
        {
            for (int i = 0; i < ShootingStarCount; i++)
            {
                if (_shootLife[i] > 0f)
                {
                    _shootLife[i] -= dt;
                    _shootX[i] += _shootVX[i] * dt;
                    _shootY[i] += _shootVY[i] * dt;
                }
                else
                {
                    _shootLife[i] = 0f;
                }
            }

            _shootTimer -= dt;
            if (_shootTimer <= 0f)
            {
                _shootTimer = 6f + (float)_rng.NextDouble() * 9f;
                int slot = -1;
                for (int i = 0; i < ShootingStarCount; i++)
                    if (_shootLife[i] <= 0f) { slot = i; break; }

                if (slot >= 0)
                {
                    _shootX[slot] = 0.10f + (float)_rng.NextDouble() * 0.6f;
                    _shootY[slot] = 0.04f + (float)_rng.NextDouble() * 0.26f;
                    float ang = (float)(Math.PI * (0.62 + _rng.NextDouble() * 0.2));
                    float spd = 0.42f + (float)_rng.NextDouble() * 0.28f;
                    _shootVX[slot] = MathF.Cos(ang) * spd;
                    _shootVY[slot] = MathF.Sin(ang) * spd * 0.55f;
                    _shootLife[slot] = 0.9f + (float)_rng.NextDouble() * 0.7f;
                }
            }
        }

        public void Draw(CanvasDrawingSession ds)
        {
            if (_device == null || _width <= 0 || _height <= 0) return;

            EnsureSceneTarget();
            if (_sceneTarget == null) return;

            try
            {
                // Skyline rebuild allocates render targets, so it must be
                // serialized against Dispose (UI thread) — and it must never
                // escape to kill the frame: any transient target/GPU error here
                // degrades to a skipped scene (logged once) like RenderScene.
                lock (_resLock)
                {
                    if (_skylineDirty) { GenerateSkyline(); _skylineDirty = false; }
                }

                using (var sceneDs = _sceneTarget.CreateDrawingSession())
                    RenderScene(sceneDs);

                ds.DrawImage(_sceneTarget);
                _drawErrorLogged = false;
            }
            catch (Exception ex)
            {
                // A transient target/GPU error must never kill the frame: the
                // water reflection still reads the last good scene target, so
                // the foreground keeps drawing and the user sees a glitch
                // instead of a jump. Log once per failure streak.
                if (!_drawErrorLogged)
                {
                    _drawErrorLogged = true;
                    Log.Warn("NightCity.Draw: scene render skipped ({Ex})", ex.GetType().Name);
                }
            }

            DrawWater(ds);
            DrawHorizonStructures(ds);
            DrawBridge(ds);
        }

        private void RenderScene(CanvasDrawingSession ds)
        {
            float horizonY = _height * HorizonFraction;

            if (_skyBrush == null) CreateSkyBrush();
            if (_skyBrush != null)
                ds.FillRectangle(0, 0, _width, horizonY, _skyBrush);
            else
                ds.Clear(Color.FromArgb(255, 1, 2, 8));

            ds.FillRectangle(0, horizonY, _width, _height - horizonY, Color.FromArgb(255, 4, 7, 13));

            DrawMoon(ds);
            DrawStars(ds);
            DrawClouds(ds);
            DrawCityGlow(ds, horizonY);
            DrawHills(ds, horizonY);
            DrawBuildings(ds, horizonY);
            DrawShootingStars(ds);
        }

        private void CreateSkyBrush()
        {
            // Near-black zenith fading to a slightly warmer, slightly lighter
            // band near the horizon -- reads as atmosphere, not a hard line.
            var stops = new CanvasGradientStop[]
            {
                new CanvasGradientStop { Position = 0f,   Color = Color.FromArgb(255, 1, 2, 7) },
                new CanvasGradientStop { Position = 0.55f, Color = Color.FromArgb(255, 3, 5, 13) },
                new CanvasGradientStop { Position = 1f,   Color = Color.FromArgb(255, 8, 10, 22) }
            };
            _skyBrush = new CanvasLinearGradientBrush(_device, stops);
        }

        private void DrawMoon(CanvasDrawingSession ds)
        {
            float cx = _width * MoonXFraction;
            float cy = _height * 0.15f;
            float r = _height * 0.055f;

            var haloStops = new CanvasGradientStop[]
            {
                new CanvasGradientStop { Position = 0f, Color = Color.FromArgb(70, 205, 214, 255) },
                new CanvasGradientStop { Position = 1f, Color = Color.FromArgb(0, 200, 212, 255) }
            };
            using (var halo = new CanvasRadialGradientBrush(_device, haloStops, CanvasEdgeBehavior.Clamp, CanvasAlphaMode.Premultiplied)
            {
                Center = new Vector2(cx, cy),
                RadiusX = r * 3.4f,
                RadiusY = r * 3.4f
            })
            {
                ds.FillEllipse(cx, cy, r * 3.4f, r * 3.4f, halo);
            }

            ds.FillEllipse(cx, cy, r, r, Color.FromArgb(255, 224, 229, 240));

            // Cheap crater texture instead of the old fake terminator gradient
            // (that gradient didn't correspond to any real light direction).
            DrawCrater(ds, cx - r * 0.30f, cy - r * 0.20f, r * 0.16f);
            DrawCrater(ds, cx + r * 0.25f, cy + r * 0.15f, r * 0.11f);
            DrawCrater(ds, cx + r * 0.05f, cy - r * 0.35f, r * 0.08f);
        }

        private void DrawCrater(CanvasDrawingSession ds, float cx, float cy, float r)
        {
            ds.FillEllipse(cx, cy, r, r, Color.FromArgb(28, 150, 160, 180));
        }

        private void DrawStars(CanvasDrawingSession ds)
        {
            EnsureGlowSprite();
            float twinkleBase = 0.55f + _smoothTreble * 0.45f;

            for (int i = 0; i < StarCount; i++)
            {
                float tw = 0.65f + 0.35f * MathF.Sin(_time * _starSpeed[i] + _starPhase[i]);
                float a = Math.Clamp(twinkleBase * tw, 0.04f, 1f);
                if (a < 0.06f) continue;

                // Near-stationary: stars drift a hair (unbounded accumulator,
                // so no snap-back when building layers wrap around).
                float x = ((_starX[i] + _starDrift) % 1f) * _width;
                float y = _starY[i] * _height;
                float s = _starSize[i] * 3.2f;
                byte alpha = (byte)(a * 220);

                if (_glowSprite != null)
                {
                    var tint = new Vector4(alpha / 255f, alpha / 255f, alpha / 255f, alpha / 255f);
                    ds.DrawImage(_glowSprite,
                        new Rect(x - s * 0.5f, y - s * 0.5f, s, s),
                        new Rect(0, 0, _glowSprite.SizeInPixels.Width, _glowSprite.SizeInPixels.Height),
                        alpha / 255f, CanvasImageInterpolation.Linear);
                }
            }
        }

        /// <summary>
        /// Single soft round radial-gradient sprite, reused (tinted) for both
        /// star glints and window lights. Replaces the old 1x1 hard-edged
        /// _windowSprite which produced blocky, non-antialiased squares.
        /// </summary>
        private void EnsureGlowSprite()
        {
            if (_glowSprite != null || _device == null) return;
            _glowSprite = new CanvasRenderTarget(_device, 32, 32, 96);
            using (var s = _glowSprite.CreateDrawingSession())
            {
                s.Clear(Color.FromArgb(0, 0, 0, 0));
                var stops = new CanvasGradientStop[]
                {
                    new CanvasGradientStop { Position = 0f,   Color = Color.FromArgb(255, 255, 255, 255) },
                    new CanvasGradientStop { Position = 0.5f, Color = Color.FromArgb(140, 255, 255, 255) },
                    new CanvasGradientStop { Position = 1f,   Color = Color.FromArgb(0, 255, 255, 255) }
                };
                using (var brush = new CanvasRadialGradientBrush(_device, stops, CanvasEdgeBehavior.Clamp, CanvasAlphaMode.Premultiplied)
                {
                    Center = new Vector2(16, 16),
                    RadiusX = 16,
                    RadiusY = 16
                })
                {
                    s.FillEllipse(16, 16, 16, 16, brush);
                }
            }
        }

        private void EnsureCloudSprite()
        {
            if (_cloudSprite != null || _device == null) return;
            _cloudSprite = new CanvasRenderTarget(_device, 256, 128, 96);
            using (var s = _cloudSprite.CreateDrawingSession())
            {
                s.Clear(Color.FromArgb(0, 0, 0, 0));
                DrawPuff(s, 100, 72, 80, 40, 200);
                DrawPuff(s, 152, 80, 60, 32, 175);
                DrawPuff(s, 68, 58, 56, 28, 150);
                DrawPuff(s, 128, 52, 46, 24, 155);
                DrawPuff(s, 88, 88, 52, 26, 125);
                DrawPuff(s, 164, 56, 42, 22, 125);
                DrawPuff(s, 116, 40, 32, 16, 130);
            }
        }

        private void DrawPuff(CanvasDrawingSession s, float cx, float cy, float rx, float ry, byte peak)
        {
            var stops = new CanvasGradientStop[]
            {
                new CanvasGradientStop { Position = 0f,    Color = Color.FromArgb(peak, 228, 234, 255) },
                new CanvasGradientStop { Position = 0.55f, Color = Color.FromArgb((byte)(peak * 0.45), 204, 214, 244) },
                new CanvasGradientStop { Position = 1f,    Color = Color.FromArgb(0, 200, 212, 245) }
            };
            using (var brush = new CanvasRadialGradientBrush(_device, stops, CanvasEdgeBehavior.Clamp, CanvasAlphaMode.Premultiplied)
            {
                Center = new Vector2(cx, cy),
                RadiusX = rx,
                RadiusY = ry
            })
            {
                s.FillEllipse(cx, cy, rx, ry, brush);
            }
        }

        private void DrawClouds(CanvasDrawingSession ds)
        {
            EnsureCloudSprite();
            if (_cloudSprite == null) return;

            for (int i = 0; i < CloudPuffCount; i++)
            {
                float cx = _cloudX[i] * _width;
                float cy = _cloudY[i] * _height;
                float w = _cloudS[i] * _height * 0.16f;
                float h = w * 0.5f;
                float a = Math.Clamp(_cloudA[i] + _smoothBass * 0.05f, 0.02f, 0.42f);

                ds.DrawImage(_cloudSprite,
                    new Rect(cx - w * 0.5f, cy - h * 0.5f, w, h),
                    new Rect(0, 0, 256, 128),
                    a, CanvasImageInterpolation.Linear);
            }
        }

        /// <summary>
        /// Faint warm/blue glow band sitting right above the skyline silhouette,
        /// standing in for city light pollution without drawing literal cars or
        /// streetlights. Pulses gently with bass.
        /// </summary>
        private void DrawCityGlow(CanvasDrawingSession ds, float horizonY)
        {
            float bandH = _height * 0.10f;
            byte a = (byte)Math.Clamp(18 + _smoothBass * 26, 0, 60);
            var glow = new CanvasLinearGradientBrush(ds,
                Color.FromArgb(0, 40, 34, 70),
                Color.FromArgb(a, 60, 46, 90))
            {
                StartPoint = new Vector2(0, horizonY - bandH),
                EndPoint = new Vector2(0, horizonY)
            };
            ds.FillRectangle(0, horizonY - bandH, _width, bandH, glow);
            glow.Dispose();
        }

        private void DrawHills(CanvasDrawingSession ds, float horizonY)
        {
            DrawHillLayer(ds, horizonY, _height * 0.11f, 0.31f, 0.13f, Color.FromArgb(255, 10, 14, 28));
            DrawHillLayer(ds, horizonY, _height * 0.07f, 0.47f, 0.29f, Color.FromArgb(255, 7, 9, 18));
        }

        private void DrawHillLayer(CanvasDrawingSession ds, float horizonY, float amp, float freq, float phase, Color col)
        {
            int seg = 64;
            float colW = _width / seg;
            for (int c = 0; c < seg; c++)
            {
                float t = (c + 0.5f) / seg;
                float n = MathF.Sin(t * freq * MathF.PI * 2f + phase) * 0.5f
                        + MathF.Sin(t * freq * 2.7f + phase * 1.7f) * 0.3f;
                float hh = horizonY - amp * (0.45f + n);
                ds.FillRectangle(c * colW, hh, colW + 0.6f, horizonY - hh, col);
            }
        }

        public void Resize(float width, float height)
        {
            // NEVER dispose here: Resize runs on the UI thread while Draw runs
            // on the render thread, so disposing _sceneTarget mid-draw raced
            // with DrawWater's GaussianBlur (Source = _sceneTarget ->
            // "Effect source #0 is null") and the earlier DrawImage E_INVALIDARG.
            // All disposal is deferred to the render thread: EnsureSceneTarget
            // (size mismatch), GenerateSkyline + BakeWindowMasks (_skylineDirty),
            // and Dispose(). The sprites are fixed-size and the sky brush is
            // size-independent, so nothing else needs to happen here.
            _width = width;
            _height = height;
            _skylineDirty = true;
        }

        private void EnsureSceneTarget()
        {
            // Clamp invalid/transient sizes: a zero- or NaN-sized render target
            // makes DrawImage throw E_INVALIDARG ("Value does not fall within
            // the expected range") and kills the whole frame.
            float w = float.IsNaN(_width) || _width <= 0f ? 1f : _width;
            float h = float.IsNaN(_height) || _height <= 0f ? 1f : _height;
            const float epsilon = 0.5f;
            bool sizeMismatch = _sceneTarget == null
                || MathF.Abs((float)_sceneTarget.Size.Width - w) > epsilon
                || MathF.Abs((float)_sceneTarget.Size.Height - h) > epsilon;

            if (sizeMismatch)
            {
                _sceneTarget?.Dispose();
                _sceneTarget = new CanvasRenderTarget(_device, w, h, 96);
            }
        }

        private void GenerateSkyline()
        {
            if (_rng == null) _rng = new Random();
            _buildingCount = 0;
            DisposeWindowMasks();

            // Fixed seed: the city must regenerate IDENTICALLY on every resize,
            // otherwise a SizeChanged reshuffles the whole skyline (visible
            // "jump"). Building layout is pure function of this seed.
            var rng = new Random(0x5EED);
            BuildLayer(rng, 28, 0.10f, 0.26f, 0.026f, 0.052f, 0, gapChance: 0.06f);
            BuildLayer(rng, 20, 0.20f, 0.46f, 0.040f, 0.075f, 1, gapChance: 0.09f);
            BuildLayer(rng, 13, 0.36f, 0.72f, 0.058f, 0.105f, 2, gapChance: 0.14f);

            BakeWindowMasks();
        }

        private void BuildLayer(Random rng, int maxCount, float hLo, float hHi, float minW, float maxW, int layer, float gapChance)
        {
            float cursor = -0.02f;
            int built = 0;
            while (cursor < 1.05f && built < maxCount && _buildingCount < MaxBuildings)
            {
                float w = minW + (float)rng.NextDouble() * (maxW - minW);

                if (rng.NextDouble() < gapChance)
                {
                    // Skip a slot: leaves visible sky/hill between buildings.
                    cursor += w * (0.6f + (float)rng.NextDouble() * 0.6f);
                    continue;
                }

                float h = hLo + (float)rng.NextDouble() * (hHi - hLo);
                AddBuilding(cursor, w, h, layer);
                cursor += w * (0.95f + (float)rng.NextDouble() * 0.25f);
                built++;
            }
        }

        private void AddBuilding(float baseX, float w, float h, int layer)
        {
            int i = _buildingCount;
            if (i >= MaxBuildings) return;
            _bX[i] = baseX;
            _bW[i] = w;
            _bH[i] = h;
            _bLayer[i] = layer;
            _buildingCount++;
        }

        private void DrawBuildings(CanvasDrawingSession ds, float horizonY)
        {
            for (int layer = 0; layer < LayerCount; layer++)
            {
                for (int i = 0; i < _buildingCount; i++)
                {
                    if (_bLayer[i] != layer) continue;
                    DrawBuilding(ds, i, horizonY, layer);
                }
            }
        }

        private void DrawBuilding(CanvasDrawingSession ds, int i, float horizonY, int layer)
        {
            float w = _bW[i] * _width;
            float h = _bH[i] * horizonY;
            float fx = (_bX[i] + _layerOffset[layer]) % 1f;
            if (fx < 0f) fx += 1f;
            float x = fx * _width;

            Color body;
            if (layer == 0) body = Color.FromArgb(255, 9, 11, 22);
            else if (layer == 1) body = Color.FromArgb(255, 5, 6, 14);
            else body = Color.FromArgb(255, 2, 2, 6);

            DrawBuildingBody(ds, i, x, w, h, horizonY, layer, body);

            if (fx + _bW[i] > 1f)
                DrawBuildingBody(ds, i, x - _width, w, h, horizonY, layer, body);
        }

        private enum BodyStyle { Slab, Tiered, TwinTower, Tapered, Setback }

        /// <summary>
        /// Picks a massing archetype per building (deterministic via hash, so
        /// wraparound copies of the same building match) instead of always
        /// drawing a single rectangle. Narrow far-layer buildings fall back to
        /// Slab -- multi-part silhouettes turn to mush below ~10px wide.
        /// </summary>
        private void DrawBuildingBody(CanvasDrawingSession ds, int i, float x, float w, float h, float horizonY, int layer, Color body)
        {
            float y = horizonY - h;
            float roll = Hash01(i, 6, 6, 777);

            BodyStyle style =
                w < 10f ? BodyStyle.Slab :
                roll < 0.32f ? BodyStyle.Slab :
                roll < 0.54f ? BodyStyle.Tiered :
                roll < 0.70f ? BodyStyle.TwinTower :
                roll < 0.86f ? BodyStyle.Tapered :
                BodyStyle.Setback;

            switch (style)
            {
                case BodyStyle.Tiered: DrawTiered(ds, i, x, w, h, horizonY, layer, body); break;
                case BodyStyle.TwinTower: DrawTwinTower(ds, i, x, w, h, horizonY, layer, body); break;
                case BodyStyle.Tapered: DrawTapered(ds, i, x, w, h, horizonY, layer, body); break;
                case BodyStyle.Setback: DrawSetbackTower(ds, i, x, w, h, horizonY, layer, body); break;
                default:
                    ds.FillRectangle(x, y, w, h, body);
                    ShadeRect(ds, x, y, w, h, layer);
                    DrawRoof(ds, i, x, y, w, h, horizonY, layer, body);
                    break;
            }

            DrawWindowMasks(ds, i, x, w, h, horizonY, layer);
        }

        /// <summary>
        /// Per-piece depth treatment, applied only inside a building's actual
        /// massing rect (tier/tower/base), never the bounding box: the face
        /// pointing at the moon is lit (soft band + edge highlight), the far
        /// face falls into shadow, plus base ambient occlusion. Disable via
        /// ShadingStrength = 0.
        /// </summary>
        private static float ShadingForLayer(int layer)
        {
            float depth = layer == 0 ? ShadingDepthFar : layer == 1 ? ShadingDepthMid : ShadingDepthNear;
            return depth * ShadingStrength;
        }

        private void ShadeRect(CanvasDrawingSession ds, float rx, float ry, float rw, float rh, int layer)
        {
            if (ShadingStrength <= 0f || rw < 3f || rh < 3f) return;
            float depth = ShadingForLayer(layer);

            // Smooth moon lighting: side weight ramps with signed distance to
            // the moon, so a building passing underneath crossfades from one
            // lit face to the other (front-facing = no sheen) instead of
            // hard-flipping. No per-frame allocations (brushes reused).
            float moonX = _width * MoonXFraction;
            float u = (moonX - (rx + rw * 0.5f)) / Math.Max(rw, 8f);
            float rightK = Math.Clamp(u * 1.2f, 0f, 1f); // moon to the right
            float leftK = Math.Clamp(-u * 1.2f, 0f, 1f); // moon to the left

            float lightW = Math.Min(rw * 0.22f, 40f);
            if (_shadeSide == null)
                _shadeSide = new CanvasLinearGradientBrush(ds,
                    Color.FromArgb(46, 130, 158, 205),
                    Color.FromArgb(0, 130, 158, 205));
            if (rightK > 0.02f)
            {
                _shadeSide.Opacity = depth * rightK;
                _shadeSide.StartPoint = new Vector2(rx + rw, 0);
                _shadeSide.EndPoint = new Vector2(rx + rw - lightW, 0);
                ds.FillRectangle(rx + rw - lightW, ry, lightW, rh, _shadeSide);
                ds.DrawLine(rx + rw, ry, rx + rw, ry + rh,
                    Color.FromArgb((byte)(85 * depth * rightK), 140, 168, 214), 1f);
            }
            if (leftK > 0.02f)
            {
                _shadeSide.Opacity = depth * leftK;
                _shadeSide.StartPoint = new Vector2(rx, 0);
                _shadeSide.EndPoint = new Vector2(rx + lightW, 0);
                ds.FillRectangle(rx, ry, lightW, rh, _shadeSide);
                ds.DrawLine(rx, ry, rx, ry + rh,
                    Color.FromArgb((byte)(85 * depth * leftK), 140, 168, 214), 1f);
            }

            // Shadow on the face AWAY from the moon, weighted the same way.
            float darkW = Math.Min(rw * 0.30f, 50f);
            if (_shadeDark == null)
                _shadeDark = new CanvasLinearGradientBrush(ds,
                    Color.FromArgb(0, 0, 0, 0),
                    Color.FromArgb(80, 0, 0, 0));
            if (rightK > 0.02f)
            {
                _shadeDark.Opacity = depth * rightK;
                _shadeDark.StartPoint = new Vector2(rx, 0);
                _shadeDark.EndPoint = new Vector2(rx + darkW, 0);
                ds.FillRectangle(rx, ry, darkW, rh, _shadeDark);
            }
            if (leftK > 0.02f)
            {
                _shadeDark.Opacity = depth * leftK;
                _shadeDark.StartPoint = new Vector2(rx + rw - darkW, 0);
                _shadeDark.EndPoint = new Vector2(rx + rw, 0);
                ds.FillRectangle(rx + rw - darkW, ry, darkW, rh, _shadeDark);
            }

            float aoH = rh * 0.32f;
            if (_shadeAo == null)
                _shadeAo = new CanvasLinearGradientBrush(ds,
                    Color.FromArgb(120, 0, 0, 0),
                    Color.FromArgb(0, 0, 0, 0));
            _shadeAo.StartPoint = new Vector2(0, ry + rh);
            _shadeAo.EndPoint = new Vector2(0, ry + rh - aoH);
            _shadeAo.Opacity = depth;
            ds.FillRectangle(rx, ry + rh - aoH, rw, aoH, _shadeAo);
        }

        /// <summary>Wedding-cake silhouette: 2-3 rectangles stacked and centered, narrowing upward.</summary>
        private void DrawTiered(CanvasDrawingSession ds, int i, float x, float w, float h, float horizonY, int layer, Color body)
        {
            bool threeTiers = Hash01(i, 7, 7, 321) > 0.5f;
            float[] hFrac = threeTiers ? new[] { 0.50f, 0.30f, 0.20f } : new[] { 0.62f, 0.38f };
            float[] wFrac = threeTiers ? new[] { 1.00f, 0.66f, 0.42f } : new[] { 1.00f, 0.58f };

            float baseY = horizonY;
            float topX = x, topW = w;
            for (int t = 0; t < hFrac.Length; t++)
            {
                float tierH = Math.Max(4f, h * hFrac[t]);
                float tierW = w * wFrac[t];
                float tierX = x + (w - tierW) * 0.5f;
                float tierY = baseY - tierH;

                ds.FillRectangle(tierX, tierY, tierW, tierH, body);
                ShadeRect(ds, tierX, tierY, tierW, tierH, layer);

                baseY = tierY;
                topX = tierX; topW = tierW;
            }
            DrawRoof(ds, i, topX, baseY, topW, h, horizonY, layer, body);
        }

        /// <summary>Two adjacent slim towers of slightly different heights in one building slot.</summary>
        private void DrawTwinTower(CanvasDrawingSession ds, int i, float x, float w, float h, float horizonY, int layer, Color body)
        {
            float gap = Math.Max(1.5f, w * 0.14f);
            float towerW = (w - gap) * 0.5f;
            float leftH = h * (0.68f + Hash01(i, 8, 8, 111) * 0.22f);
            float rightH = h;

            float leftX = x, leftY = horizonY - leftH;
            ds.FillRectangle(leftX, leftY, towerW, leftH, body);
            ShadeRect(ds, leftX, leftY, towerW, leftH, layer);
            DrawRoof(ds, i * 10 + 1, leftX, leftY, towerW, leftH, horizonY, layer, body);

            float rightX = x + towerW + gap, rightY = horizonY - rightH;
            ds.FillRectangle(rightX, rightY, towerW, rightH, body);
            ShadeRect(ds, rightX, rightY, towerW, rightH, layer);
            DrawRoof(ds, i * 10 + 2, rightX, rightY, towerW, rightH, horizonY, layer, body);
        }

        /// <summary>Rectangular base capped by a triangular apex (pyramid-top skyscraper).</summary>
        private void DrawTapered(CanvasDrawingSession ds, int i, float x, float w, float h, float horizonY, int layer, Color body)
        {
            float baseH = h * 0.76f;
            float baseY = horizonY - baseH;
            ds.FillRectangle(x, baseY, w, baseH, body);
            ShadeRect(ds, x, baseY, w, baseH, layer);

            float apexX = x + w * 0.5f;
            float apexY = baseY - (h - baseH);
            FillTriangle(ds, new Vector2(x, baseY), new Vector2(x + w, baseY), new Vector2(apexX, apexY), body);
            float depth = ShadingForLayer(layer);
            if (depth > 0f)
            {
                float moonX = _width * MoonXFraction;
                float u = (moonX - apexX) / Math.Max(w, 8f);
                float rightK = Math.Clamp(u * 1.2f, 0f, 1f);
                float leftK = Math.Clamp(-u * 1.2f, 0f, 1f);
                if (rightK > 0.02f)
                    ds.DrawLine(x + w, baseY, apexX, apexY,
                        Color.FromArgb((byte)(70 * depth * rightK), 140, 168, 214), 1f);
                if (leftK > 0.02f)
                    ds.DrawLine(x, baseY, apexX, apexY,
                        Color.FromArgb((byte)(70 * depth * leftK), 140, 168, 214), 1f);
            }

            if (Hash01(i, 9, 9, 555) > 0.7f)
                ds.DrawLine(apexX, apexY, apexX, apexY - 14f, Color.FromArgb(255, 2, 3, 6), 1.2f);
        }

        /// <summary>Wide low base plus a narrower full-height tower offset to one side (asymmetric, not centered).</summary>
        private void DrawSetbackTower(CanvasDrawingSession ds, int i, float x, float w, float h, float horizonY, int layer, Color body)
        {
            float mainH = h * (0.52f + Hash01(i, 10, 10, 222) * 0.16f);
            float mainY = horizonY - mainH;
            ds.FillRectangle(x, mainY, w, mainH, body);
            ShadeRect(ds, x, mainY, w, mainH, layer);

            float towerW = w * (0.34f + Hash01(i, 11, 11, 333) * 0.24f);
            bool left = Hash01(i, 12, 12, 444) > 0.5f;
            float margin = w * 0.06f;
            float towerX = left ? x + margin : x + w - towerW - margin;
            float towerY = horizonY - h;
            ds.FillRectangle(towerX, towerY, towerW, h, body);
            ShadeRect(ds, towerX, towerY, towerW, h, layer);
            DrawRoof(ds, i * 10 + 4, towerX, towerY, towerW, h, horizonY, layer, body);
        }

        private void FillTriangle(CanvasDrawingSession ds, Vector2 a, Vector2 b, Vector2 c, Color color)
        {
            using (var pb = new CanvasPathBuilder(ds))
            {
                pb.BeginFigure(a);
                pb.AddLine(b);
                pb.AddLine(c);
                pb.EndFigure(CanvasFigureLoop.Closed);
                using (var geo = CanvasGeometry.CreatePath(pb))
                    ds.FillGeometry(geo, color);
            }
        }

        private void DrawRoof(CanvasDrawingSession ds, int i, float x, float y, float w, float h, float horizonY, int layer, Color body)
        {
            // Roof details read as a slightly lighter slate than the black
            // building body, with a bright top edge so towers/water tanks stand
            // out against the dark sky instead of vanishing into it.
            Color roofDark = Color.FromArgb(255, 14, 20, 34);
            Color roofEdge = Color.FromArgb(255, 66, 84, 118);
            float roofR = Hash01(i, 1, 2, 5);

            // Even spread across ten archetypes so the skyline is never "a wall
            // of rectangles with antennas". Tall buildings favor needle roofs;
            // wide ones favor billboards/crenellation/tank farms.
            RoofStyle roof;
            if (h > horizonY * 0.55f)
                roof = roofR < 0.50f ? RoofStyle.Spire :
                       roofR < 0.85f ? RoofStyle.Antenna : RoofStyle.CellTower;
            else if (w > _width * 0.08f)
                roof = roofR < 0.35f ? RoofStyle.Billboard :
                       roofR < 0.70f ? RoofStyle.Crenellation :
                       roofR < 0.85f ? RoofStyle.WaterTower : RoofStyle.Tanks;
            else if (roofR < 0.16f) roof = RoofStyle.Flat;
            else if (roofR < 0.32f) roof = RoofStyle.Setback;
            else if (roofR < 0.46f) roof = RoofStyle.Dome;
            else if (roofR < 0.60f) roof = RoofStyle.Dish;
            else if (roofR < 0.72f) roof = RoofStyle.Crenellation;
            else if (roofR < 0.82f) roof = RoofStyle.WaterTower;
            else if (roofR < 0.92f) roof = RoofStyle.Tanks;
            else roof = RoofStyle.CellTower;

            switch (roof)
            {
                case RoofStyle.Antenna:
                {
                    float antX = x + w * 0.5f;
                    float antH = Math.Max(14f, h * 0.08f);
                    ds.DrawLine(antX, y, antX, y - antH, roofDark, 1.8f);
                    float armW = Math.Max(3f, w * 0.05f);
                    ds.DrawLine(antX - armW, y - antH * 0.6f, antX + armW, y - antH * 0.6f, roofDark, 1.2f);
                    DrawBeacon(ds, antX, y - antH, i, layer, 1.6f);
                    break;
                }

                case RoofStyle.Spire:
                {
                    float spX = x + w * 0.5f;
                    float spH = Math.Max(16f, h * 0.11f);
                    ds.DrawLine(spX, y, spX, y - spH, roofDark, 1.6f);
                    ds.FillEllipse(spX, y - spH, w * 0.02f, w * 0.02f, roofEdge);
                    DrawBeacon(ds, spX, y - spH, i, layer, 1.4f);
                    break;
                }

                case RoofStyle.Setback:
                {
                    float sw = w * 0.55f;
                    float sh = Math.Max(7f, h * 0.05f);
                    ds.FillRectangle(x + (w - sw) * 0.5f, y - sh, sw, sh, body);
                    ds.DrawLine(x + (w - sw) * 0.5f, y - sh, x + (w + sw) * 0.5f, y - sh, roofEdge, 1f);
                    float sw2 = w * 0.32f;
                    ds.FillRectangle(x + (w - sw2) * 0.5f, y - sh * 1.9f, sw2, sh * 0.9f, body);
                    ds.DrawLine(x + (w - sw2) * 0.5f, y - sh * 1.9f, x + (w + sw2) * 0.5f, y - sh * 1.9f, roofEdge, 1f);
                    DrawBeacon(ds, x + w * 0.5f, y - sh * 1.9f, i, layer, 1.3f);
                    break;
                }

                case RoofStyle.WaterTower:
                {
                    float wtW = Math.Max(8f, w * 0.26f);
                    float wtH = Math.Max(10f, h * 0.06f);
                    float wtx = x + w * 0.5f - wtW * 0.5f;
                    ds.DrawLine(wtx + 1f, y, wtx + 1f, y - wtH, roofDark, 1.4f);
                    ds.DrawLine(wtx + wtW - 1f, y, wtx + wtW - 1f, y - wtH, roofDark, 1.4f);
                    ds.FillRectangle(wtx, y - wtH, wtW, wtH * 0.75f, roofDark);
                    ds.FillEllipse(wtx + wtW * 0.5f, y - wtH, wtW * 0.5f, wtW * 0.28f, roofEdge);
                    DrawBeacon(ds, wtx + wtW * 0.5f, y - wtH - wtW * 0.2f, i, layer, 1.3f);
                    break;
                }

                case RoofStyle.Dome:
                    ds.FillEllipse(x + w * 0.5f, y, w * 0.32f, w * 0.16f, body);
                    ds.DrawLine(x + w * 0.5f, y - w * 0.16f, x + w * 0.5f, y - w * 0.16f - 2.5f, roofEdge, 1.2f);
                    DrawBeacon(ds, x + w * 0.5f, y - w * 0.16f, i, layer, 1.3f);
                    break;

                case RoofStyle.Dish:
                {
                    float dX = x + w * 0.5f;
                    float dH = Math.Max(8f, h * 0.07f);
                    ds.DrawLine(dX, y, dX, y - dH, roofDark, 1.6f);
                    ds.FillEllipse(dX, y - dH, w * 0.18f, dH * 0.6f, roofDark);
                    ds.DrawLine(dX, y - dH, dX, y - dH - dH * 0.4f, roofDark, 1.2f);
                    DrawBeacon(ds, dX, y - dH - dH * 0.4f, i, layer, 1.4f);
                    break;
                }

                case RoofStyle.Billboard:
                {
                    float bH = Math.Max(4f, h * 0.03f);
                    float bW = w * 0.72f;
                    ds.FillRectangle(x + (w - bW) * 0.5f, y - bH, bW, bH, roofDark);
                    ds.DrawLine(x + (w - bW) * 0.5f, y - bH, x + (w + bW) * 0.5f, y - bH, roofEdge, 1.2f);
                    // Neon edge reacts to the beat.
                    float neon = 0.25f + _smoothBeat * 0.75f;
                    ds.FillRectangle(x + (w - bW) * 0.5f, y - bH, bW, 1.6f,
                        Color.FromArgb((byte)(220 * neon), 110, 160, 255));
                    break;
                }

                case RoofStyle.Crenellation:
                {
                    float ch = Math.Max(5f, h * 0.04f);
                    float cw = Math.Max(2f, w * 0.08f);
                    float gap = Math.Max(1f, cw * 0.45f);
                    for (float px = x + 1f; px < x + w - 1f; px += cw + gap)
                    {
                        ds.FillRectangle(px, y - ch, cw, ch, roofDark);
                        ds.DrawLine(px, y - ch, px + cw, y - ch, roofEdge, 0.9f);
                    }
                    break;
                }

                case RoofStyle.Tanks:
                {
                    float tw = Math.Max(4f, w * 0.14f);
                    float th = Math.Max(7f, h * 0.06f);
                    float tx = x + w * 0.5f - tw;
                    ds.FillRectangle(tx, y - th, tw, th, roofDark);
                    ds.FillEllipse(tx + tw * 0.5f, y - th, tw * 0.5f, tw * 0.5f, roofEdge);
                    DrawBeacon(ds, tx + tw * 0.5f, y - th - tw * 0.4f, i, layer, 1.3f);
                    tx += tw * 1.6f;
                    ds.FillRectangle(tx, y - th * 0.8f, tw, th * 0.8f, roofDark);
                    ds.FillEllipse(tx + tw * 0.5f, y - th * 0.8f, tw * 0.5f, tw * 0.5f, roofEdge);
                    break;
                }

                case RoofStyle.CellTower:
                {
                    // Lattice mast: triangular taper with X cross-bracing, on a
                    // minority of towers so the skyline reads as cell sites.
                    float ctH = Math.Max(20f, h * 0.16f);
                    float baseW = Math.Max(3f, w * 0.10f);
                    float ctX = x + w * 0.5f;
                    int segs = 6;
                    for (int s = 0; s < segs; s++)
                    {
                        float t0 = s / (float)segs;
                        float t1 = (s + 1) / (float)segs;
                        float y0 = y - ctH * t0;
                        float y1 = y - ctH * t1;
                        float w0 = baseW * (1f - t0 * 0.65f);
                        float w1 = baseW * (1f - t1 * 0.65f);
                        ds.DrawLine(ctX - w0, y0, ctX - w1, y1, roofDark, 0.8f);
                        ds.DrawLine(ctX + w0, y0, ctX + w1, y1, roofDark, 0.8f);
                        ds.DrawLine(ctX - w0, y0, ctX + w1, y1, roofDark, 0.6f);
                        ds.DrawLine(ctX + w0, y0, ctX - w1, y1, roofDark, 0.6f);
                    }
                    DrawBeacon(ds, ctX, y - ctH, i, layer, 1.5f);
                    break;
                }
            }
        }

        private void DrawBeacon(CanvasDrawingSession ds, float x, float y, int i, int layer, float size)
        {
            // Aviation warning light: red double-blink, dimmed for far layers.
            float phase = i * 1.7f;
            float flash = MathF.Sin(_time * 3.4f + phase);
            float on = flash > -0.25f ? 1f : 0.10f;
            if (on < 0.5f && MathF.Sin(_time * 3.4f + phase + MathF.PI) > -0.25f) on = 1f;
            float farDim = layer == 0 ? 0.55f : layer == 1 ? 0.8f : 1f;
            byte a = (byte)((150 + 105 * on) * farDim);
            float s = size * (0.8f + 0.6f * on);
            ds.FillEllipse(x, y, s * 3.2f, s * 3.2f, Color.FromArgb((byte)(a * 0.16f), 255, 64, 56));
            ds.FillEllipse(x, y, s * 1.5f, s * 1.5f, Color.FromArgb((byte)(a * 0.5f), 255, 80, 70));
            ds.FillEllipse(x, y, s, s, Color.FromArgb(a, 255, 96, 84));
        }

        /// <summary>
        /// Per-frame draw of a building's pre-baked window masks. Two texture
        /// draws total: the always-on mask (slow breathing opacity) and the
        /// reactive mask cropped bottom-up by a level that tracks the
        /// building's spectrum band, so the reactive windows behave as one
        /// grouped VU-meter bar filling with the music.
        /// </summary>
        private void DrawWindowMasks(CanvasDrawingSession ds, int i, float x, float w, float h, float horizonY, int layer)
        {
            var st = _winStatic[i];
            var re = _winReactive[i];
            if (st == null && re == null) return;

            float bottomY = horizonY;
            float topY = bottomY - h;
            if (topY >= bottomY) return;

            float visibleH = Math.Min(bottomY, _height) - Math.Max(topY, 0f);
            if (visibleH <= 0f) return;

            float maskW = st != null ? (float)st.Size.Width : (float)re.Size.Width;
            float maskH = st != null ? (float)st.Size.Height : (float)re.Size.Height;

            // Portion of the building clipped above the screen top.
            float hidden = Math.Max(0f, -topY);
            if (hidden >= h) return;

            float srcTop = hidden / h * maskH;
            float srcH = maskH - srcTop;
            float destTop = Math.Max(0f, topY);

            var destRect = new Rect(x, destTop, w, visibleH);
            var srcRect = new Rect(0, srcTop, maskW, srcH);

            if (st != null)
            {
                // Slow per-building breathing so the always-on lights feel alive.
                float breathe = 0.82f + 0.18f * (0.5f + 0.5f * MathF.Sin(
                    _time * (1.3f + Hash01(i, 33, 1, 7) * 1.4f) + i * 0.9f));
                ds.DrawImage(st, destRect, srcRect, breathe, CanvasImageInterpolation.Linear);
            }

            if (re != null)
            {
                int bandCount = _smoothBands.Length;
                int band = Math.Clamp((int)(Hash01(i, 4, 4, 4242) * bandCount), 0, bandCount - 1);
                float bandLevel = _smoothBands[band];
                float beatKick = (layer == LayerCount - 1 ? 0.65f : 0.20f) * _smoothBeat;
                float flick = 0.75f + 0.25f * MathF.Sin(_time * (1.0f + Hash01(i, 70, 1, 5) * 1.6f) + i * 1.7f);

                // VU meter bar: expose only the bottom `level` fraction of the
                // reactive mask, anchored to the horizon, so the lit windows
                // fill upward with the music. src/dest stay proportional, so
                // the window dots never stretch.
                float level = Math.Clamp((bandLevel * 1.5f + beatKick) * flick, 0.05f, 1f);
                float barH = maskH * level;
                var barSrc = new Rect(0, maskH - barH, maskW, barH);
                var barDest = new Rect(x, horizonY - h * level, w, h * level);
                ds.DrawImage(re, barDest, barSrc, 1f, CanvasImageInterpolation.Linear);
            }
        }

        /// <summary>
        /// Pre-bakes each building's lit-window pattern into two small masks:
        /// a static mask (always-on windows, ~60%, baked at full brightness)
        /// and a reactive mask (~40%, full-bright) drawn per-frame with an
        /// opacity gain so the whole group acts like one VU-meter bar pinned
        /// to the building's spectrum band. Replaces thousands of per-window
        /// sprite draws with two texture draws per building.
        /// </summary>
        private void BakeWindowMasks()
        {
            if (_device == null) return;

            for (int i = 0; i < _buildingCount; i++)
            {
                float wPx = _bW[i] * _width;
                float hPx = _bH[i] * (_height * HorizonFraction);
                if (wPx < 1f || hPx < 1f) continue;

                int mw = Math.Min((int)wPx, 1024);
                int mh = Math.Min((int)hPx, 1024);
                if (mw < 1 || mh < 1) continue;

                int layer = _bLayer[i];
                var pieces = GetWindowPieces(i, wPx, hPx);
                if (pieces.Count == 0) continue;

                _winStatic[i]?.Dispose();
                _winReactive[i]?.Dispose();

                // Build into locals and publish to the array only once both
                // targets exist, then draw the locals: a concurrent Dispose can
                // null the slot, but the locals keep this frame coherent.
                var winStatic = new CanvasRenderTarget(_device, mw, mh, 96);
                var winReactive = new CanvasRenderTarget(_device, mw, mh, 96);
                _winStatic[i] = winStatic;
                _winReactive[i] = winReactive;

                using (var s = winStatic.CreateDrawingSession())
                using (var r = winReactive.CreateDrawingSession())
                {
                    s.Clear(Color.FromArgb(0, 0, 0, 0));
                    r.Clear(Color.FromArgb(0, 0, 0, 0));

                    float sx = mw / wPx;
                    float sy = mh / hPx;
                    float gs = WindowGridStep[layer];
                    float litRatio = WindowLitBase[layer];

                    for (int p = 0; p < pieces.Count; p++)
                    {
                        var piece = pieces[p];
                        float pw = (float)piece.Width;
                        float ph = (float)piece.Height;
                        int cols = (int)Math.Clamp(pw / gs, 1f, 30f);
                        int rows = (int)Math.Clamp(ph / (gs * 1.1f), 1f, 30f);
                        if (cols < 1 || rows < 1) continue;

                        float cw = pw / cols;
                        float ch = ph / rows;

                        for (int row = 0; row < rows; row++)
                        {
                            float rowHash = Hash01(i, row, 3, 53);
                            float rowP = Math.Min(0.97f, litRatio * (0.55f + rowHash));

                            for (int col = 0; col < cols; col++)
                            {
                                float winHash = Hash01(i, col, row, 11);
                                if (winHash >= rowP) continue;

                                // Window cell center, building-local pixels (y from bottom).
                                float wx = (float)piece.X + col * cw + cw * 0.5f;
                                float wy = (float)piece.Y + row * ch + ch * 0.5f;

                                // Mask pixel coordinates (y from top).
                                float mx = wx * sx;
                                float my = (hPx - wy) * sy;

                                float flick = Hash01(i + 50, col, row, 29);
                                float warm = Hash01(i + 100, col, row, 37);

                                byte rr = warm > 0.3f ? (byte)(235 + warm * 20) : (byte)(170 + warm * 40);
                                byte gg = warm > 0.3f ? (byte)(175 + warm * 40) : (byte)(205 + warm * 40);
                                byte bb = warm > 0.3f ? (byte)(105 + warm * 30) : (byte)250;

                                float cell = Math.Min(cw, ch);
                                float radScale = WindowRadScale[layer];
                                // Per-window size jitter so windows read as
                                // individual fixtures, not a uniform grid.
                                float sizeJitter = 0.60f + 0.75f * Hash01(i + 7, col, row, 13);
                                float rad = cell * 0.28f * radScale * sizeJitter;
                                float glow = cell * 0.18f * radScale * sizeJitter;

                                // ~65% always on, ~35% reactive (grouped VU set).
                                // Reactive spread uniformly through the full
                                // building height so the bottom-up VU crop reads
                                // as lights climbing the whole facade, not a
                                // single band near the base.
                                bool reactive = Hash01(i + 200, col, row, 89) < 0.35f;
                                if (reactive)
                                {
                                    byte a = (byte)((0.55f + 0.45f * Hash01(i + 300, col, row, 41)) * 255);
                                    r.FillEllipse(mx, my, rad, rad, Color.FromArgb(a, rr, gg, bb));
                                    r.FillEllipse(mx, my, rad + glow, rad + glow, Color.FromArgb((byte)(a * 0.15f), rr, gg, bb));
                                    r.FillEllipse(mx, my, rad + glow * 2f, rad + glow * 2f, Color.FromArgb((byte)(a * 0.05f), rr, gg, bb));
                                }
                                else
                                {
                                    float idle = 0.85f + 0.15f * flick;
                                    byte a = (byte)((110 + 140 * flick) * idle * LayerAlphaFactor[layer]);
                                    s.FillEllipse(mx, my, rad, rad, Color.FromArgb(a, rr, gg, bb));
                                    s.FillEllipse(mx, my, rad + glow, rad + glow, Color.FromArgb((byte)(a * 0.15f), rr, gg, bb));
                                    s.FillEllipse(mx, my, rad + glow * 2f, rad + glow * 2f, Color.FromArgb((byte)(a * 0.05f), rr, gg, bb));
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Rectangular massing pieces per body style (building-local coords,
        /// y measured from the horizon upward). Mirrors the silhouette geometry
        /// so window masks only cover the actual built volume -- no floating
        /// lights beside a setback tier or in the twin-tower gap.
        /// </summary>
        private List<Rect> GetWindowPieces(int i, float w, float h)
        {
            var pieces = new List<Rect>(3);
            float roll = Hash01(i, 6, 6, 777);

            BodyStyle style =
                w < 10f ? BodyStyle.Slab :
                roll < 0.32f ? BodyStyle.Slab :
                roll < 0.54f ? BodyStyle.Tiered :
                roll < 0.70f ? BodyStyle.TwinTower :
                roll < 0.86f ? BodyStyle.Tapered :
                BodyStyle.Setback;

            switch (style)
            {
                case BodyStyle.Tiered:
                {
                    bool threeTiers = Hash01(i, 7, 7, 321) > 0.5f;
                    float[] hFrac = threeTiers ? new[] { 0.50f, 0.30f, 0.20f } : new[] { 0.62f, 0.38f };
                    float[] wFrac = threeTiers ? new[] { 1.00f, 0.66f, 0.42f } : new[] { 1.00f, 0.58f };
                    float baseY = 0f;
                    for (int t = 0; t < hFrac.Length; t++)
                    {
                        float tierH = Math.Max(4f, h * hFrac[t]);
                        float tierW = w * wFrac[t];
                        float tierX = (w - tierW) * 0.5f;
                        pieces.Add(new Rect(tierX, baseY, tierW, tierH));
                        baseY += tierH;
                    }
                    break;
                }

                case BodyStyle.TwinTower:
                {
                    float gap = Math.Max(1.5f, w * 0.14f);
                    float towerW = (w - gap) * 0.5f;
                    float leftH = h * (0.68f + Hash01(i, 8, 8, 111) * 0.22f);
                    pieces.Add(new Rect(0, 0, towerW, leftH));
                    pieces.Add(new Rect(towerW + gap, 0, towerW, h));
                    break;
                }

                case BodyStyle.Tapered:
                    pieces.Add(new Rect(0, 0, w, h * 0.76f));
                    break;

                case BodyStyle.Setback:
                {
                    float mainH = h * (0.52f + Hash01(i, 10, 10, 222) * 0.16f);
                    pieces.Add(new Rect(0, 0, w, mainH));
                    float towerW2 = w * (0.34f + Hash01(i, 11, 11, 333) * 0.24f);
                    bool left = Hash01(i, 12, 12, 444) > 0.5f;
                    float margin = w * 0.06f;
                    float towerX = left ? margin : w - towerW2 - margin;
                    pieces.Add(new Rect(towerX, 0, towerW2, h));
                    break;
                }

                default:
                    pieces.Add(new Rect(0, 0, w, h));
                    break;
            }

            return pieces;
        }

        private void DisposeWindowMasks()
        {
            for (int i = 0; i < MaxBuildings; i++)
            {
                _winStatic[i]?.Dispose();
                _winStatic[i] = null;
                _winReactive[i]?.Dispose();
                _winReactive[i] = null;
            }
        }

        private void DrawHorizonStructures(CanvasDrawingSession ds)
        {
            float horizonY = _height * HorizonFraction;
            Color shore = Color.FromArgb(255, 6, 9, 16);
            Color shoreEdge = Color.FromArgb(255, 16, 24, 40);
            Color lamp = Color.FromArgb(210, 255, 214, 140);

            // Scrolls with the camera but slower than the buildings: silhouettes
            // sit on the far bank, the avenue/railing/lamps/trees live at the
            // waterline. Positions are wrapped fractions so the pattern slides
            // seamlessly. Shared _fgOffset keeps the whole waterline band glued
            // together as one plane.
            float nearOx = _fgOffset;

            // Distant shoreline silhouettes across the whole horizon.
            int nBuild = 30;
            for (int k = 0; k < nBuild; k++)
            {
                int seed = 500 + k;
                float fx = ((k + Hash01(seed, 1, 1, 71)) / nBuild + nearOx) % 1f;
                if (fx < 0f) fx += 1f;
                float x = fx * _width;
                float w = _width * (0.010f + Hash01(seed, 2, 2, 72) * 0.022f);
                float h = horizonY * (0.012f + Hash01(seed, 3, 3, 73) * 0.05f);
                ds.FillRectangle(x, horizonY - h, w, h, shore);
                if (Hash01(seed, 4, 4, 74) > 0.55f)
                    ds.FillRectangle(x + w * 0.5f, horizonY - h, 1f, 1.5f, shoreEdge);
                if (Hash01(seed, 5, 5, 75) > 0.6f)
                    ds.FillRectangle(x + w * 0.3f, horizonY - h * 0.55f, w * 0.12f, 1.2f,
                        Color.FromArgb((byte)(80 + 120 * Hash01(seed, 6, 6, 76)), 255, 214, 150));
            }

            // Avenue surface at the waterline spanning the full horizon.
            ds.FillRectangle(0, horizonY, _width, _height * 0.014f, Color.FromArgb(255, 9, 13, 22));

            // Railing grid across the whole horizon -- same geometry as the
            // bridge guardrail so the two blend into one continuous fence.
            // Rails stay full-width; only the pickets scroll.
            float deckH2 = Math.Max(2f, _height * 0.006f);
            float railTop = horizonY - deckH2 - _height * 0.010f;
            Color rail = Color.FromArgb(255, 34, 46, 68);
            ds.DrawLine(0, horizonY - deckH2, _width, horizonY - deckH2, rail, 1.2f);
            ds.DrawLine(0, railTop, _width, railTop, rail, 1.4f);
            float railPicket = Math.Max(_height * 0.018f, 5f);
            float picketFrac = railPicket / _width;
            for (float p = picketFrac * 0.5f; p < 1f; p += picketFrac)
            {
                float px = ((p + nearOx) % 1f + 1f) % 1f * _width;
                ds.DrawLine(px, horizonY - deckH2, px, railTop, rail, 1f);
            }

            // Avenue street lamps across the whole width.
            int nLamps = 11;
            for (int k = 0; k < nLamps; k++)
            {
                int seed = 600 + k;
                float fx = ((k + 0.5f) / nLamps + nearOx) % 1f;
                if (fx < 0f) fx += 1f;
                float lx = fx * _width;
                float postH = _height * (0.016f + Hash01(seed, 7, 7, 77) * 0.008f);
                ds.DrawLine(lx, horizonY, lx, horizonY - postH, Color.FromArgb(255, 16, 22, 36), 1.1f);
                float gl = 0.7f + 0.3f * MathF.Sin(_time * 2.0f + seed);
                byte la = (byte)(150 + 105 * gl);
                ds.FillEllipse(lx, horizonY - postH, _height * 0.003f, _height * 0.003f, lamp);
                ds.FillEllipse(lx, horizonY - postH, _height * 0.009f, _height * 0.009f,
                    Color.FromArgb((byte)(la * 0.22f), 255, 210, 140));
            }

            // Avenue trees breaking the horizon, alternating with the lamps.
            int nTrees = 9;
            for (int k = 0; k < nTrees; k++)
            {
                int seed = 700 + k;
                float fx = ((k + 0.5f + Hash01(seed, 8, 8, 78) * 0.4f) / nTrees + nearOx) % 1f;
                if (fx < 0f) fx += 1f;
                float tx = fx * _width;
                float th = horizonY * (0.02f + Hash01(seed, 9, 9, 79) * 0.03f);
                DrawTreeSilhouette(ds, tx, horizonY, th, seed);
            }
        }

        private void DrawBridge(CanvasDrawingSession ds)
        {
            float horizonY = _height * HorizonFraction;
            float bw = Math.Min(_width * 0.42f, _height * 0.9f);

            // Slides at foreground speed (slower than the buildings) so the
            // whole scene pans together and the waterline stays a deeper plane
            // than the skyline. Wrapped position + one wrap copy.
            float ox = _fgOffset;
            float fx = (0.5f + ox) % 1f;
            if (fx < 0f) fx += 1f;
            float bx = fx * _width - bw * 0.5f;

            DrawBridgeSpan(ds, bx, bw, horizonY);

            if (bx + bw > _width)
                DrawBridgeSpan(ds, bx - _width, bw, horizonY);
            else if (bx < 0f)
                DrawBridgeSpan(ds, bx + _width, bw, horizonY);
        }

        private void DrawBridgeSpan(CanvasDrawingSession ds, float bx, float bw, float horizonY)
        {
            float deckY = horizonY;
            float deckH = Math.Max(2f, _height * 0.006f);
            Color steel = Color.FromArgb(255, 13, 19, 32);
            Color steelEdge = Color.FromArgb(255, 70, 88, 124);
            Color lamp = Color.FromArgb(235, 255, 214, 140);

            float towerX0 = bx + bw * 0.28f;
            float towerX1 = bx + bw * 0.72f;
            float towerH = Math.Min(horizonY * 0.16f, _height * 0.085f);
            float towerW = Math.Max(2.5f, _width * 0.004f);

            // Trees planted along the deck break the horizon across the whole bridge.
            DrawDeckTrees(ds, bx, bw, deckY, towerX0, towerX1);

            // Deck
            ds.FillRectangle(bx, deckY - deckH, bw, deckH, steel);
            ds.DrawLine(bx, deckY - deckH, bx + bw, deckY - deckH, steelEdge, 1f);

            // Guardrail along the deck edge.
            float railTop = deckY - deckH - _height * 0.010f;
            ds.DrawLine(bx, railTop, bx + bw, railTop, steelEdge, 1.2f);
            float picketSpacing = Math.Max(_height * 0.014f, 4f);
            for (float px = bx + picketSpacing * 0.5f; px < bx + bw; px += picketSpacing)
            {
                ds.DrawLine(px, deckY - deckH, px, railTop, steel, 1f);
            }

            // Superstructure archetype. _fgBridgeType cycles once per full
            // foreground wrap, so every pass of the river brings a different
            // bridge without any mid-frame pop.
            switch ((BridgeType)_fgBridgeType)
            {
                case BridgeType.CableStayed:
                    DrawCableStayed(ds, bx, bw, deckY, deckH, horizonY, steel, steelEdge, towerX0, towerX1, towerH, towerW);
                    break;
                case BridgeType.Arch:
                    DrawArchBridge(ds, bx, bw, deckY, deckH, horizonY, steel, steelEdge);
                    break;
                case BridgeType.Suspension:
                    DrawSuspensionBridge(ds, bx, bw, deckY, deckH, horizonY, steel, steelEdge, towerW);
                    break;
                case BridgeType.Bowstring:
                    DrawBowstringBridge(ds, bx, bw, deckY, deckH, steel, steelEdge);
                    break;
            }

            // Lamp posts along the deck + warm reflections in the water.
            int nLamps = 6;
            for (int k = 0; k < nLamps; k++)
            {
                float lx = bx + bw * ((k + 1) / (nLamps + 1f));
                float ph = k * 1.1f;
                float gl = 0.7f + 0.3f * MathF.Sin(_time * 2.2f + ph);
                byte la = (byte)(150 + 105 * gl);
                ds.DrawLine(lx, deckY, lx, deckY - deckH - _height * 0.012f, steel, 1.2f);
                ds.FillEllipse(lx, deckY - deckH - _height * 0.013f, _height * 0.004f, _height * 0.004f, lamp);
                ds.FillEllipse(lx, deckY - deckH - _height * 0.013f, _height * 0.010f, _height * 0.010f,
                    Color.FromArgb((byte)(la * 0.20f), 255, 210, 140));
                float refLen = _height * (0.035f + 0.02f * gl);
                ds.FillRectangle(lx - 0.6f, deckY, 1.2f, refLen,
                    Color.FromArgb((byte)(la * 0.35f), 255, 200, 130));
            }

            // Support piers under the deck.
            for (int k = 0; k < 3; k++)
            {
                float px = bx + bw * (0.16f + k * 0.34f);
                ds.FillRectangle(px - 1f, deckY, 2f, _height * 0.02f, steel);
            }
        }

        /// <summary>Two towers with stays fanning outward to the deck. Stay
        /// direction uses the SPAN-local center so the fan is correct even when
        /// the bridge is scrolled/wrapped (the old screen-center test broke it).</summary>
        private void DrawCableStayed(CanvasDrawingSession ds, float bx, float bw, float deckY, float deckH, float horizonY,
            Color steel, Color steelEdge, float towerX0, float towerX1, float towerH, float towerW)
        {
            float spanCenter = bx + bw * 0.5f;
            foreach (float tx in new[] { towerX0, towerX1 })
            {
                ds.FillRectangle(tx - towerW * 0.5f, deckY - towerH, towerW, towerH, steel);
                ds.DrawLine(tx - towerW * 0.5f, deckY - towerH, tx + towerW * 0.5f, deckY - towerH, steelEdge, 1f);
                int nCables = 4;
                for (int k = 0; k < nCables; k++)
                {
                    float t = (k + 1f) / (nCables + 1f);
                    float deckX = tx > spanCenter
                        ? tx + (bx + bw - tx) * t
                        : bx + (tx - bx) * t;
                    ds.DrawLine(tx, deckY - towerH, deckX, deckY - deckH, steelEdge, 0.8f);
                }
                DrawBeacon(ds, tx, deckY - towerH - 1.5f, (int)(tx * 7f), LayerCount - 1, 1.8f);
            }
        }

        /// <summary>Rising parabolic steel arch over the deck with spandrel posts.</summary>
        private void DrawArchBridge(CanvasDrawingSession ds, float bx, float bw, float deckY, float deckH, float horizonY,
            Color steel, Color steelEdge)
        {
            float deckY0 = deckY - deckH;
            float archRise = Math.Min(horizonY * 0.14f, _height * 0.075f);
            float ctrlY = deckY0 - 2f * archRise;

            // Faint back arch for depth, brighter front arch on top.
            DrawParabolicCable(ds, bx + 1.2f, deckY0, bx + bw + 1.2f, deckY0, ctrlY + archRise * 0.12f,
                Color.FromArgb(255, 9, 13, 22), 1.4f, 24);
            DrawParabolicCable(ds, bx, deckY0, bx + bw, deckY0, ctrlY, steelEdge, 1.6f, 24);

            for (int k = 1; k < 7; k++)
            {
                float t = k / 7f;
                float x = bx + t * bw;
                float arcY = BezierY(deckY0, deckY0, ctrlY, t);
                ds.DrawLine(x, deckY0, x, arcY, steel, 1f);
            }

            DrawBeacon(ds, bx + bw * 0.5f, deckY0 - archRise - 1.5f, 777, LayerCount - 1, 1.8f);
        }

        /// <summary>Two tall towers, sagging main cable, back spans and vertical hangers.</summary>
        private void DrawSuspensionBridge(CanvasDrawingSession ds, float bx, float bw, float deckY, float deckH, float horizonY,
            Color steel, Color steelEdge, float towerW)
        {
            float deckY0 = deckY - deckH;
            float suspH = Math.Min(horizonY * 0.17f, _height * 0.09f);
            float ty = deckY - suspH;
            float sx0 = bx + bw * 0.22f;
            float sx1 = bx + bw * 0.78f;

            foreach (float tx in new[] { sx0, sx1 })
            {
                ds.FillRectangle(tx - towerW * 0.5f, deckY - suspH, towerW, suspH, steel);
                ds.DrawLine(tx - towerW * 0.5f, deckY - suspH, tx + towerW * 0.5f, deckY - suspH, steelEdge, 1f);
                DrawBeacon(ds, tx, deckY - suspH - 1.5f, (int)(tx * 13f), LayerCount - 1, 1.8f);
            }

            // Back spans droop gently from the deck edge up to each tower top.
            float backCtrl = (deckY0 + ty) * 0.5f + (deckY0 - ty) * 0.10f;
            DrawParabolicCable(ds, bx, deckY0, sx0, ty, backCtrl, steelEdge, 0.9f, 10);
            DrawParabolicCable(ds, bx + bw, deckY0, sx1, ty, backCtrl, steelEdge, 0.9f, 10);

            // Main cable sags ~45% toward the deck between the tower tops.
            float midCtrl = ty + (deckY0 - ty) * 0.45f;
            DrawParabolicCable(ds, sx0, ty, sx1, ty, midCtrl, steelEdge, 0.9f, 16);

            int nH = 7;
            for (int k = 1; k < nH; k++)
            {
                float t = k / (float)nH;
                float x = sx0 + (sx1 - sx0) * t;
                float cableY = BezierY(ty, ty, midCtrl, t);
                ds.DrawLine(x, cableY, x, deckY0, steel, 0.7f);
            }
        }

        /// <summary>Slender tied arch (bowstring): thin parabolic rib + hangers to the deck.</summary>
        private void DrawBowstringBridge(CanvasDrawingSession ds, float bx, float bw, float deckY, float deckH,
            Color steel, Color steelEdge)
        {
            float deckY0 = deckY - deckH;
            float rise = Math.Min(deckY * 0.10f, _height * 0.055f);
            float ctrlY = deckY0 - 2f * rise;

            DrawParabolicCable(ds, bx, deckY0, bx + bw, deckY0, ctrlY, steelEdge, 1.3f, 20);

            int nH = 5;
            for (int k = 1; k < nH; k++)
            {
                float t = k / (float)nH;
                float x = bx + t * bw;
                float arcY = BezierY(deckY0, deckY0, ctrlY, t);
                ds.DrawLine(x, deckY0, x, arcY, steel, 0.7f);
            }

            DrawBeacon(ds, bx + bw * 0.5f, BezierY(deckY0, deckY0, ctrlY, 0.5f) - 1.5f, 991, LayerCount - 1, 1.8f);
        }

        /// <summary>Quadratic-bezier cable/arch polyline sampled into line segments.</summary>
        private void DrawParabolicCable(CanvasDrawingSession ds, float x0, float y0, float x1, float y1, float ctrlY,
            Color color, float width, int segs)
        {
            var prev = new Vector2(x0, y0);
            for (int s = 1; s <= segs; s++)
            {
                float t = s / (float)segs;
                float it = 1f - t;
                float x = it * it * x0 + 2f * it * t * ((x0 + x1) * 0.5f) + t * t * x1;
                float y = BezierY(y0, y1, ctrlY, t);
                ds.DrawLine(prev.X, prev.Y, x, y, color, width);
                prev = new Vector2(x, y);
            }
        }

        private static float BezierY(float y0, float y1, float ctrlY, float t)
        {
            float it = 1f - t;
            return it * it * y0 + 2f * it * t * ctrlY + t * t * y1;
        }

        private void DrawDeckTrees(CanvasDrawingSession ds, float bx, float bw, float deckY, float towerX0, float towerX1)
        {
            int n = 14;
            for (int k = 0; k < n; k++)
            {
                int seed = 900 + k;
                float x = bx + bw * (0.04f + Hash01(seed, 5, 5, 77) * 0.92f);
                // Sparse near the towers so the pylons stay readable.
                if (MathF.Abs(x - towerX0) < bw * 0.05f || MathF.Abs(x - towerX1) < bw * 0.05f) continue;
                float hgt = deckY * (0.018f + Hash01(seed, 6, 6, 88) * 0.035f);
                DrawTreeSilhouette(ds, x, deckY, hgt, seed);
            }
        }

        private void DrawTreeSilhouette(CanvasDrawingSession ds, float x, float baseY, float h, int seed)
        {
            Color trunkCol = Color.FromArgb(255, 7, 10, 17);
            Color leafCol = Color.FromArgb(255, 9, 13, 21);
            Color leafTop = Color.FromArgb(255, 13, 19, 29);
            float sway = MathF.Sin(_time * 0.5f + seed) * h * 0.03f;

            // Trunk + a couple of bare branches.
            float trunkW = Math.Max(1f, h * 0.055f);
            ds.FillRectangle(x - trunkW * 0.5f, baseY - h * 0.55f, trunkW, h * 0.55f, trunkCol);
            float b1 = Hash01(seed, 10, 1, 51);
            float b2 = Hash01(seed, 11, 1, 52);
            ds.DrawLine(x, baseY - h * 0.45f, x - h * 0.25f * b1, baseY - h * 0.64f, trunkCol, 1f);
            ds.DrawLine(x, baseY - h * 0.38f, x + h * 0.22f * b2, baseY - h * 0.60f, trunkCol, 1f);

            // Dense canopy: four overlapping blobs with a lit top highlight.
            float cw = h * (0.62f + Hash01(seed, 1, 1, 11) * 0.3f);
            float ch = h * (0.34f + Hash01(seed, 2, 2, 22) * 0.18f);
            float cx = x + sway;
            float cy = baseY - h * 0.75f;
            ds.FillEllipse(cx, cy, cw, ch, leafCol);
            ds.FillEllipse(cx - cw * 0.5f, cy - ch * 0.15f, cw * 0.62f, ch * 0.75f, leafCol);
            ds.FillEllipse(cx + cw * 0.5f, cy - ch * 0.15f, cw * 0.62f, ch * 0.75f, leafCol);
            ds.FillEllipse(cx + cw * 0.12f, cy - ch * 0.6f, cw * 0.5f, ch * 0.6f, leafCol);
            ds.FillEllipse(cx, cy - ch * 0.55f, cw * 0.35f, ch * 0.35f, leafTop);
        }

        private void DrawWater(CanvasDrawingSession ds)
        {
            float horizonY = _height * HorizonFraction;
            float waterTop = horizonY;
            float waterH = _height - waterTop;
            if (waterH <= 0f) return;

            ds.FillRectangle(0, waterTop, _width, waterH, Color.FromArgb(255, 5, 8, 15));

            // Murky water: reflection is pulled from a blurred, dimmed copy of
            // the scene so the river reads foggy instead of mirror-crisp.
            var reflectSource = new GaussianBlurEffect
            {
                Source = _sceneTarget,
                BlurAmount = 4f
            };

            float rippleAmp = _height * 0.0035f * (1f + _smoothBass * 1.6f);
            float reflectAlpha = 0.55f;

            for (int i = 0; i < ReflectStrips; i++)
            {
                float y0 = waterTop + (i / (float)ReflectStrips) * waterH;
                float y1 = waterTop + ((i + 1) / (float)ReflectStrips) * waterH;
                float t0 = (y0 - waterTop) / waterH;
                float t1 = (y1 - waterTop) / waterH;
                float srcY0 = horizonY * (1f - t0);
                float srcY1 = horizonY * (1f - t1);

                float ripple = MathF.Sin(_time * 1.6f + i * 0.9f + MathF.Sin(_time * 0.7f + i * 0.4f)) * rippleAmp;
                float ripple2 = MathF.Sin(_time * 2.2f + i * 1.3f) * rippleAmp * 0.5f;
                float drift = ripple + ripple2;

                ds.DrawImage(reflectSource,
                    new Rect(0, y0 + drift, _width, y1 - y0),
                    new Rect(0, srcY1, _width, srcY0 - srcY1),
                    reflectAlpha, CanvasImageInterpolation.Linear);
            }

            DrawMoonReflection(ds, waterTop, waterH);
            DrawShimmerStreaks(ds, waterTop, waterH);

            var fade = new CanvasLinearGradientBrush(ds,
                Color.FromArgb(200, 2, 4, 10),
                Color.FromArgb(45, 4, 8, 16))
            {
                StartPoint = new Vector2(0, waterTop),
                EndPoint = new Vector2(0, _height)
            };
            ds.FillRectangle(0, waterTop, _width, waterH, fade);
            fade.Dispose();

            ds.FillRectangle(0, waterTop - 1f, _width, 2f, Color.FromArgb(80, 40, 58, 108));
        }

        private void DrawMoonReflection(CanvasDrawingSession ds, float waterTop, float waterH)
        {
            float cx = _width * 0.76f;
            float pulse = 1f + _smoothBass * 0.5f + _smoothBeat * 0.4f;

            var glowStops = new CanvasGradientStop[]
            {
                new CanvasGradientStop { Position = 0f, Color = Color.FromArgb(42, 190, 205, 255) },
                new CanvasGradientStop { Position = 1f, Color = Color.FromArgb(0, 185, 200, 255) }
            };
            using (var glow = new CanvasRadialGradientBrush(_device, glowStops, CanvasEdgeBehavior.Clamp, CanvasAlphaMode.Premultiplied)
            {
                Center = new Vector2(cx, waterTop),
                RadiusX = _height * 0.10f * pulse,
                RadiusY = waterH * 0.5f
            })
            {
                ds.FillRectangle(cx - _height * 0.10f * pulse, waterTop, _height * 0.20f * pulse, waterH, glow);
            }

            for (int k = 0; k < 3; k++)
            {
                float yy = waterTop + waterH * (0.08f + k * 0.18f)
                         + MathF.Sin(_time * 1.1f + k * 1.7f) * _height * 0.008f;
                float rw = _height * (0.045f - k * 0.006f) * pulse;
                byte a = (byte)(20 + _smoothBass * 30 + (k == 0 ? _smoothBeat * 30 : 0));
                ds.FillEllipse(cx, yy, rw, 1.4f, Color.FromArgb(a, 205, 218, 255));
            }
        }

        private void DrawShimmerStreaks(CanvasDrawingSession ds, float waterTop, float waterH)
        {
            for (int k = 0; k < 4; k++)
            {
                float p = (_time * 0.05f + k * 0.25f) % 1f;
                float y = waterTop + waterH * (0.12f + 0.76f * p);
                float x = ((_time * (10f + k * 4f) + k * 137f) % (_width + 600f)) - 300f;
                byte a = (byte)(7 + _smoothBass * 16);
                ds.FillEllipse(x, y, 70f + k * 26f, 1.4f, Color.FromArgb(a, 175, 202, 255));
            }
        }

        private void DrawShootingStars(CanvasDrawingSession ds)
        {
            for (int i = 0; i < ShootingStarCount; i++)
            {
                if (_shootLife[i] <= 0f) continue;
                float life = Math.Clamp(_shootLife[i] / 1.2f, 0f, 1f);
                float x = _shootX[i] * _width;
                float y = _shootY[i] * _height;
                float tailLen = _height * 0.045f;
                float dx = -_shootVX[i] * 0.16f;
                float dy = -_shootVY[i] * 0.16f;

                byte a1 = (byte)(140 * life);
                byte a2 = (byte)(65 * life);
                byte a3 = (byte)(26 * life);
                ds.DrawLine(x - dx * 2.6f, y - dy * 2.6f, x, y, Color.FromArgb(a3, 120, 150, 255), tailLen * 0.5f);
                ds.DrawLine(x - dx * 1.6f, y - dy * 1.6f, x, y, Color.FromArgb(a2, 190, 210, 255), tailLen * 0.32f);
                ds.DrawLine(x - dx, y - dy, x, y, Color.FromArgb(a1, 255, 255, 255), tailLen * 0.16f);
            }
        }

        private static float Hash01(int a, int b, int c, int seed)
        {
            unchecked
            {
                int h = a * 73856093 ^ b * 19349663 ^ c * 83492791 ^ seed * 1812433253;
                h &= 0x7fffffff;
                return h / (float)0x7fffffff;
            }
        }

        public void Dispose()
        {
            // Take the render lock so a mid-frame rebuild (BakeWindowMasks)
            // never touches resources this thread is disposing underneath it.
            lock (_resLock)
            {
                _skyBrush?.Dispose();
                _skyBrush = null;
                _sceneTarget?.Dispose();
                _sceneTarget = null;
                _cloudSprite?.Dispose();
                _cloudSprite = null;
                _glowSprite?.Dispose();
                _glowSprite = null;
                _shadeSide?.Dispose();
                _shadeSide = null;
                _shadeDark?.Dispose();
                _shadeDark = null;
                _shadeAo?.Dispose();
                _shadeAo = null;
                DisposeWindowMasks();
                _device = null;
            }
        }

        public void ConfigurePipeline(PostProcessPipeline pipeline)
        {
            pipeline.FeedbackOpacity = 0.05f;
            pipeline.FeedbackZoom = 1.0002f;
            pipeline.BloomAmount = 0.55f;
            pipeline.BloomBlur = 3.2f;
            pipeline.BloomThreshold = 0.14f;
            pipeline.VignetteEnabled = true;
            pipeline.VignetteAmount = 0.42f;
            pipeline.NoiseGrainEnabled = true;
            pipeline.NoiseGrainAmount = 0.05f;
            pipeline.NightTintEnabled = true;
            pipeline.NightTintStrength = 0.55f;
            pipeline.WaterRippleEnabled = true;
            pipeline.WaterTopFraction = HorizonFraction;
            pipeline.WaterRippleAmount = 4f;
            pipeline.WaterRippleSpeed = 7f;
        }
    }
}