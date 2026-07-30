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

        public void Initialize(CanvasDevice device) { _device = device; }

        public void Resize(float width, float height)
        {
            _width = width;
            _height = height;
            _feedbackBuffer?.Dispose(); _feedbackBuffer = null;
            _sceneBuffer?.Dispose(); _sceneBuffer = null;
            _bloomBlur?.Dispose(); _bloomBlur = null;
            _bloomBlend?.Dispose(); _bloomBlend = null;
            _noiseTexture?.Dispose(); _noiseTexture = null;
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

            // Red channel: shift left
            using (var redDs = _sceneBuffer.CreateDrawingSession())
            {
                var redMatrix = Matrix3x2.CreateTranslation(-offset, 0);
                var redEffect = new ColorMatrixEffect
                {
                    Source = _sceneBuffer,
                    ColorMatrix = new Matrix5x4
                    {
                        M11 = 1, M12 = 0, M13 = 0, M14 = 0,
                        M21 = 0, M22 = 0, M23 = 0, M24 = 0,
                        M31 = 0, M32 = 0, M33 = 0, M34 = 0,
                        M41 = 0, M42 = 0, M43 = 0, M44 = 1,
                        M51 = 0, M52 = 0, M53 = 0, M54 = 0
                    }
                };
                var prevTransform = redDs.Transform;
                redDs.Transform = redMatrix;
                redDs.DrawImage(redEffect);
                redDs.Transform = prevTransform;
            }

            // Blue channel: shift right
            using (var blueDs = _sceneBuffer.CreateDrawingSession())
            {
                var blueMatrix = Matrix3x2.CreateTranslation(offset, 0);
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
                blueDs.Transform = blueMatrix;
                blueDs.DrawImage(blueEffect);
                blueDs.Transform = prevTransform;
            }
        }

        private void ApplyScanlines(CanvasDrawingSession ds)
        {
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
                    byte alpha = (byte)(rng.Next(20, (int)(255 * NoiseGrainAmount)));
                    using (var brush = new CanvasSolidColorBrush(_device, Color.FromArgb(alpha, brightness, brightness, brightness)))
                    {
                        noiseDs.FillRectangle(x, y, pixelSize, pixelSize, brush);
                    }
                }
            }

            using (var blend = new BlendEffect
            {
                Background = _sceneBuffer,
                Foreground = _noiseTexture,
                Mode = BlendEffectMode.Screen
            })
            using (var blendDs = _sceneBuffer.CreateDrawingSession())
            {
                blendDs.DrawImage(blend);
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
