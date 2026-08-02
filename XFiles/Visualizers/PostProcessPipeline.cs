using System;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;
using Windows.Foundation;
using Windows.UI;

namespace XFiles.Visualizers
{
    public sealed class PostProcessPipeline : IDisposable
    {
        private CanvasDevice _device;
        private CanvasRenderTarget _feedbackBuffer;
        private CanvasRenderTarget _sceneBuffer;
        private CanvasRenderTarget _bloomBlur;
        private CanvasRenderTarget _bloomBlend;
        private CanvasRenderTarget _noiseTexture;
        private float _width, _height;
        private bool _disposed;
        private float _time;

        private float _bassLevel;
        private float _beatLevel;

        // Feedback trails
        public float FeedbackOpacity { get; set; } = 0.55f;
        public float FeedbackZoom { get; set; } = 1.0008f;
        public float FeedbackDecay { get; set; } = 0f;
        public float FeedbackOffsetY { get; set; } = 0f;

        // Bloom
        public bool BloomEnabled { get; set; } = true;
        public float BloomAmount { get; set; } = 0.12f;
        public float BloomBlur { get; set; } = 4f;
        public float BloomThreshold { get; set; } = 0.05f;

        // Vignette
        public bool VignetteEnabled { get; set; } = true;
        public float VignetteAmount { get; set; } = 0.2f;

        // Motion
        public float SlideX { get; set; } = 0f;
        public float SlideY { get; set; } = 0f;
        public float Rotation { get; set; } = 0f;
        private float _cumulativeSlideX;
        private float _cumulativeSlideY;

        // Chromatic aberration (pixels)
        public float ChromaticAberration { get; set; } = 0f;

        // Scanlines
        public bool ScanlinesEnabled { get; set; } = false;
        public float ScanlineIntensity { get; set; } = 0.15f;
        public float ScanlineCount { get; set; } = 300f;

        // Film grain
        public bool NoiseGrainEnabled { get; set; } = false;
        public float NoiseGrainAmount { get; set; } = 0.06f;

        // Night color grade (deep navy shadows + subtle desaturation)
        public bool NightTintEnabled { get; set; } = false;
        public float NightTintStrength { get; set; } = 0.5f;

        // Water ripple: turbulence-driven displacement masked to the region
        // below WaterTopFraction. Disabled by default (WaterTopFraction = 0).
        public bool WaterRippleEnabled { get; set; } = false;
        public float WaterTopFraction { get; set; } = 0f;
        public float WaterRippleAmount { get; set; } = 3f;
        public float WaterRippleSpeed { get; set; } = 6f;
        private float _rippleScrollX;
        private float _rippleScrollY;

        public void Initialize(CanvasDevice device) { _device = device; }

        public void Resize(float width, float height)
        {
            // NEVER dispose here: Resize runs on the UI thread while Draw runs
            // on the render thread. Disposing _sceneBuffer/_bloomBlur etc.
            // mid-draw made effect sources point at disposed targets
            // ("Effect source #0 is null", D2DERR_BITMAP_BOUND_AS_TARGET).
            // Recreation is deferred to the render thread: EnsureBuffers /
            // EnsureBloomBuffers / EnsureNoiseTexture already rebuild a buffer
            // whenever its size no longer matches _width/_height.
            _width = width;
            _height = height;
        }

        public void Draw(CanvasDrawingSession mainDs, Action<CanvasDrawingSession> drawContent, float bassLevel, float beatLevel)
        {
            if (_device == null || _width == 0 || _height == 0) return;

            _bassLevel = bassLevel;
            _beatLevel = beatLevel;
            _time += 0.016f;
            _cumulativeSlideX += SlideX * 0.5f;
            _cumulativeSlideY += SlideY * 0.5f;
            EnsureBuffers();

            using (var sceneDs = _sceneBuffer.CreateDrawingSession())
            {
                sceneDs.Clear(Color.FromArgb(255, 2, 2, 5));

                // 1. Draw new content first (visualizers call ds.Clear internally)
                {
                    var prevTransform = sceneDs.Transform;
                    var center = new Vector2(_width * 0.5f, _height * 0.5f);
                    Matrix3x2 contentMat = Matrix3x2.CreateRotation(Rotation * _time * 0.3f, center)
                                         * Matrix3x2.CreateTranslation(SlideX * 0.5f, SlideY * 0.5f);
                    sceneDs.Transform = contentMat;
                    drawContent(sceneDs);
                    sceneDs.Transform = prevTransform;
                }

                // 2. Feedback trails with ADDITIVE blending on top of content
                if (_feedbackBuffer != null)
                {
                float opacity = Math.Min(0.90f, FeedbackOpacity + _bassLevel * 0.06f - FeedbackDecay * 0.1f);
                opacity = Math.Max(0f, opacity);
                    float zoom = FeedbackZoom + _beatLevel * 0.008f;
                    var center = new Vector2(_width * 0.5f, _height * 0.5f);

                    Matrix3x2 slideMat = Matrix3x2.CreateTranslation(_cumulativeSlideX, _cumulativeSlideY + FeedbackOffsetY);
                    Matrix3x2 rotMat = Matrix3x2.CreateRotation(Rotation * _time, center);
                    Matrix3x2 zoomMat = Matrix3x2.CreateScale(zoom, center);
                    Matrix3x2 feedbackMatrix = slideMat * rotMat * zoomMat;

                    var prevTransform = sceneDs.Transform;
                    sceneDs.Transform = feedbackMatrix;
                    sceneDs.DrawImage(_feedbackBuffer, Vector2.Zero, _feedbackBuffer.Bounds,
                        opacity, CanvasImageInterpolation.Linear, CanvasComposite.Add);
                    sceneDs.Transform = prevTransform;
                }
            }

            // 3. Save clean composite to feedback BEFORE bloom/overlays
            using (var copyDs = _feedbackBuffer.CreateDrawingSession())
            {
                copyDs.DrawImage(_sceneBuffer);
            }

            // 4. Post-processing on display output only
            if (BloomEnabled && BloomAmount > 0)
                ApplyBloom();

            if (ChromaticAberration > 0.5f)
                ApplyChromaticAberration();

            if (NightTintEnabled && NightTintStrength > 0)
                ApplyNightTint();

            if (WaterRippleEnabled && WaterTopFraction > 0.01f && WaterTopFraction < 0.99f)
                ApplyWaterRipple();

            mainDs.DrawImage(_sceneBuffer);

            if (ScanlinesEnabled && ScanlineIntensity > 0)
                ApplyScanlines(mainDs);

            if (NoiseGrainEnabled && NoiseGrainAmount > 0)
                ApplyNoiseGrain(mainDs);

            if (VignetteEnabled && VignetteAmount > 0)
                DrawVignette(mainDs);
        }

        private void ApplyBloom()
        {
            float blurAmount = BloomBlur + _beatLevel * 8f;
            float intensity = BloomAmount * (0.7f + _beatLevel * 0.3f);

            EnsureBloomBuffers();

            // Step 1: Blur scene -> _bloomBlur
            using (var blurDs = _bloomBlur.CreateDrawingSession())
            {
                ICanvasImage source = _sceneBuffer;

                // Apply bloom threshold via brightness black point
                if (BloomThreshold > 0.01f)
                {
                    source = new BrightnessEffect
                    {
                        Source = _sceneBuffer,
                        BlackPoint = new Vector2(BloomThreshold, 0),
                        WhitePoint = new Vector2(1, 1)
                    };
                }

                var blur = new GaussianBlurEffect
                {
                    Source = source,
                    BlurAmount = blurAmount,
                    BorderMode = EffectBorderMode.Soft
                };
                blurDs.DrawImage(blur);
            }

            // Step 2: Screen blend -> _bloomBlend
            using (var blendDs = _bloomBlend.CreateDrawingSession())
            {
                var blend = new BlendEffect
                {
                    Background = _sceneBuffer,
                    Foreground = _bloomBlur,
                    Mode = BlendEffectMode.Screen
                };

                using (var opacity = new OpacityEffect
                {
                    Source = blend,
                    Opacity = intensity
                })
                {
                    blendDs.DrawImage(opacity);
                }
            }

            // Step 3: Copy back to scene
            using (var copyDs = _sceneBuffer.CreateDrawingSession())
            {
                copyDs.DrawImage(_bloomBlend);
            }
        }

        private void ApplyChromaticAberration()
        {
            float offset = ChromaticAberration;

            // Draw the shifted channels into a temp buffer, then copy back.
            // Drawing an effect that reads _sceneBuffer while _sceneBuffer is
            // bound as the target throws D2DERR_BITMAP_BOUND_AS_TARGET.
            EnsureBloomBuffers();

            // Pass 1: red + green shifted left
            using (var redDs = _bloomBlend.CreateDrawingSession())
            {
                redDs.Clear(Colors.Transparent);
                var redEffect = new ColorMatrixEffect
                {
                    Source = _sceneBuffer,
                    ColorMatrix = new Matrix5x4
                    {
                        M11 = 1, M12 = 0, M13 = 0, M14 = 0,
                        M21 = 0, M22 = 1, M23 = 0, M24 = 0,
                        M31 = 0, M32 = 0, M33 = 0, M34 = 0,
                        M41 = 0, M42 = 0, M43 = 0, M44 = 1,
                        M51 = 0, M52 = 0, M53 = 0, M54 = 0
                    }
                };
                var prevTransform = redDs.Transform;
                redDs.Transform = Matrix3x2.CreateTranslation(-offset, 0);
                redDs.DrawImage(redEffect);
                redDs.Transform = prevTransform;
            }

            // Pass 2: blue shifted right, added on top
            using (var blueDs = _bloomBlend.CreateDrawingSession())
            {
                var blueEffect = new ColorMatrixEffect
                {
                    Source = _sceneBuffer,
                    ColorMatrix = new Matrix5x4
                    {
                        M11 = 0, M12 = 0, M13 = 0, M14 = 0,
                        M21 = 0, M22 = 0, M23 = 0, M24 = 0,
                        M31 = 0, M32 = 0, M33 = 1, M34 = 0,
                        M41 = 0, M42 = 0, M43 = 0, M44 = 1,
                        M51 = 0, M52 = 0, M53 = 0, M54 = 0
                    }
                };
                var prevTransform = blueDs.Transform;
                blueDs.Transform = Matrix3x2.CreateTranslation(offset, 0);
                blueDs.DrawImage(blueEffect,
                    new Rect(0, 0, _width, _height),
                    new Rect(0, 0, _width, _height),
                    1f, CanvasImageInterpolation.Linear, CanvasComposite.Add);
                blueDs.Transform = prevTransform;
            }

            using (var copyDs = _sceneBuffer.CreateDrawingSession())
            {
                copyDs.DrawImage(_bloomBlend);
            }
        }

        private void ApplyNightTint()
        {
            float s = NightTintStrength;
            var m = new Matrix5x4
            {
                M11 = 1f - (1f - 0.86f) * s,
                M22 = 1f - (1f - 0.90f) * s,
                M33 = 1f - (1f - 0.95f) * s,
                M44 = 1f,
                M51 = 0.03f * s,
                M52 = 0.05f * s,
                M53 = 0.10f * s
            };

            EnsureBloomBuffers();

            using (var effect = new ColorMatrixEffect { Source = _sceneBuffer, ColorMatrix = m })
            using (var tintDs = _bloomBlend.CreateDrawingSession())
            {
                tintDs.DrawImage(effect);
            }

            using (var copyDs = _sceneBuffer.CreateDrawingSession())
            {
                copyDs.DrawImage(_bloomBlend);
            }
        }

        private void ApplyWaterRipple()
        {
            float amp = WaterRippleAmount * (0.5f + _bassLevel * 1.2f) * (1f + _beatLevel * 0.3f);
            if (amp < 0.05f) return;

            float waterY = _height * WaterTopFraction;
            var waterRect = new Rect(0, waterY, _width, _height - waterY);

            // Pipeline advances _time by ~0.016f per frame, so scroll by dt.
            _rippleScrollX += WaterRippleSpeed * 0.016f;
            _rippleScrollY += WaterRippleSpeed * 0.4f * 0.016f;

            EnsureBloomBuffers();

            // Pass 1: displaced scene into _bloomBlend. The DisplacementMap
            // reads _sceneBuffer as its source, which is legal because
            // _sceneBuffer is only bound as a target in Pass 3.
            using (var dispDs = _bloomBlend.CreateDrawingSession())
            {
                dispDs.Clear(Colors.Transparent);

                var turb = new TurbulenceEffect
                {
                    Frequency = new Vector2(0.020f, 0.045f),
                    Octaves = 3,
                    Size = new Vector2(_width, _height),
                    Seed = 1337
                };
                var scroll = new Transform2DEffect
                {
                    Source = turb,
                    TransformMatrix = Matrix3x2.CreateTranslation(
                        -(_rippleScrollX % _width),
                        -(_rippleScrollY % _height))
                };
                var border = new BorderEffect
                {
                    Source = scroll,
                    ExtendX = CanvasEdgeBehavior.Wrap,
                    ExtendY = CanvasEdgeBehavior.Wrap
                };
                var displace = new DisplacementMapEffect
                {
                    Source = _sceneBuffer,
                    Displacement = border,
                    Amount = amp,
                    XChannelSelect = EffectChannelSelect.Red,
                    YChannelSelect = EffectChannelSelect.Green
                };

                dispDs.DrawImage(displace);
            }

            // Pass 2: snapshot the original composite, since the final target
            // below is _sceneBuffer itself (can't read it while it's bound).
            using (var saveDs = _bloomBlur.CreateDrawingSession())
            {
                saveDs.DrawImage(_sceneBuffer);
            }

            // Pass 3: original everywhere, displaced version only below the
            // horizon so the skyline stays sharp while the water ripples.
            using (var compDs = _sceneBuffer.CreateDrawingSession())
            {
                compDs.DrawImage(_bloomBlur);
                using (var layer = compDs.CreateLayer(1.0f, waterRect))
                {
                    compDs.DrawImage(_bloomBlend);
                }
            }
        }

        private void ApplyScanlines(CanvasDrawingSession ds)        {
            float lineSpacing = _height / ScanlineCount;
            float alpha = (byte)(255 * ScanlineIntensity);

            using (var brush = new CanvasSolidColorBrush(_device, Color.FromArgb((byte)alpha, 0, 0, 0)))
            {
                for (float y = 0; y < _height; y += lineSpacing)
                {
                    ds.FillRectangle(0, y, _width, 1, brush);
                }
            }
        }

        private void ApplyNoiseGrain(CanvasDrawingSession ds)
        {
            EnsureNoiseTexture();

            using (var noiseDs = _noiseTexture.CreateDrawingSession())
            {
                var rng = new Random((int)(_time * 1000) & 0xFFFFFF);
                int pixelSize = 4;
                int cols = (int)(_width / pixelSize) + 1;
                int rows = (int)(_height / pixelSize) + 1;

                for (int i = 0; i < cols * rows / 8; i++)
                {
                    int x = rng.Next(0, cols) * pixelSize;
                    int y = rng.Next(0, rows) * pixelSize;
                    byte brightness = (byte)(rng.Next(80, 200));
                    int maxAlpha = (int)(255 * NoiseGrainAmount);
                    if (maxAlpha < 21) maxAlpha = 21;
                    byte alpha = (byte)(rng.Next(20, maxAlpha));
                    noiseDs.FillRectangle(x, y, pixelSize, pixelSize,
                        Color.FromArgb(alpha, brightness, brightness, brightness));
                }
            }

            // Screen-blend the grain onto a temp buffer, then copy back. Drawing
            // a blend whose Background reads _sceneBuffer while _sceneBuffer is
            // bound as the target throws D2DERR_BITMAP_BOUND_AS_TARGET.
            EnsureBloomBuffers();

            using (var blend = new BlendEffect
            {
                Background = _sceneBuffer,
                Foreground = _noiseTexture,
                Mode = BlendEffectMode.Screen
            })
            using (var blendDs = _bloomBlend.CreateDrawingSession())
            {
                blendDs.DrawImage(blend);
            }

            using (var copyDs = _sceneBuffer.CreateDrawingSession())
            {
                copyDs.DrawImage(_bloomBlend);
            }
        }
        private void EnsureNoiseTexture()
        {
            if (_noiseTexture == null || _noiseTexture.Size.Width != _width || _noiseTexture.Size.Height != _height)
            {
                _noiseTexture?.Dispose();
                _noiseTexture = new CanvasRenderTarget(_device, _width, _height, 96);
            }
            using (var clearDs = _noiseTexture.CreateDrawingSession())
            {
                clearDs.Clear(Colors.Transparent);
            }
        }

        private void DrawVignette(CanvasDrawingSession ds)
        {
            float amount = VignetteAmount + (1f - _bassLevel) * 0.1f;
            float cx = _width * 0.5f;
            float cy = _height * 0.5f;
            float radius = (float)Math.Sqrt(cx * cx + cy * cy);

            var stops = new CanvasGradientStop[]
            {
                new CanvasGradientStop { Position = 0f, Color = Color.FromArgb(0, 0, 0, 0) },
                new CanvasGradientStop { Position = 0.5f, Color = Color.FromArgb(0, 0, 0, 0) },
                new CanvasGradientStop { Position = 1f, Color = Color.FromArgb((byte)(255 * amount), 0, 0, 0) }
            };

            using (var brush = new CanvasRadialGradientBrush(
                _device,
                stops,
                CanvasEdgeBehavior.Clamp,
                CanvasAlphaMode.Premultiplied))
            {
                brush.Center = new Vector2(cx, cy);
                brush.RadiusX = radius * 1.2f;
                brush.RadiusY = radius * 1.2f;
                ds.FillRectangle(0, 0, _width, _height, brush);
            }
        }

        private void EnsureBuffers()
        {
            if (_sceneBuffer == null || _sceneBuffer.Size.Width != _width || _sceneBuffer.Size.Height != _height)
            {
                _sceneBuffer?.Dispose();
                _sceneBuffer = new CanvasRenderTarget(_device, _width, _height, 96);
            }
            if (_feedbackBuffer == null || _feedbackBuffer.Size.Width != _width || _feedbackBuffer.Size.Height != _height)
            {
                _feedbackBuffer?.Dispose();
                _feedbackBuffer = new CanvasRenderTarget(_device, _width, _height, 96);
            }
        }

        private void EnsureBloomBuffers()
        {
            if (_bloomBlur == null || _bloomBlur.Size.Width != _width || _bloomBlur.Size.Height != _height)
            {
                _bloomBlur?.Dispose();
                _bloomBlur = new CanvasRenderTarget(_device, _width, _height, 96);
            }
            if (_bloomBlend == null || _bloomBlend.Size.Width != _width || _bloomBlend.Size.Height != _height)
            {
                _bloomBlend?.Dispose();
                _bloomBlend = new CanvasRenderTarget(_device, _width, _height, 96);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _feedbackBuffer?.Dispose(); _sceneBuffer?.Dispose();
            _bloomBlur?.Dispose(); _bloomBlend?.Dispose();
            _noiseTexture?.Dispose();
            _feedbackBuffer = null; _sceneBuffer = null;
            _bloomBlur = null; _bloomBlend = null; _noiseTexture = null;
            _device = null;
        }
    }
}
