using System;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Windows.Foundation;
using Windows.UI;

namespace XFiles.Visualizers.Visualizers
{
    public sealed class RetroOscilloscopeVisualizer : IAudioVisualizer
    {
        public string Name => "Retro Oscilloscope";
        public string Id => "retro-oscilloscope";

        private CanvasDevice _device;
        private float _width, _height, _time;

        private float[] _prevWaveform;
        private int _prevWaveformCount;
        private float _smoothBass, _smoothMid, _smoothBeat;
        private const float AudioSmooth = 0.25f;
        private const int GhostFrames = 4;
        private float[][] _ghostHistory;
        private int[] _ghostCounts;
        private int _ghostIndex;

        public void Initialize(CanvasDevice device)
        {
            _device = device;
            _ghostHistory = new float[GhostFrames][];
            _ghostCounts = new int[GhostFrames];
            for (int i = 0; i < GhostFrames; i++)
            {
                _ghostHistory[i] = new float[2048];
                _ghostCounts[i] = 0;
            }
        }

        public void Update(AudioData data, TimeSpan elapsed)
        {
            _time = data.Time;

            float bass = 0, mid = 0;
            for (int i = 0; i < 6; i++) bass += data.BandLevels[i]; bass /= 6f;
            for (int i = 10; i < 16; i++) mid += data.BandLevels[i]; mid /= 6f;
            _smoothBass += (bass - _smoothBass) * AudioSmooth;
            _smoothMid += (mid - _smoothMid) * AudioSmooth;
            _smoothBeat += (data.Beat - _smoothBeat) * 0.4f;

            _prevWaveform = data.Waveform;
            _prevWaveformCount = data.WaveformCount;

            _ghostHistory[_ghostIndex] = data.Waveform;
            _ghostCounts[_ghostIndex] = data.WaveformCount;
            _ghostIndex = (_ghostIndex + 1) % GhostFrames;
        }

        public void Draw(CanvasDrawingSession ds)
        {
            if (_device == null || _width == 0 || _height == 0) return;
            ds.Clear(Color.FromArgb(255, 2, 8, 2));

            DrawGrid(ds);
            DrawGhostTraces(ds);
            DrawWaveform(ds);
            DrawLissajous(ds);
            DrawPhosphorGlow(ds);
            DrawVignette(ds);
        }

        public void Resize(float width, float height) { _width = width; _height = height; }
        public void Dispose() { _device = null; }

        private void DrawGrid(CanvasDrawingSession ds)
        {
            Color gridColor = Color.FromArgb(18, 50, 180, 50);
            var strokeStyle = new CanvasStrokeStyle { StartCap = CanvasCapStyle.Flat, EndCap = CanvasCapStyle.Flat };

            float stepX = _width / 10f;
            for (int i = 1; i < 10; i++)
            {
                float x = i * stepX;
                ds.DrawLine(x, 0, x, _height, gridColor, 0.5f, strokeStyle);
            }

            float stepY = _height / 8f;
            for (int i = 1; i < 8; i++)
            {
                float y = i * stepY;
                ds.DrawLine(0, y, _width, y, gridColor, 0.5f, strokeStyle);
            }

            Color centerColor = Color.FromArgb(35, 50, 180, 50);
            ds.DrawLine(_width * 0.5f, 0, _width * 0.5f, _height, centerColor, 1f, strokeStyle);
            ds.DrawLine(0, _height * 0.5f, _width, _height * 0.5f, centerColor, 1f, strokeStyle);
        }

        private void DrawGhostTraces(CanvasDrawingSession ds)
        {
            Color phosphorGreen = Color.FromArgb(255, 40, 220, 40);
            var strokeStyle = new CanvasStrokeStyle { StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round };

            for (int g = 0; g < GhostFrames; g++)
            {
                int gi = (_ghostIndex + g) % GhostFrames;
                float[] wave = _ghostHistory[gi];
                int count = _ghostCounts[gi];
                if (wave == null || count <= 0) continue;

                float age = (float)(GhostFrames - g) / GhostFrames;
                byte alpha = (byte)Math.Min(255, (int)(30 * (1f - age)));
                if (alpha < 3) continue;

                float cy = _height * 0.5f;
                float amplitude = _height * 0.3f;
                float prevX = 0, prevY = cy;
                bool first = true;

                int step = Math.Max(1, count / 400);
                for (int i = 0; i < count; i += step)
                {
                    float x = (float)i / count * _width;
                    float y = cy + wave[i] * amplitude;
                    if (first) { prevX = x; prevY = y; first = false; continue; }
                    ds.DrawLine(prevX, prevY, x, y, Color.FromArgb(alpha, phosphorGreen.R, phosphorGreen.G, phosphorGreen.B), 1.5f, strokeStyle);
                    prevX = x; prevY = y;
                }
            }
        }

        private void DrawWaveform(CanvasDrawingSession ds)
        {
            if (_prevWaveform == null || _prevWaveformCount <= 0) return;

            Color phosphorGreen = Color.FromArgb(255, 40, 255, 40);
            Color glowGreen = Color.FromArgb(40, 40, 255, 40);
            var strokeStyle = new CanvasStrokeStyle { StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round };

            float cy = _height * 0.5f;
            float amplitude = _height * 0.3f;
            float prevX = 0, prevY = cy;
            bool first = true;

            int step = Math.Max(1, _prevWaveformCount / 500);
            for (int i = 0; i < _prevWaveformCount; i += step)
            {
                float x = (float)i / _prevWaveformCount * _width;
                float y = cy + _prevWaveform[i] * amplitude;
                if (first) { prevX = x; prevY = y; first = false; continue; }

                ds.DrawLine(prevX, prevY, x, y, glowGreen, 5f, strokeStyle);
                ds.DrawLine(prevX, prevY, x, y, phosphorGreen, 1.8f, strokeStyle);
                prevX = x; prevY = y;
            }
        }

        private void DrawLissajous(CanvasDrawingSession ds)
        {
            if (_prevWaveform == null || _prevWaveformCount <= 0) return;

            float cx = _width * 0.5f, cy = _height * 0.5f;
            float radius = Math.Min(_width, _height) * 0.12f * (0.5f + _smoothBass * 0.8f);
            Color lissaColor = Color.FromArgb(50, 40, 255, 40);
            var strokeStyle = new CanvasStrokeStyle { StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round };

            float freqMul = 1f + _smoothBass * 3f;
            float phase = _time * 0.5f;
            int pointCount = 200;
            float prevX = 0, prevY = 0;
            bool first = true;

            for (int i = 0; i <= pointCount; i++)
            {
                float t = (float)i / pointCount * 2f * (float)Math.PI;
                float lx = cx + (float)Math.Sin(t * freqMul + phase) * radius;
                float ly = cy + (float)Math.Cos(t * 2f + phase * 0.7f) * radius;
                if (first) { prevX = lx; prevY = ly; first = false; continue; }
                ds.DrawLine(prevX, prevY, lx, ly, lissaColor, 1f, strokeStyle);
                prevX = lx; prevY = ly;
            }
        }

        private void DrawPhosphorGlow(CanvasDrawingSession ds)
        {
            float intensity = 0.4f + _smoothBeat * 0.4f;
            byte a = (byte)Math.Min(255, (int)(12 * intensity));
            Color glow = Color.FromArgb(a, 40, 200, 40);
            var geo = CanvasGeometry.CreateEllipse(ds, _width * 0.5f, _height * 0.5f, _width * 0.4f, _height * 0.35f);
            ds.FillGeometry(geo, glow);
        }

        private void DrawVignette(CanvasDrawingSession ds)
        {
            Color vignette = Color.FromArgb(180, 0, 0, 0);
            float w = _width, h = _height;
            float stripW = w * 0.15f;

            ds.FillRectangle(0, 0, stripW, h, Color.FromArgb(120, 0, 0, 0));
            ds.FillRectangle(w - stripW, 0, stripW, h, Color.FromArgb(120, 0, 0, 0));
            ds.FillRectangle(0, 0, w, h * 0.08f, Color.FromArgb(80, 0, 0, 0));
            ds.FillRectangle(0, h - h * 0.08f, w, h * 0.08f, Color.FromArgb(80, 0, 0, 0));
        }

        private static Color HslToRgb(float h, float s, float l)
        {
            h -= (float)Math.Floor(h); float hue = h * 360f;
            float c = (1f - Math.Abs(2f * l - 1f)) * s;
            float x = c * (1f - Math.Abs((hue / 60f) % 2f - 1f));
            float m = l - c / 2f;
            float r, g, b;
            if (hue < 60) { r = c; g = x; b = 0; }
            else if (hue < 120) { r = x; g = c; b = 0; }
            else if (hue < 180) { r = 0; g = c; b = x; }
            else if (hue < 240) { r = 0; g = x; b = c; }
            else if (hue < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            return Color.FromArgb(255, (byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
        }
    }
}
