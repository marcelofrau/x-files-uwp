using System;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Windows.Foundation;
using Windows.UI;

namespace XFiles.Visualizers.Visualizers
{
    public sealed class WaveformVisualizer : IAudioVisualizer
    {
        public string Name => "Waveform";
        public string Id => "waveform";

        private CanvasDevice _device;
        private float _width, _height, _time;

        private const float LineThickness = 2.5f;
        private const float GlowThickness = 6f;
        private const float AmplitudeScale = 0.35f;

        private float[] _smoothWave;
        private int _smoothCount;
        private float _smoothBeat;
        private const float SmoothFactor = 0.4f;

        public void Initialize(CanvasDevice device) { _device = device; }

        public void Update(AudioData data, TimeSpan elapsed)
        {
            _time = data.Time;
            _smoothBeat += (data.Beat - _smoothBeat) * 0.3f;
            int count = Math.Min(data.WaveformCount, data.Waveform.Length);
            if (_smoothWave == null || _smoothWave.Length != data.Waveform.Length)
                _smoothWave = new float[data.Waveform.Length];
            for (int i = 0; i < count; i++)
                _smoothWave[i] += (data.Waveform[i] - _smoothWave[i]) * SmoothFactor;
            _smoothCount = count;
        }

        public void Draw(CanvasDrawingSession ds)
        {
            if (_device == null || _width == 0 || _height == 0) return;
            ds.Clear(Color.FromArgb(255, 10, 10, 15));
            DrawRadialGradientBackground(ds);
            DrawWaveform(ds, 1.0f);
        }

        public void Resize(float width, float height) { _width = width; _height = height; }
        public void Dispose() { _device = null; }

        private void DrawRadialGradientBackground(CanvasDrawingSession ds)
        {
            float cx = _width * 0.5f, cy = _height * 0.5f;
            float radius = Math.Max(_width, _height) * 0.7f;
            var geo = CanvasGeometry.CreateEllipse(ds, cx, cy, radius, radius);
            ds.FillGeometry(geo, Color.FromArgb(255, 10, 10, 15));
            var outerGeo = CanvasGeometry.CreateEllipse(ds, cx, cy, radius * 1.5f, radius * 1.5f);
            ds.FillGeometry(outerGeo, Color.FromArgb(255, 2, 2, 3));
        }

        private void DrawWaveform(CanvasDrawingSession ds, float opacity)
        {
            if (_smoothCount < 2) return;
            float centerX = _width * 0.5f, centerY = _height * 0.5f;
            float waveWidth = _width * 0.85f;
            float startX = (_width - waveWidth) * 0.5f;

            DrawWaveformLine(ds, startX, waveWidth, centerY, GlowThickness,
                Color.FromArgb((byte)(40 * opacity), 0, 230, 230), 0.15f);
            DrawMirrorLine(ds, startX, waveWidth, centerY, GlowThickness,
                Color.FromArgb((byte)(20 * opacity), 230, 0, 230), 0.08f);
            DrawWaveformLine(ds, startX, waveWidth, centerY, LineThickness,
                Color.FromArgb((byte)(255 * opacity), 0, 230, 230), 1.0f);
            DrawMirrorLine(ds, startX, waveWidth, centerY, LineThickness * 0.8f,
                Color.FromArgb((byte)(128 * opacity), 230, 0, 230), 0.5f);
        }

        private void DrawWaveformLine(CanvasDrawingSession ds, float startX, float waveWidth,
            float centerY, float thickness, Color baseColor, float amplitudeMul)
        {
            if (_smoothCount < 2) return;
            var strokeStyle = new CanvasStrokeStyle { StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round };
            float step = waveWidth / (_smoothCount - 1);
            for (int i = 0; i < _smoothCount - 1; i++)
            {
                float x1 = startX + i * step, x2 = startX + (i + 1) * step;
                float y1 = centerY - _smoothWave[i] * _height * AmplitudeScale * amplitudeMul;
                float y2 = centerY - _smoothWave[i + 1] * _height * AmplitudeScale * amplitudeMul;
                float t = (float)i / (_smoothCount - 1);
                float amplitude = Math.Abs(_smoothWave[i]);
                float brightness = 0.8f + 0.2f * _smoothBeat;
                byte r = (byte)(Lerp(0, 230, t) * brightness);
                byte g = (byte)(Lerp(230, 0, t) * brightness);
                byte b = (byte)(230 * brightness);
                byte a = (byte)Math.Min(255, (int)(baseColor.A * (0.7f + 0.3f * amplitude)));
                ds.DrawLine(x1, y1, x2, y2, Color.FromArgb(a, r, g, b), thickness, strokeStyle);
            }
        }

        private void DrawMirrorLine(CanvasDrawingSession ds, float startX, float waveWidth,
            float centerY, float thickness, Color baseColor, float amplitudeMul)
        {
            if (_smoothCount < 2) return;
            var strokeStyle = new CanvasStrokeStyle { StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round };
            float step = waveWidth / (_smoothCount - 1);
            for (int i = 0; i < _smoothCount - 1; i++)
            {
                float x1 = startX + i * step, x2 = startX + (i + 1) * step;
                float y1 = centerY + _smoothWave[i] * _height * AmplitudeScale * amplitudeMul;
                float y2 = centerY + _smoothWave[i + 1] * _height * AmplitudeScale * amplitudeMul;
                float t = (float)i / (_smoothCount - 1);
                float brightness = 0.8f + 0.2f * _smoothBeat;
                byte r = (byte)(Lerp(0, 230, t) * brightness);
                byte g = (byte)(Lerp(230, 0, t) * brightness);
                byte b = (byte)(230 * brightness);
                byte a = (byte)Math.Min(255, (int)(baseColor.A * 0.5f));
                ds.DrawLine(x1, y1, x2, y2, Color.FromArgb(a, r, g, b), thickness, strokeStyle);
            }
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        public void ConfigurePipeline(PostProcessPipeline pipeline)
        {
            pipeline.FeedbackOpacity = 0.45f;
            pipeline.FeedbackZoom = 1.0005f;
            pipeline.SlideY = -0.8f;
            pipeline.BloomAmount = 0.35f;
            pipeline.BloomBlur = 8f;
        }
    }
}
