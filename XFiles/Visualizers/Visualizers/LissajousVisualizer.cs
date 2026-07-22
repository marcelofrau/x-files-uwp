using System;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Windows.Foundation;
using Windows.UI;

namespace XFiles.Visualizers.Visualizers
{
    /// <summary>
    /// Lissajous visualizer: parametric curves where frequency ratios are driven by band levels.
    /// Color shifts by time. Beat causes curve expansion/thickness pulse.
    /// Glow via GaussianBlur + Screen. MilkDrop-inspired: multiple overlapping curves,
    /// additive blending, organic flowing motion.
    /// </summary>
    public sealed class LissajousVisualizer : IAudioVisualizer
    {
        public string Name => "Lissajous";
        public string Id => "lissajous";

        private CanvasDevice _device;
        private float _width, _height, _time;

        // Smoothed band groups
        private float _smoothBass, _smoothMid, _smoothTreble, _smoothBeat;
        private const float AudioSmooth = 0.25f;

        // Curve parameters
        private const int CurvePoints = 512;
        private const int CurveLayers = 5;

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
            _smoothBeat += (data.Beat - _smoothBeat) * 0.4f;
        }

        public void Draw(CanvasDrawingSession ds)
        {
            if (_device == null || _width == 0 || _height == 0) return;
            ds.Clear(Color.FromArgb(255, 2, 2, 5));
            DrawLissajous(ds);
        }

        public void Resize(float width, float height) { _width = width; _height = height; }

        public void Dispose() { _device = null; }

        private void DrawLissajous(CanvasDrawingSession ds)
        {
            float cx = _width * 0.5f, cy = _height * 0.5f;
            float minDim = Math.Min(_width, _height);
            float baseRadius = minDim * 0.32f;

            var strokeStyle = new CanvasStrokeStyle
            {
                StartCap = CanvasCapStyle.Round,
                EndCap = CanvasCapStyle.Round
            };

            for (int layer = 0; layer < CurveLayers; layer++)
            {
                float layerT = (float)layer / CurveLayers;

                float freqX = 2f + _smoothBass * 3f + layer * 0.5f;
                float freqY = 3f + _smoothMid * 2f + layer * 0.3f;
                float phaseShift = _time * 0.3f + layer * 0.7f;
                float radius = baseRadius * (0.4f + layerT * 0.6f) * (1f + _smoothBeat * 0.2f);

                float hue = (layerT * 0.3f + _time * 0.05f) % 1.0f;
                float sat = 0.8f + _smoothBeat * 0.2f;
                float lum = 0.35f + layerT * 0.2f + _smoothBeat * 0.1f;
                Color curveColor = HslToRgb(hue, sat, lum);

                float thickness = 1.5f + (1f - layerT) * 3f + _smoothBeat * 2f;
                byte alpha = (byte)Math.Min(255, (int)(200 * (1f - layerT * 0.5f)));

                using (var builder = new CanvasPathBuilder(ds))
                {
                    bool first = true;
                    for (int i = 0; i <= CurvePoints; i++)
                    {
                        float t = (float)i / CurvePoints * 2f * (float)Math.PI;
                        float x = (float)Math.Sin(freqX * t + phaseShift);
                        float y = (float)Math.Sin(freqY * t);
                        float audioMod = 1f + _smoothBass * 0.3f * (float)Math.Sin(t * 3f + _time);
                        x *= radius * audioMod;
                        y *= radius * audioMod;
                        float screenX = cx + x, screenY = cy + y;

                        if (first)
                        {
                            builder.BeginFigure(screenX, screenY);
                            first = false;
                        }
                        else
                        {
                            builder.AddLine(screenX, screenY);
                        }
                    }
                    builder.EndFigure(CanvasFigureLoop.Open);

                    var path = CanvasGeometry.CreatePath(builder);
                    ds.DrawGeometry(path, Color.FromArgb(alpha, curveColor.R, curveColor.G, curveColor.B), thickness, strokeStyle);
                }
            }

            float dotRadius = 4f + _smoothBeat * 6f;
            float dotHue = (_time * 0.15f) % 1.0f;
            ds.FillGeometry(CanvasGeometry.CreateCircle(ds, cx, cy, dotRadius), HslToRgb(dotHue, 1f, 0.8f));
            ds.FillGeometry(CanvasGeometry.CreateCircle(ds, cx, cy, dotRadius * 0.3f), Colors.White);
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
