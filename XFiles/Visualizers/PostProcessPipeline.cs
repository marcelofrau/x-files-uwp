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
        private float _width, _height;
        private bool _disposed;

        private float _bassLevel;
        private float _beatLevel;

        public float FeedbackOpacity { get; set; } = 0.88f;
        public float FeedbackZoom { get; set; } = 1.002f;
        public bool BloomEnabled { get; set; } = true;
        public float BloomAmount { get; set; } = 0.6f;
        public float BloomBlur { get; set; } = 14f;
        public bool VignetteEnabled { get; set; } = true;
        public float VignetteAmount { get; set; } = 0.45f;

        public void Initialize(CanvasDevice device) { _device = device; }

        public void Resize(float width, float height)
        {
            _width = width;
            _height = height;
            _feedbackBuffer?.Dispose(); _feedbackBuffer = null;
            _sceneBuffer?.Dispose(); _sceneBuffer = null;
            _bloomBlur?.Dispose(); _bloomBlur = null;
            _bloomBlend?.Dispose(); _bloomBlend = null;
        }

        public void Draw(CanvasDrawingSession mainDs, Action<CanvasDrawingSession> drawContent, float bassLevel, float beatLevel)
        {
            if (_device == null || _width == 0 || _height == 0) return;

            _bassLevel = bassLevel;
            _beatLevel = beatLevel;
            EnsureBuffers();

            using (var sceneDs = _sceneBuffer.CreateDrawingSession())
            {
                sceneDs.Clear(Color.FromArgb(255, 2, 2, 5));

                if (_feedbackBuffer != null)
                {
                    float opacity = Math.Min(0.96f, FeedbackOpacity + _bassLevel * 0.08f);
                    float zoom = FeedbackZoom + _beatLevel * 0.008f;
                    var center = new Vector2(_width * 0.5f, _height * 0.5f);
                    var matrix = Matrix3x2.CreateScale(zoom, center);

                    using (var opacityEffect = new OpacityEffect
                    {
                        Source = _feedbackBuffer,
                        Opacity = opacity
                    })
                    {
                        var prevTransform = sceneDs.Transform;
                        sceneDs.Transform = matrix;
                        sceneDs.DrawImage(opacityEffect);
                        sceneDs.Transform = prevTransform;
                    }
                }

                drawContent(sceneDs);
            }

            if (BloomEnabled && BloomAmount > 0)
                ApplyBloom();

            mainDs.DrawImage(_sceneBuffer);

            if (VignetteEnabled && VignetteAmount > 0)
                DrawVignette(mainDs);

            using (var copyDs = _feedbackBuffer.CreateDrawingSession())
            {
                copyDs.DrawImage(_sceneBuffer);
            }
        }

        private void ApplyBloom()
        {
            float blurAmount = BloomBlur + _beatLevel * 8f;
            float intensity = BloomAmount * (0.7f + _beatLevel * 0.3f);

            EnsureBloomBuffers();

            // Step 1: Blur scene → _bloomBlur
            using (var blurDs = _bloomBlur.CreateDrawingSession())
            {
                var blur = new GaussianBlurEffect
                {
                    Source = _sceneBuffer,
                    BlurAmount = blurAmount,
                    BorderMode = EffectBorderMode.Soft
                };
                blurDs.DrawImage(blur);
            }

            // Step 2: Screen blend scene + blur → _bloomBlend (separate buffer to avoid read/write conflict)
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

            // Step 3: Copy blend result back to scene
            using (var copyDs = _sceneBuffer.CreateDrawingSession())
            {
                copyDs.DrawImage(_bloomBlend);
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

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _feedbackBuffer?.Dispose(); _sceneBuffer?.Dispose();
            _bloomBlur?.Dispose(); _bloomBlend?.Dispose();
            _feedbackBuffer = null; _sceneBuffer = null;
            _bloomBlur = null; _bloomBlend = null; _device = null;
        }
    }
}
