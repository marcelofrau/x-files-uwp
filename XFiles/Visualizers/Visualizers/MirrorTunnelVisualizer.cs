using System;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Windows.Foundation;
using Windows.UI;

namespace XFiles.Visualizers.Visualizers
{
    public sealed class MirrorTunnelVisualizer : IAudioVisualizer
    {
        public string Name => "Mirror Tunnel";
        public string Id => "mirror-tunnel";

        private CanvasDevice _device;
        private float _width, _height, _time;

        private readonly float[] _smoothBands = new float[AudioData.BandCount];
        private float _smoothBeat, _smoothBass, _smoothMid, _smoothTreble;
        private const float AudioSmooth = 0.25f;
        private const int RingCount = 30;
        private const float TunnelSpeed = 0.3f;
        private const float WallThickness = 2.5f;

        public void Initialize(CanvasDevice device) { _device = device; }

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
            for (int i = 0; i < AudioData.BandCount; i++)
                _smoothBands[i] += (data.BandLevels[i] - _smoothBands[i]) * AudioSmooth;
            _smoothBeat += (data.Beat - _smoothBeat) * 0.35f;
        }

        public void Draw(CanvasDrawingSession ds)
        {
            if (_device == null || _width == 0 || _height == 0) return;
            ds.Clear(Color.FromArgb(255, 2, 2, 5));
            DrawTunnel(ds);
        }

        public void Resize(float width, float height) { _width = width; _height = height; }

        public void Dispose() { _device = null; }

        private void DrawTunnel(CanvasDrawingSession ds)
        {
            float cx = _width * 0.5f, cy = _height * 0.5f;
            float scroll = _time * TunnelSpeed;
            float warp = 1f + _smoothBeat * 0.15f;
            var strokeStyle = new CanvasStrokeStyle { StartCap = CanvasCapStyle.Square, EndCap = CanvasCapStyle.Square };

            for (int ring = RingCount; ring >= 1; ring--)
            {
                float depthNorm = (float)ring / RingCount;
                float depth = depthNorm + (scroll % (1f / RingCount));
                if (depth <= 0.001f || depth > 1.5f) continue;
                float scale = Math.Min(1f / (depth * 4f + 0.1f), 2f);
                int bandIdx = ring % AudioData.BandCount;
                float bandLevel = _smoothBands[bandIdx];
                float halfW = cx * scale * warp, halfH = cy * scale * warp;
                float fog = 1f - depthNorm * 0.7f;
                float thickness = WallThickness + bandLevel * 4f;

                float x1 = cx - halfW, y1 = cy - halfH, x2 = cx + halfW, y2 = cy + halfH;

                int topBand = bandIdx;
                ds.DrawLine(x1, y1, x2, y1, HslToRgb(((float)topBand / AudioData.BandCount + _time * 0.03f) % 1.0f, 0.7f + _smoothBands[topBand] * 0.3f, (0.2f + _smoothBands[topBand] * 0.5f) * fog), thickness, strokeStyle);

                int rightBand = (bandIdx + 7) % AudioData.BandCount;
                ds.DrawLine(x2, y1, x2, y2, HslToRgb(((float)rightBand / AudioData.BandCount + _time * 0.03f) % 1.0f, 0.7f + _smoothBands[rightBand] * 0.3f, (0.2f + _smoothBands[rightBand] * 0.5f) * fog), thickness, strokeStyle);

                int bottomBand = (bandIdx + 13) % AudioData.BandCount;
                ds.DrawLine(x2, y2, x1, y2, HslToRgb(((float)bottomBand / AudioData.BandCount + _time * 0.03f) % 1.0f, 0.7f + _smoothBands[bottomBand] * 0.3f, (0.2f + _smoothBands[bottomBand] * 0.5f) * fog), thickness, strokeStyle);

                int leftBand = (bandIdx + 20) % AudioData.BandCount;
                ds.DrawLine(x1, y2, x1, y1, HslToRgb(((float)leftBand / AudioData.BandCount + _time * 0.03f) % 1.0f, 0.7f + _smoothBands[leftBand] * 0.3f, (0.2f + _smoothBands[leftBand] * 0.5f) * fog), thickness, strokeStyle);

                if (bandLevel > 0.3f && depthNorm < 0.6f)
                {
                    float dotSize = 1.5f + bandLevel * 2f;
                    float dotAlpha = fog * bandLevel;
                    byte da = (byte)Math.Min(255, (int)(255 * dotAlpha));
                    var dotColor = HslToRgb(((float)bandIdx / AudioData.BandCount + _time * 0.03f) % 1.0f, 1f, 0.7f);
                    var c = Color.FromArgb(da, dotColor.R, dotColor.G, dotColor.B);
                    ds.FillGeometry(CanvasGeometry.CreateCircle(ds, x1, y1, dotSize), c);
                    ds.FillGeometry(CanvasGeometry.CreateCircle(ds, x2, y1, dotSize), c);
                    ds.FillGeometry(CanvasGeometry.CreateCircle(ds, x2, y2, dotSize), c);
                    ds.FillGeometry(CanvasGeometry.CreateCircle(ds, x1, y2, dotSize), c);
                }
            }

            float centerSize = 4f + _smoothBeat * 6f;
            float centerHue = (_time * 0.15f) % 1.0f;
            ds.FillGeometry(CanvasGeometry.CreateEllipse(ds, cx, cy, centerSize, centerSize), HslToRgb(centerHue, 1f, 0.8f + _smoothBeat * 0.2f));
            ds.FillGeometry(CanvasGeometry.CreateEllipse(ds, cx, cy, centerSize * 0.3f, centerSize * 0.3f), Colors.White);

            // Spectrum bars at bottom of each ring, receding into depth
            const int BarCount = 14;
            float minDim = Math.Min(_width, _height);
            for (int ring = RingCount; ring >= 1; ring--)
            {
                float ringDepthNorm = (float)ring / RingCount;
                float ringDepth = ringDepthNorm + (scroll % (1f / RingCount));
                if (ringDepth <= 0.001f || ringDepth > 1.5f) continue;
                float ringScale = Math.Min(1f / (ringDepth * 4f + 0.1f), 2f);
                int ringBandIdx = ring % AudioData.BandCount;
                float ringBandLevel = _smoothBands[ringBandIdx];
                float ringHalfW = cx * ringScale * warp, ringHalfH = cy * ringScale * warp;
                float ringFog = 1f - ringDepthNorm * 0.7f;

                float rx1 = cx - ringHalfW, rx2 = cx + ringHalfW, ry2 = cy + ringHalfH;
                float bottomWidth = rx2 - rx1;
                float barWidth = bottomWidth / BarCount;
                float maxBarHeight = minDim * 0.26f;

                for (int b = 0; b < BarCount; b++)
                {
                    int barBand = (ringBandIdx + b * 2) % AudioData.BandCount;
                    float barLevel = _smoothBands[barBand];
                    float barH = barLevel * maxBarHeight * ringFog;
                    if (barH < 0.5f) continue;

                    float barX = rx1 + b * barWidth + barWidth * 0.15f;
                    float barW = barWidth * 0.7f;
                    float barY = ry2 - barH;

                    float hue = ((float)barBand / AudioData.BandCount + _time * 0.03f) % 1.0f;
                    Color barColor = HslToRgb(hue, 0.7f + barLevel * 0.3f, (0.3f + barLevel * 0.4f) * ringFog);

                    // Glow layer: wider, semi-transparent
                    byte glowAlpha = (byte)Math.Min(255, (int)(30 * ringFog));
                    var glowColor = Color.FromArgb(glowAlpha, barColor.R, barColor.G, barColor.B);
                    ds.FillRectangle(barX - barW, barY, barW * 3f, barH, glowColor);

                    // Sharp bar on top
                    byte barAlpha = (byte)Math.Min(255, (int)(ringFog * 200));
                    var sharpColor = Color.FromArgb(barAlpha, barColor.R, barColor.G, barColor.B);
                    ds.FillRectangle(barX, barY, barW, barH, sharpColor);
                }
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

        public void ConfigurePipeline(PostProcessPipeline pipeline)
        {
        }
    }
}
