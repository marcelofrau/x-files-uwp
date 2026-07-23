using System;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Windows.Foundation;
using Windows.UI;

namespace XFiles.Visualizers.Visualizers
{
    public sealed class FeedbackTrailVisualizer : IAudioVisualizer
    {
        public string Name => "Feedback Trail";
        public string Id => "feedback-trail";

        private CanvasDevice _device;
        private float _width, _height, _time;

        private readonly float[] _smoothBands = new float[AudioData.BandCount];
        private float _smoothBass, _smoothMid, _smoothTreble, _smoothBeat;
        private const float AudioSmooth = 0.3f;

        private const int TrailSegments = 48;
        private const int PointsPerSegment = 64;
        private float _rotation;
        private float _driftX, _driftY;

        private float[,] _trailX, _trailY;
        private int _trailHead;
        private const int TrailDepth = 20;

        public void Initialize(CanvasDevice device)
        {
            _device = device;
            _trailX = new float[TrailDepth, PointsPerSegment];
            _trailY = new float[TrailDepth, PointsPerSegment];
        }

        public void Update(AudioData data, TimeSpan elapsed)
        {
            _time = data.Time;
            float bass = 0, mid = 0, treble = 0;
            for (int i = 0; i < 6; i++) bass += data.BandLevels[i]; bass /= 6f;
            for (int i = 10; i < 16; i++) mid += data.BandLevels[i]; mid /= 6f;
            for (int i = 20; i < 26; i++) treble += data.BandLevels[i]; treble /= 6f;
            _smoothBass += (bass - _smoothBass) * AudioSmooth;
            _smoothMid += (mid - _smoothMid) * AudioSmooth;
            _smoothTreble += (treble - _smoothTreble) * AudioSmooth;
            _smoothBeat += (data.Beat - _smoothBeat) * 0.4f;
            for (int i = 0; i < AudioData.BandCount; i++)
                _smoothBands[i] += (data.BandLevels[i] - _smoothBands[i]) * AudioSmooth;

            _rotation += (0.3f + _smoothBass * 1.2f + _smoothBeat * 0.8f) * (float)elapsed.TotalSeconds;

            float driftSpeed = 0.6f + _smoothBass * 1.0f;
            _driftX += (float)Math.Sin(_time * driftSpeed * 0.7f) * 200f * (float)elapsed.TotalSeconds;
            _driftY += (float)Math.Cos(_time * driftSpeed * 0.5f) * 160f * (float)elapsed.TotalSeconds;
            _driftX += (float)Math.Sin(_time * 0.13f) * 80f * (float)elapsed.TotalSeconds;
            _driftY += (float)Math.Cos(_time * 0.17f) * 60f * (float)elapsed.TotalSeconds;
            _driftX += (float)Math.Sin(_time * 2.1f + _smoothBass * 3f) * 120f * (float)elapsed.TotalSeconds * _smoothBass;
            _driftY += (float)Math.Cos(_time * 1.7f + _smoothBeat * 4f) * 100f * (float)elapsed.TotalSeconds * _smoothBeat;
            float maxDrift = Math.Min(_width, _height) * 0.45f;
            _driftX = Math.Clamp(_driftX, -maxDrift, maxDrift);
            _driftY = Math.Clamp(_driftY, -maxDrift, maxDrift);

            ComputeCurrentFrame(out float[] frameX, out float[] frameY);

            for (int t = TrailDepth - 1; t > 0; t--)
                for (int p = 0; p < PointsPerSegment; p++)
                {
                    _trailX[t, p] = _trailX[t - 1, p];
                    _trailY[t, p] = _trailY[t - 1, p];
                }
            for (int p = 0; p < PointsPerSegment; p++)
            {
                _trailX[0, p] = frameX[p];
                _trailY[0, p] = frameY[p];
            }
        }

        public void Draw(CanvasDrawingSession ds)
        {
            if (_device == null || _width == 0 || _height == 0) return;
            ds.Clear(Color.FromArgb(255, 2, 2, 5));

            float cx = _width * 0.5f + _driftX, cy = _height * 0.5f + _driftY;

            for (int t = TrailDepth - 1; t >= 0; t--)
            {
                float age = (float)t / TrailDepth;
                float alpha = (1f - age) * 0.7f;
                float hue = (_time * 0.06f + age * 0.3f) % 1.0f;
                float lum = 0.3f + (1f - age) * 0.4f;
                Color c = HslToRgb(hue, 0.85f, lum);
                byte a = (byte)(alpha * 255);
                float thickness = (1f + (1f - age) * 3.5f) * (1f + _smoothBeat * 0.5f);

                var strokeStyle = new CanvasStrokeStyle { StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round };
                for (int p = 0; p < PointsPerSegment - 1; p++)
                {
                    ds.DrawLine(_trailX[t, p], _trailY[t, p],
                        _trailX[t, p + 1], _trailY[t, p + 1],
                        Color.FromArgb(a, c.R, c.G, c.B), thickness, strokeStyle);
                }
            }

            DrawCenterFlash(ds, cx, cy);
        }

        public void Resize(float width, float height) { _width = width; _height = height; }
        public void Dispose() { _device = null; }

        private void ComputeCurrentFrame(out float[] frameX, out float[] frameY)
        {
            frameX = new float[PointsPerSegment];
            frameY = new float[PointsPerSegment];
            float cx = _width * 0.5f + _driftX, cy = _height * 0.5f + _driftY;
            float minDim = Math.Min(_width, _height);
            float baseRadius = minDim * 0.3f;

            for (int p = 0; p < PointsPerSegment; p++)
            {
                float t = (float)p / PointsPerSegment;
                float angle = _rotation + t * 2f * (float)Math.PI;
                int bandIdx = Math.Min((int)(t * AudioData.BandCount), AudioData.BandCount - 1);
                float level = _smoothBands[bandIdx];
                float wobble = (float)Math.Sin(t * 8f + _time * 3f) * 25f * (1f + level * 1.5f);
                float bassPulse = (float)Math.Sin(t * 3f + _time * 2f) * 20f * _smoothBass;
                float radius = baseRadius * (0.5f + level * 0.5f) + wobble + bassPulse;
                frameX[p] = cx + (float)Math.Cos(angle) * radius;
                frameY[p] = cy + (float)Math.Sin(angle) * radius;
            }
        }

        private void DrawCenterFlash(CanvasDrawingSession ds, float cx, float cy)
        {
            float r = 5f + _smoothBeat * 12f;
            Color c = HslToRgb((_time * 0.12f) % 1.0f, 1f, 0.85f);
            ds.FillGeometry(CanvasGeometry.CreateEllipse(ds, cx, cy, r * 2f, r * 2f),
                Color.FromArgb(20, c.R, c.G, c.B));
            ds.FillGeometry(CanvasGeometry.CreateEllipse(ds, cx, cy, r, r), c);
            ds.FillGeometry(CanvasGeometry.CreateEllipse(ds, cx, cy, r * 0.3f, r * 0.3f), Colors.White);
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

        public void ConfigurePipeline(PostProcessPipeline pipeline)
        {
            pipeline.FeedbackOpacity = 0.55f;
            pipeline.FeedbackZoom = 1.01f;
            pipeline.FeedbackDecay = 0.02f;
        }
    }
}
