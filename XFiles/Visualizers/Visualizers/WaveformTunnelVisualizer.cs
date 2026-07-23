using System;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Windows.Foundation;
using Windows.UI;

namespace XFiles.Visualizers.Visualizers
{
    public sealed class WaveformTunnelVisualizer : IAudioVisualizer
    {
        public string Name => "Waveform Tunnel";
        public string Id => "waveform-tunnel";

        private CanvasDevice _device;
        private float _width, _height, _time;

        private float[] _waveform;
        private int _waveformCount;
        private float _smoothBass, _smoothBeat;
        private const float AudioSmooth = 0.25f;
        private const int RingCount = 25;
        private const float TunnelSpeed = 0.25f;
        private const int WaveformSegments = 128;

        public void Initialize(CanvasDevice device) { _device = device; }

        public void Update(AudioData data, TimeSpan elapsed)
        {
            _time = data.Time;
            _waveform = data.Waveform;
            _waveformCount = data.WaveformCount;

            float bass = 0;
            for (int i = 0; i < 6; i++) bass += data.BandLevels[i]; bass /= 6f;
            _smoothBass += (bass - _smoothBass) * AudioSmooth;
            _smoothBeat += (data.Beat - _smoothBeat) * 0.4f;
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
            float minDim = Math.Min(_width, _height);
            float scroll = _time * TunnelSpeed;
            var strokeStyle = new CanvasStrokeStyle { StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round };

            for (int ring = RingCount; ring >= 1; ring--)
            {
                float depthNorm = (float)ring / RingCount;
                float depth = depthNorm + (scroll % (1f / RingCount));
                if (depth <= 0.001f || depth > 1.5f) continue;

                float scale = Math.Min(1f / (depth * 3f + 0.1f), 2.5f);
                float fog = 1f - depthNorm * 0.65f;
                float baseRadius = minDim * 0.15f * scale;
                float amplitude = minDim * 0.10f * scale;

                float hue = ((depthNorm * 0.4f) + _time * 0.02f) % 1.0f;
                float sat = 0.7f + _smoothBeat * 0.3f;
                float lum = 0.35f + _smoothBeat * 0.15f;

                int segCount = Math.Min(_waveformCount > 0 ? _waveformCount : WaveformSegments, WaveformSegments);
                float twoPi = (float)(Math.PI * 2.0);

                // Glow pass
                {
                    float glowLum = Math.Min(1f, lum * 0.6f);
                    Color glowColor = HslToRgb(hue, sat, glowLum);
                    byte glowA = (byte)Math.Min(255, (int)(35 * fog));
                    Color glowLine = Color.FromArgb(glowA, glowColor.R, glowColor.G, glowColor.B);

                    float prevAngle = 0f;
                    float prevR = baseRadius + GetWaveformSample(0) * amplitude;
                    float prevX = cx + (float)Math.Cos(prevAngle) * prevR;
                    float prevY = cy + (float)Math.Sin(prevAngle) * prevR;

                    for (int i = 1; i <= segCount; i++)
                    {
                        float angle = (float)i / segCount * twoPi;
                        int idx = (int)((float)i / segCount * _waveformCount) % Math.Max(1, _waveformCount);
                        float sample = _waveform != null && _waveformCount > 0 ? _waveform[idx] : 0f;
                        float r = baseRadius + sample * amplitude;
                        float x = cx + (float)Math.Cos(angle) * r;
                        float y = cy + (float)Math.Sin(angle) * r;
                        ds.DrawLine(prevX, prevY, x, y, glowLine, 5f + _smoothBeat * 3f, strokeStyle);
                        prevX = x; prevY = y;
                    }
                }

                // Sharp pass
                {
                    Color sharpColor = HslToRgb(hue, sat, lum);
                    byte sharpA = (byte)Math.Min(255, (int)(220 * fog));
                    Color sharpLine = Color.FromArgb(sharpA, sharpColor.R, sharpColor.G, sharpColor.B);

                    float prevAngle = 0f;
                    float prevR = baseRadius + GetWaveformSample(0) * amplitude;
                    float prevX = cx + (float)Math.Cos(prevAngle) * prevR;
                    float prevY = cy + (float)Math.Sin(prevAngle) * prevR;

                    for (int i = 1; i <= segCount; i++)
                    {
                        float angle = (float)i / segCount * twoPi;
                        int idx = (int)((float)i / segCount * _waveformCount) % Math.Max(1, _waveformCount);
                        float sample = _waveform != null && _waveformCount > 0 ? _waveform[idx] : 0f;
                        float r = baseRadius + sample * amplitude;
                        float x = cx + (float)Math.Cos(angle) * r;
                        float y = cy + (float)Math.Sin(angle) * r;
                        ds.DrawLine(prevX, prevY, x, y, sharpLine, 2f, strokeStyle);
                        prevX = x; prevY = y;
                    }
                }
            }

            // Center orb
            float orbRadius = minDim * 0.025f * (1f + _smoothBeat * 0.5f);
            float orbHue = (_time * 0.1f) % 1.0f;
            var orbGeo = CanvasGeometry.CreateEllipse(ds, cx, cy, orbRadius, orbRadius);
            ds.FillGeometry(orbGeo, HslToRgb(orbHue, 0.9f, 0.7f + _smoothBeat * 0.3f));
            var coreGeo = CanvasGeometry.CreateEllipse(ds, cx, cy, orbRadius * 0.35f, orbRadius * 0.35f);
            ds.FillGeometry(coreGeo, Color.FromArgb(255, 255, 255, 255));
        }

        private float GetWaveformSample(int index)
        {
            if (_waveform == null || _waveformCount <= 0) return 0f;
            int idx = index % _waveformCount;
            return _waveform[idx];
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
            pipeline.FeedbackOpacity = 0.35f;
            pipeline.FeedbackZoom = 1.012f;
            pipeline.FeedbackOffsetY = -1.5f;
            pipeline.BloomAmount = 0.12f;
            pipeline.BloomBlur = 4f;
        }
    }
}
