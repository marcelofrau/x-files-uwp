using System;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Windows.Foundation;
using Windows.UI;

namespace XFiles.Visualizers.Visualizers
{
    /// <summary>
    /// Orbiting Circles visualizer: circles orbiting center point.
    /// Each circle radius = bandLevel. Orbital speed = beat.
    /// Color = HSL rotation per orbit. Trail = additive glow.
    /// Glow via GaussianBlur + Screen. MilkDrop-inspired: harmonic motion,
    /// multiple orbit rings, pulsing cores.
    /// </summary>
    public sealed class OrbitingCirclesVisualizer : IAudioVisualizer
    {
        public string Name => "Orbiting Circles";
        public string Id => "orbiting-circles";

        private CanvasDevice _device;
        private float _width, _height, _time;

        // Audio
        private readonly float[] _smoothBands = new float[AudioData.BandCount];
        private float _smoothBass, _smoothBeat;
        private const float AudioSmooth = 0.25f;

        // Orbit parameters
        private const int OrbitRings = 4;
        private const int CirclesPerRing = 8;

        public void Initialize(CanvasDevice device) { _device = device; }

        public void Update(AudioData data, TimeSpan elapsed)
        {
            _time = data.Time;

            float bass = 0;
            for (int i = 0; i < 6; i++) bass += data.BandLevels[i]; bass /= 6f;
            _smoothBass += (bass - _smoothBass) * AudioSmooth;
            _smoothBeat += (data.Beat - _smoothBeat) * 0.4f;

            for (int i = 0; i < AudioData.BandCount; i++)
                _smoothBands[i] += (data.BandLevels[i] - _smoothBands[i]) * AudioSmooth;
        }

        public void Draw(CanvasDrawingSession ds)
        {
            if (_device == null || _width == 0 || _height == 0) return;
            ds.Clear(Color.FromArgb(255, 2, 2, 5));
            DrawOrbits(ds);
        }

        public void Resize(float width, float height) { _width = width; _height = height; }

        public void Dispose() { _device = null; }

        private void DrawOrbits(CanvasDrawingSession ds)
        {
            float cx = _width * 0.5f, cy = _height * 0.5f;
            float minDim = Math.Min(_width, _height);

            // Draw orbit ring outlines (faint)
            for (int ring = 0; ring < OrbitRings; ring++)
            {
                float ringRadius = minDim * (0.12f + ring * 0.08f);
                float ringAlpha = 20 + ring * 5;
                var ringGeo = CanvasGeometry.CreateEllipse(ds, cx, cy, ringRadius, ringRadius);
                ds.DrawGeometry(ringGeo, Color.FromArgb((byte)ringAlpha, 80, 80, 120), 0.5f);
            }

            // Draw orbiting circles
            for (int ring = 0; ring < OrbitRings; ring++)
            {
                float ringRadius = minDim * (0.12f + ring * 0.08f);
                float speed = (0.3f + ring * 0.15f) * (1f + _smoothBeat * 0.5f);
                int bandOffset = ring * (AudioData.BandCount / OrbitRings);

                for (int c = 0; c < CirclesPerRing; c++)
                {
                    int bandIdx = (bandOffset + c) % AudioData.BandCount;
                    float bandLevel = _smoothBands[bandIdx];

                    // Orbital angle
                    float angle = _time * speed + (float)c / CirclesPerRing * 2f * (float)Math.PI;

                    // Position on orbit
                    float orbX = cx + (float)Math.Cos(angle) * ringRadius;
                    float orbY = cy + (float)Math.Sin(angle) * ringRadius;

                    // Circle radius = bandLevel
                    float circleRadius = 3f + bandLevel * minDim * 0.06f;

                    // Color: HSL per ring + band
                    float hue = ((float)ring / OrbitRings + (float)c / CirclesPerRing * 0.2f + _time * 0.04f) % 1.0f;
                    float sat = 0.75f + _smoothBeat * 0.25f;
                    float lum = 0.4f + bandLevel * 0.4f;

                    Color circleColor = HslToRgb(hue, sat, lum);

                    // Draw glow circle (larger, dimmer)
                    float glowRadius = circleRadius * 3.5f;
                    byte glowAlpha = (byte)Math.Min(255, (int)(60 + bandLevel * 80));
                    var glowGeo = CanvasGeometry.CreateEllipse(ds, orbX, orbY, glowRadius, glowRadius);
                    ds.FillGeometry(glowGeo, Color.FromArgb(glowAlpha, circleColor.R, circleColor.G, circleColor.B));

                    // Extra outer haze
                    float hazeRadius = circleRadius * 6f;
                    byte hazeAlpha = (byte)Math.Min(255, (int)(20 + bandLevel * 30));
                    var hazeGeo = CanvasGeometry.CreateEllipse(ds, orbX, orbY, hazeRadius, hazeRadius);
                    ds.FillGeometry(hazeGeo, Color.FromArgb(hazeAlpha, circleColor.R, circleColor.G, circleColor.B));

                    // Draw main circle
                    byte mainAlpha = (byte)Math.Min(255, (int)(180 + bandLevel * 75));
                    var mainGeo = CanvasGeometry.CreateEllipse(ds, orbX, orbY, circleRadius, circleRadius);
                    ds.FillGeometry(mainGeo, Color.FromArgb(mainAlpha, circleColor.R, circleColor.G, circleColor.B));

                    // Bright core for high-energy circles
                    if (bandLevel > 0.4f)
                    {
                        float coreRadius = circleRadius * 0.35f;
                        byte coreAlpha = (byte)Math.Min(255, (int)(200 * bandLevel));
                        var coreGeo = CanvasGeometry.CreateEllipse(ds, orbX, orbY, coreRadius, coreRadius);
                        ds.FillGeometry(coreGeo, Color.FromArgb(coreAlpha, 255, 255, 220));
                    }

                    // Lens flare streaks on bright circles
                    if (bandLevel > 0.5f)
                    {
                        float flareSz = circleRadius * 3f * bandLevel;
                        byte flareA = (byte)Math.Min(255, (int)(40 * bandLevel));
                        var fStyle = new CanvasStrokeStyle { StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round };
                        Color fColor = Color.FromArgb(flareA, circleColor.R, circleColor.G, circleColor.B);
                        ds.DrawLine(orbX - flareSz, orbY, orbX + flareSz, orbY, fColor, 1f, fStyle);
                        ds.DrawLine(orbX, orbY - flareSz, orbX, orbY + flareSz, fColor, 1f, fStyle);
                    }

                    // Connecting line to center (faint)
                    if (bandLevel > 0.2f)
                    {
                        byte lineAlpha = (byte)Math.Min(255, (int)(40 * bandLevel));
                        ds.DrawLine(cx, cy, orbX, orbY, Color.FromArgb(lineAlpha, circleColor.R, circleColor.G, circleColor.B), 0.5f);
                    }
                }
            }

            // Center glow orb — pulses with beat
            float centerRadius = 6f + _smoothBeat * 10f;
            float centerHue = (_time * 0.1f) % 1.0f;
            Color centerColor = HslToRgb(centerHue, 1f, 0.7f + _smoothBeat * 0.3f);

            var outerGlow2 = CanvasGeometry.CreateEllipse(ds, cx, cy, centerRadius * 5f, centerRadius * 5f);
            ds.FillGeometry(outerGlow2, Color.FromArgb(15, centerColor.R, centerColor.G, centerColor.B));

            var outerGlow = CanvasGeometry.CreateEllipse(ds, cx, cy, centerRadius * 3f, centerRadius * 3f);
            ds.FillGeometry(outerGlow, Color.FromArgb(30, centerColor.R, centerColor.G, centerColor.B));

            var centerGeo = CanvasGeometry.CreateEllipse(ds, cx, cy, centerRadius, centerRadius);
            ds.FillGeometry(centerGeo, centerColor);

            var centerCoreGeo = CanvasGeometry.CreateEllipse(ds, cx, cy, centerRadius * 0.3f, centerRadius * 0.3f);
            ds.FillGeometry(centerCoreGeo, Colors.White);

            // Center lens flare streaks
            float flareLen = centerRadius * 4f * (1f + _smoothBeat * 0.5f);
            byte flareAlpha = (byte)Math.Min(255, (int)(60 + _smoothBeat * 80));
            var flareStyle = new CanvasStrokeStyle { StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round };
            Color flareColor = Color.FromArgb(flareAlpha, centerColor.R, centerColor.G, centerColor.B);
            ds.DrawLine(cx - flareLen, cy, cx + flareLen, cy, flareColor, 1.5f, flareStyle);
            ds.DrawLine(cx, cy - flareLen, cx, cy + flareLen, flareColor, 1.5f, flareStyle);
            float flareLen2 = flareLen * 0.7f;
            ds.DrawLine(cx - flareLen2, cy - flareLen2, cx + flareLen2, cy + flareLen2, Color.FromArgb((byte)(flareAlpha / 2), centerColor.R, centerColor.G, centerColor.B), 1f, flareStyle);
            ds.DrawLine(cx + flareLen2, cy - flareLen2, cx - flareLen2, cy + flareLen2, Color.FromArgb((byte)(flareAlpha / 2), centerColor.R, centerColor.G, centerColor.B), 1f, flareStyle);
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
