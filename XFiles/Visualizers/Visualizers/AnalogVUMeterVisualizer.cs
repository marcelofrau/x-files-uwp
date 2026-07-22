using System;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Windows.Foundation;
using Windows.UI;

namespace XFiles.Visualizers.Visualizers
{
    public sealed class AnalogVUMeterVisualizer : IAudioVisualizer
    {
        public string Name => "Analog VU Meter";
        public string Id => "analog-vu-meter";

        private CanvasDevice _device;
        private float _width, _height, _time;

        private float _needleBass, _needleMid, _needleTreble;
        private float _velBass, _velMid, _velTreble;
        private float _smoothBass, _smoothMid, _smoothTreble, _smoothBeat;
        private const float AudioSmooth = 0.35f;
        private const float SpringK = 180f;
        private const float Damping = 8f;
        private const float InputGain = 0.7f;

        public void Initialize(CanvasDevice device) { _device = device; }

        public void Update(AudioData data, TimeSpan elapsed)
        {
            _time = data.Time;
            float dt = (float)elapsed.TotalSeconds;
            dt = Math.Min(dt, 0.05f);

            float bass = 0, mid = 0, treble = 0;
            for (int i = 0; i < 6; i++) bass += data.BandLevels[i]; bass /= 6f;
            for (int i = 10; i < 16; i++) mid += data.BandLevels[i]; mid /= 6f;
            for (int i = 20; i < 26; i++) treble += data.BandLevels[i]; treble /= 6f;

            _smoothBass += (bass - _smoothBass) * AudioSmooth;
            _smoothMid += (mid - _smoothMid) * AudioSmooth;
            _smoothTreble += (treble - _smoothTreble) * AudioSmooth;
            _smoothBeat += (data.Beat - _smoothBeat) * 0.4f;

            SimulateNeedle(ref _needleBass, ref _velBass, Math.Min(1f, _smoothBass * InputGain), dt);
            SimulateNeedle(ref _needleMid, ref _velMid, Math.Min(1f, _smoothMid * InputGain), dt);
            SimulateNeedle(ref _needleTreble, ref _velTreble, Math.Min(1f, _smoothTreble * InputGain), dt);
        }

        private void SimulateNeedle(ref float pos, ref float vel, float target, float dt)
        {
            float force = SpringK * (target - pos) - Damping * vel;
            vel += force * dt;
            pos += vel * dt;
            if (pos < 0f) { pos = 0f; vel = Math.Max(0, vel); }
            if (pos > 1f) { pos = 1f; vel = Math.Min(0, vel); }
        }

        public void Draw(CanvasDrawingSession ds)
        {
            if (_device == null || _width == 0 || _height == 0) return;
            ds.Clear(Color.FromArgb(255, 12, 10, 8));

            float meterW = _width * 0.28f;
            float meterH = meterW * 0.7f;
            float spacing = _width * 0.02f;
            float totalW = meterW * 3 + spacing * 2;
            float startX = (_width - totalW) * 0.5f;
            float meterY = _height * 0.35f;

            DrawOneMeter(ds, startX, meterY, meterW, meterH, _needleBass, "BASS", 0.0f);
            DrawOneMeter(ds, startX + meterW + spacing, meterY, meterW, meterH, _needleMid, "MID", 0.33f);
            DrawOneMeter(ds, startX + (meterW + spacing) * 2, meterY, meterW, meterH, _needleTreble, "TREBLE", 0.66f);

            DrawIncandescentGlow(ds);
        }

        private void DrawOneMeter(CanvasDrawingSession ds, float x, float y, float w, float h, float needlePos, string label, float hueBase)
        {
            float cornerR = w * 0.03f;

            Color bgColor = Color.FromArgb(255, 25, 22, 18);
            ds.FillRoundedRectangle(x, y, w, h, cornerR, cornerR, bgColor);

            Color bezelColor = Color.FromArgb(255, 45, 40, 35);
            ds.DrawRoundedRectangle(x, y, w, h, cornerR, cornerR, bezelColor, 2f);

            Color faceColor = Color.FromArgb(255, 200, 195, 180);
            float inset = w * 0.06f;
            ds.FillRoundedRectangle(x + inset, y + inset, w - inset * 2, h - inset * 2, cornerR, cornerR, faceColor);

            float pivotX = x + w * 0.5f;
            float pivotY = y + h * 0.82f;
            float needleLen = h * 0.6f;
            float angle = -90f + needlePos * 180f;
            float rad = angle * (float)Math.PI / 180f;
            float tipX = pivotX + (float)Math.Cos(rad) * needleLen;
            float tipY = pivotY + (float)Math.Sin(rad) * needleLen;

            DrawScaleArc(ds, pivotX, pivotY, needleLen * 0.85f, x, w);

            var needleStyle = new CanvasStrokeStyle { StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round };
            Color needleColor = needlePos > 0.8f
                ? Color.FromArgb(255, 200, 30, 20)
                : Color.FromArgb(255, 30, 30, 30);
            ds.DrawLine(pivotX, pivotY, tipX, tipY, needleColor, 1.8f, needleStyle);

            float pivotR = w * 0.025f;
            ds.FillGeometry(CanvasGeometry.CreateCircle(ds, pivotX, pivotY, pivotR), Color.FromArgb(255, 60, 55, 50));

            if (needlePos > 0.75f)
            {
                float redZone = (needlePos - 0.75f) / 0.25f;
                float flashAlpha = (byte)Math.Min(255, (int)(redZone * 40 * (0.5f + _smoothBeat * 0.5f)));
                ds.FillRoundedRectangle(x + inset, y + inset, w - inset * 2, h * 0.25f, cornerR, cornerR,
                    Color.FromArgb((byte)flashAlpha, 255, 40, 20));
            }

            float labelY = y + h - inset - h * 0.08f;
            var labelStyle = new CanvasTextFormat
            {
                FontSize = w * 0.07f,
                HorizontalAlignment = CanvasHorizontalAlignment.Center,
                VerticalAlignment = CanvasVerticalAlignment.Center
            };
            ds.DrawText(label, pivotX, labelY, Color.FromArgb(200, 60, 55, 50), labelStyle);
        }

        private void DrawScaleArc(CanvasDrawingSession ds, float cx, float cy, float radius, float meterX, float meterW)
        {
            int tickCount = 21;
            var tickStyle = new CanvasStrokeStyle { StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round };

            for (int i = 0; i <= tickCount; i++)
            {
                float t = (float)i / tickCount;
                float angle = (-90f + t * 180f) * (float)Math.PI / 180f;
                float innerR = radius * 0.88f;
                float outerR = radius;
                float x1 = cx + (float)Math.Cos(angle) * innerR;
                float y1 = cy + (float)Math.Sin(angle) * innerR;
                float x2 = cx + (float)Math.Cos(angle) * outerR;
                float y2 = cy + (float)Math.Sin(angle) * outerR;

                Color tickColor = t > 0.75f
                    ? Color.FromArgb(180, 200, 30, 20)
                    : Color.FromArgb(120, 80, 75, 70);
                float tickW = (i % 5 == 0) ? 1.5f : 0.8f;
                ds.DrawLine(x1, y1, x2, y2, tickColor, tickW, tickStyle);
            }
        }

        private void DrawIncandescentGlow(CanvasDrawingSession ds)
        {
            float glowIntensity = 0.5f + _smoothBeat * 0.5f;
            byte a = (byte)Math.Min(255, (int)(4 * glowIntensity));
            Color warmGlow = Color.FromArgb(a, 255, 200, 120);
            var geo = CanvasGeometry.CreateEllipse(ds, _width * 0.5f, _height * 0.5f, _width * 0.45f, _height * 0.4f);
            ds.FillGeometry(geo, warmGlow);
        }

        public void Resize(float width, float height) { _width = width; _height = height; }
        public void Dispose() { _device = null; }

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

        public void ConfigurePipeline(PostProcessPipeline pipeline)
        {
        }
    }
}
