using System;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Windows.Foundation;
using Windows.UI;

namespace XFiles.Visualizers.Visualizers
{
    public sealed class StarfieldVisualizer : IAudioVisualizer
    {
        public string Name => "Starfield";
        public string Id => "starfield";

        private CanvasDevice _device;
        private float _width, _height, _time;

        private const int StarCount = 600;
        private float[] _starX, _starY, _starZ, _starSpeed, _starBrightness;
        private float _smoothBass, _smoothMid, _smoothTreble, _smoothBeat, _smoothAvgFreq;
        private const float AudioSmooth = 0.2f;
        private float _beatPulse;

        // Waveform
        private float[] _waveform;
        private int _waveformCount;

        // Planets
        private const int PlanetCount = 4;
        private float[] _planetX, _planetZ, _planetHue, _planetRadius;
        private bool[] _planetHasRing;

        public void Initialize(CanvasDevice device)
        {
            _device = device;
            InitStars();
            InitPlanets();
        }

        public void Update(AudioData data, TimeSpan elapsed)
        {
            float dt = (float)elapsed.TotalSeconds;
            _time = data.Time;

            float bass = 0, mid = 0, treble = 0;
            for (int i = 0; i < 6; i++) bass += data.BandLevels[i]; bass /= 6f;
            for (int i = 10; i < 16; i++) mid += data.BandLevels[i]; mid /= 6f;
            for (int i = 20; i < 26; i++) treble += data.BandLevels[i]; treble /= 26f;
            _smoothBass += (bass - _smoothBass) * AudioSmooth;
            _smoothMid += (mid - _smoothMid) * AudioSmooth;
            _smoothTreble += (treble - _smoothTreble) * AudioSmooth;
            _smoothBeat += (data.Beat - _smoothBeat) * 0.4f;
            float avgFreq = (bass + mid + treble) / 3f;
            _smoothAvgFreq += (avgFreq - _smoothAvgFreq) * AudioSmooth;

            // Beat pulse: fast attack, slow decay
            _beatPulse = Math.Max(_beatPulse * 0.92f, data.Beat);

            // Waveform
            _waveform = data.Waveform;
            _waveformCount = data.WaveformCount;

            // Stars: slow constant forward movement + bass modulation
            float speed = 0.012f + _smoothBass * 0.06f;
            for (int i = 0; i < StarCount; i++)
            {
                _starZ[i] -= speed * _starSpeed[i];
                if (_starZ[i] < 0.01f) ResetStar(i);
            }

            // Planets: drift toward viewer, bass-modulated
            for (int i = 0; i < PlanetCount; i++)
            {
                _planetZ[i] -= (0.01f + _smoothBass * 0.03f) * dt;
                if (_planetZ[i] < 0.01f) ResetPlanet(i);
            }
        }

        public void Draw(CanvasDrawingSession ds)
        {
            if (_device == null || _width == 0 || _height == 0) return;
            ds.Clear(Color.FromArgb(255, 2, 2, 5));
            DrawStars(ds);
            DrawPlanets(ds);
            DrawCenterWaveform(ds);
        }

        public void Resize(float width, float height) { _width = width; _height = height; InitStars(); }

        public void Dispose() { _device = null; }

        private void InitStars()
        {
            _starX = new float[StarCount]; _starY = new float[StarCount];
            _starZ = new float[StarCount]; _starSpeed = new float[StarCount];
            _starBrightness = new float[StarCount];
            var rng = new Random(42);
            for (int i = 0; i < StarCount; i++) ResetStar(i, rng);
        }

        private void ResetStar(int i, Random rng = null)
        {
            rng = rng ?? new Random();
            _starX[i] = (float)(rng.NextDouble() * 2.0 - 1.0);
            _starY[i] = (float)(rng.NextDouble() * 2.0 - 1.0);
            _starZ[i] = (float)(rng.NextDouble() * 1.0 + 0.1);
            _starSpeed[i] = 0.5f + (float)rng.NextDouble() * 1.5f;
            _starBrightness[i] = 0.3f + (float)rng.NextDouble() * 0.7f;
        }

        private void InitPlanets()
        {
            _planetX = new float[PlanetCount];
            _planetZ = new float[PlanetCount];
            _planetHue = new float[PlanetCount];
            _planetRadius = new float[PlanetCount];
            _planetHasRing = new bool[PlanetCount];
            var rng = new Random(137);
            for (int i = 0; i < PlanetCount; i++)
            {
                _planetX[i] = (float)(rng.NextDouble() * 1.6 - 0.8);
                _planetZ[i] = (float)(rng.NextDouble() * 0.8 + 0.3);
                _planetHue[i] = (float)rng.NextDouble();
                _planetRadius[i] = (float)(rng.NextDouble() * 20 + 15);
                _planetHasRing[i] = i < 2; // first 2 planets have rings
            }
        }

        private void ResetPlanet(int i)
        {
            var rng = new Random();
            _planetX[i] = (float)(rng.NextDouble() * 1.6 - 0.8);
            _planetZ[i] = (float)(rng.NextDouble() * 0.5 + 0.8);
            _planetHue[i] = (float)rng.NextDouble();
            _planetRadius[i] = (float)(rng.NextDouble() * 20 + 15);
            _planetHasRing[i] = rng.Next(3) == 0; // ~33% chance
        }

        private void DrawStars(CanvasDrawingSession ds)
        {
            float cx = _width * 0.5f, cy = _height * 0.5f, fov = _width * 0.8f;
            float hue = 0.55f - _smoothAvgFreq * 0.15f;
            float sat = 0.6f + _smoothBeat * 0.4f, lum = 0.7f + _smoothBeat * 0.3f;
            var strokeStyle = new CanvasStrokeStyle { StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round };

            for (int i = 0; i < StarCount; i++)
            {
                float z = _starZ[i];
                if (z <= 0) continue;
                float screenX = cx + (_starX[i] / z) * fov;
                float screenY = cy + (_starY[i] / z) * fov;
                if (screenX < -50 || screenX > _width + 50 || screenY < -50 || screenY > _height + 50) continue;
                float depthFade = Math.Max(0, 1f - z);
                float size = (1f / z) * 1.5f;
                size *= (1f + _beatPulse * 0.4f);
                float alpha = depthFade * _starBrightness[i];
                if (alpha < 0.01f) continue;
                float starHue = hue + (i % 7) * 0.02f;
                Color starColor = HslToRgb(starHue - (float)Math.Floor(starHue), sat, lum);
                byte r = (byte)(starColor.R * alpha), g = (byte)(starColor.G * alpha), b = (byte)(starColor.B * alpha);
                byte a = (byte)Math.Min(255, (int)(255 * alpha));

                // Motion blur streak — line from previous position toward screen center
                if (z < 0.6f && size > 1.5f)
                {
                    float streakLen = (1f - z) * 30f * (1f + _smoothBass * 2f);
                    float dx = screenX - cx, dy = screenY - cy;
                    float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (dist > 1f)
                    {
                        float nx = dx / dist, ny = dy / dist;
                        float tailX = screenX + nx * streakLen;
                        float tailY = screenY + ny * streakLen;
                        byte streakA = (byte)Math.Min(255, (int)(a * 0.35f));
                        ds.DrawLine(tailX, tailY, screenX, screenY,
                            Color.FromArgb(streakA, starColor.R, starColor.G, starColor.B),
                            Math.Max(1f, size * 0.6f), strokeStyle);
                    }
                }

                // Star glow halo (glare)
                if (z < 0.4f && size > 2f)
                {
                    float glowSize = size * 3.5f;
                    byte glowA = (byte)Math.Min(255, (int)(alpha * 60));
                    var glowGeo = CanvasGeometry.CreateCircle(ds, screenX, screenY, glowSize);
                    ds.FillGeometry(glowGeo, Color.FromArgb(glowA, starColor.R, starColor.G, starColor.B));
                }

                var starGeo = CanvasGeometry.CreateCircle(ds, screenX, screenY, size);
                ds.FillGeometry(starGeo, Color.FromArgb(a, r, g, b));
                if (z < 0.3f && size > 2f)
                {
                    var coreGeo = CanvasGeometry.CreateCircle(ds, screenX, screenY, size * 0.4f);
                    ds.FillGeometry(coreGeo, Color.FromArgb((byte)Math.Min(255, a + 40), 255, 255, 255));
                }
            }
        }

        private void DrawPlanets(CanvasDrawingSession ds)
        {
            float cx = _width * 0.5f, cy = _height * 0.5f, fov = _width * 0.8f;
            var strokeStyle = new CanvasStrokeStyle { StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round };

            for (int i = 0; i < PlanetCount; i++)
            {
                float z = _planetZ[i];
                if (z <= 0.01f) continue;
                float screenX = cx + (_planetX[i] / z) * fov;
                float size = _planetRadius[i] / z;
                if (screenX < -size * 3 || screenX > _width + size * 3) continue;

                Color planetColor = HslToRgb(_planetHue[i], 0.5f, 0.45f);
                Color glowColor = HslToRgb(_planetHue[i], 0.4f, 0.3f);

                // Atmospheric glow (larger, semi-transparent)
                byte glowA = (byte)Math.Min(255, (int)(60 / z));
                var glowGeo = CanvasGeometry.CreateCircle(ds, screenX, cy, size * 1.4f);
                ds.FillGeometry(glowGeo, Color.FromArgb(glowA, glowColor.R, glowColor.G, glowColor.B));

                // Planet body
                byte planetA = (byte)Math.Min(255, (int)(220 / z));
                var planetGeo = CanvasGeometry.CreateCircle(ds, screenX, cy, size);
                ds.FillGeometry(planetGeo, Color.FromArgb(planetA, planetColor.R, planetColor.G, planetColor.B));

                // Ring (stroked ellipse)
                if (_planetHasRing[i])
                {
                    float ringRX = size * 2.0f;
                    float ringRY = size * 0.5f;
                    float ringThickness = Math.Max(1.5f, size * 0.15f);
                    byte ringA = (byte)Math.Min(255, (int)(140 / z));
                    Color ringColor = HslToRgb(_planetHue[i] + 0.05f, 0.3f, 0.55f);
                    var ringGeo = CanvasGeometry.CreateEllipse(ds, screenX, cy, ringRX, ringRY);
                    ds.DrawGeometry(ringGeo, Color.FromArgb(ringA, ringColor.R, ringColor.G, ringColor.B), ringThickness, strokeStyle);
                }
            }
        }

        private void DrawCenterWaveform(CanvasDrawingSession ds)
        {
            if (_waveform == null || _waveformCount == 0) return;

            float cx = _width * 0.5f, cy = _height * 0.5f;
            float minDim = Math.Min(_width, _height);
            float baseRadius = minDim * 0.12f;
            float amplitude = minDim * 0.08f;

            var strokeStyle = new CanvasStrokeStyle { StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round };

            // Cyan/white color
            Color glowColor = Color.FromArgb(40, 100, 220, 255);
            Color sharpColor = Color.FromArgb(200, 180, 240, 255);

            float twoPi = (float)(Math.PI * 2.0);

            // Glow pass (thickness 5, alpha 40)
            float prevX = cx + baseRadius;
            float prevY = cy;
            for (int i = 1; i <= _waveformCount; i++)
            {
                float angle = (float)i / _waveformCount * twoPi;
                int idx = i % _waveformCount;
                float sampleR = baseRadius + _waveform[idx] * amplitude;
                float x = cx + (float)Math.Cos(angle) * sampleR;
                float y = cy + (float)Math.Sin(angle) * sampleR;
                ds.DrawLine(prevX, prevY, x, y, glowColor, 5f, strokeStyle);
                prevX = x; prevY = y;
            }

            // Sharp pass (thickness 2, alpha 200)
            prevX = cx + baseRadius;
            prevY = cy;
            for (int i = 1; i <= _waveformCount; i++)
            {
                float angle = (float)i / _waveformCount * twoPi;
                int idx = i % _waveformCount;
                float sampleR = baseRadius + _waveform[idx] * amplitude;
                float x = cx + (float)Math.Cos(angle) * sampleR;
                float y = cy + (float)Math.Sin(angle) * sampleR;
                ds.DrawLine(prevX, prevY, x, y, sharpColor, 2f, strokeStyle);
                prevX = x; prevY = y;
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
