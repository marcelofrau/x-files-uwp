using System;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Windows.Foundation;
using Windows.UI;

namespace XFiles.Visualizers.Visualizers
{
    public sealed class CircularRadialSpectrumVisualizer : IAudioVisualizer
    {
        public string Name => "Circular Radial Spectrum";
        public string Id => "circular-radial-spectrum";

        private CanvasDevice _device;
        private float _width, _height, _time;

        private readonly float[] _smoothBands = new float[AudioData.BandCount];
        private float _smoothBass, _smoothBeat, _smoothAvg;
        private const float AudioSmooth = 0.25f;
        private float _rotationAngle;

        public void Initialize(CanvasDevice device) { _device = device; }

        public void Update(AudioData data, TimeSpan elapsed)
        {
            _time = data.Time;

            float bass = 0, avg = 0;
            for (int i = 0; i < 6; i++) bass += data.BandLevels[i]; bass /= 6f;
            for (int i = 0; i < AudioData.BandCount; i++) avg += data.BandLevels[i]; avg /= AudioData.BandCount;

            _smoothBass += (bass - _smoothBass) * AudioSmooth;
            _smoothBeat += (data.Beat - _smoothBeat) * 0.4f;
            _smoothAvg += (avg - _smoothAvg) * AudioSmooth;

            _rotationAngle += (0.05f + _smoothAvg * 0.1f) * (float)elapsed.TotalSeconds;

            for (int i = 0; i < AudioData.BandCount; i++)
                _smoothBands[i] += (data.BandLevels[i] - _smoothBands[i]) * AudioSmooth;
        }

        public void Draw(CanvasDrawingSession ds)
        {
            if (_device == null || _width == 0 || _height == 0) return;
            ds.Clear(Color.FromArgb(255, 2, 2, 6));

            float cx = _width * 0.5f, cy = _height * 0.5f;
            float minDim = Math.Min(_width, _height);

            DrawOuterRing(ds, cx, cy, minDim);
            DrawRadialBars(ds, cx, cy, minDim);
            DrawInnerRing(ds, cx, cy, minDim);
            DrawCenterEnergy(ds, cx, cy, minDim);
            DrawHUDLines(ds, cx, cy, minDim);
        }

        public void Resize(float width, float height) { _width = width; _height = height; }
        public void Dispose() { _device = null; }

        private void DrawOuterRing(CanvasDrawingSession ds, float cx, float cy, float minDim)
        {
            float ringR = minDim * 0.42f;
            var strokeStyle = new CanvasStrokeStyle { StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round };
            Color ringColor = Color.FromArgb(40, 100, 200, 255);
            var ringGeo = CanvasGeometry.CreateEllipse(ds, cx, cy, ringR, ringR);
            ds.DrawGeometry(ringGeo, ringColor, 1f, strokeStyle);

            float ringR2 = minDim * 0.43f;
            Color ringColor2 = Color.FromArgb(20, 100, 200, 255);
            var ringGeo2 = CanvasGeometry.CreateEllipse(ds, cx, cy, ringR2, ringR2);
            ds.DrawGeometry(ringGeo2, ringColor2, 0.5f, strokeStyle);
        }

        private void DrawRadialBars(CanvasDrawingSession ds, float cx, float cy, float minDim)
        {
            float innerR = minDim * 0.15f;
            float maxBarLen = minDim * 0.28f;
            int barCount = AudioData.BandCount;
            var strokeStyle = new CanvasStrokeStyle { StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round };

            for (int i = 0; i < barCount; i++)
            {
                float angle = _rotationAngle + (float)i / barCount * 2f * (float)Math.PI;
                float level = _smoothBands[i];
                float barLen = level * maxBarLen;
                if (barLen < 1f) barLen = 1f;

                float cosA = (float)Math.Cos(angle);
                float sinA = (float)Math.Sin(angle);
                float x1 = cx + cosA * innerR;
                float y1 = cy + sinA * innerR;
                float x2 = cx + cosA * (innerR + barLen);
                float y2 = cy + sinA * (innerR + barLen);

                float hue = (float)i / barCount;
                Color barColor = HslToRgb(hue, 0.85f, 0.45f + level * 0.3f);
                float barThickness = 2.5f + level * 3f;

                Color glowColor = Color.FromArgb(40, barColor.R, barColor.G, barColor.B);
                ds.DrawLine(x1, y1, x2, y2, glowColor, barThickness * 3f, strokeStyle);
                ds.DrawLine(x1, y1, x2, y2, barColor, barThickness, strokeStyle);

                if (level > 0.6f)
                {
                    float tipR = innerR + barLen;
                    float tipGlow = 2f + level * 3f;
                    ds.FillGeometry(CanvasGeometry.CreateCircle(ds, x2, y2, tipGlow),
                        Color.FromArgb((byte)Math.Min(255, (int)(100 * level)), barColor.R, barColor.G, barColor.B));
                }
            }
        }

        private void DrawInnerRing(CanvasDrawingSession ds, float cx, float cy, float minDim)
        {
            float ringR = minDim * 0.14f;
            Color ringColor = Color.FromArgb(60, 100, 200, 255);
            var ringGeo = CanvasGeometry.CreateEllipse(ds, cx, cy, ringR, ringR);
            ds.DrawGeometry(ringGeo, ringColor, 1.5f);
        }

        private void DrawCenterEnergy(CanvasDrawingSession ds, float cx, float cy, float minDim)
        {
            float energyR = minDim * 0.08f * (0.6f + _smoothBeat * 0.8f);
            float hue = (_time * 0.1f) % 1.0f;
            Color c = HslToRgb(hue, 0.9f, 0.6f + _smoothBeat * 0.3f);

            ds.FillGeometry(CanvasGeometry.CreateEllipse(ds, cx, cy, energyR * 2.5f, energyR * 2.5f),
                Color.FromArgb(15, c.R, c.G, c.B));
            ds.FillGeometry(CanvasGeometry.CreateEllipse(ds, cx, cy, energyR * 1.5f, energyR * 1.5f),
                Color.FromArgb(35, c.R, c.G, c.B));
            ds.FillGeometry(CanvasGeometry.CreateEllipse(ds, cx, cy, energyR, energyR), c);
            ds.FillGeometry(CanvasGeometry.CreateEllipse(ds, cx, cy, energyR * 0.3f, energyR * 0.3f), Colors.White);
        }

        private void DrawHUDLines(CanvasDrawingSession ds, float cx, float cy, float minDim)
        {
            float lineR = minDim * 0.44f;
            var strokeStyle = new CanvasStrokeStyle { StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round };
            Color lineColor = Color.FromArgb(30, 100, 200, 255);

            for (int i = 0; i < 4; i++)
            {
                float angle = _rotationAngle * 0.3f + (float)i / 4 * 2f * (float)Math.PI;
                float x1 = cx + (float)Math.Cos(angle) * lineR;
                float y1 = cy + (float)Math.Sin(angle) * lineR;
                float x2 = cx + (float)Math.Cos(angle) * (lineR + minDim * 0.03f);
                float y2 = cy + (float)Math.Sin(angle) * (lineR + minDim * 0.03f);
                ds.DrawLine(x1, y1, x2, y2, lineColor, 1f, strokeStyle);
            }

            for (int i = 0; i < 8; i++)
            {
                float angle = (float)i / 8 * 2f * (float)Math.PI;
                float dotR = minDim * 0.45f;
                float dx = cx + (float)Math.Cos(angle) * dotR;
                float dy = cy + (float)Math.Sin(angle) * dotR;
                ds.FillGeometry(CanvasGeometry.CreateCircle(ds, dx, dy, 1.5f), lineColor);
            }
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
