using System;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Windows.Foundation;
using Windows.UI;

namespace XFiles.Visualizers.Visualizers
{
    public sealed class ComancheVisualizer : IAudioVisualizer
    {
        public string Name => "Comanche Terrain";
        public string Id => "comanche-terrain";

        private static readonly Color SkyTop = Color.FromArgb(255, 28, 22, 42);
        private static readonly Color SkyHorizon = Color.FromArgb(255, 255, 150, 118);
        private static readonly Color ColorValley = Color.FromArgb(255, 38, 66, 40);
        private static readonly Color ColorLowland = Color.FromArgb(255, 92, 108, 58);
        private static readonly Color ColorHighland = Color.FromArgb(255, 150, 120, 78);
        private static readonly Color ColorRock = Color.FromArgb(255, 108, 90, 82);
        private static readonly Color ColorSnow = Color.FromArgb(255, 235, 235, 244);

        private const int ColumnCount = 168;
        private const int MinColumnCount = 72;
        private const int MaxStepsPerColumn = 60;
        private const float NearZ = 1.2f;
        private const float MaxDistance = 260f;
        private const float StepGrowth = 1.045f;
        private const float BaseStep = 1.4f;
        private const float MaxHeightScale = 46f;
        private const float HoverOffset = 24f;
        private const float NearBoostRadius = 40f;
        private const float SpikeScale = 20f;
        private const float FovDeg = 75f;
        private const float NoiseScale = 0.045f;

        private CanvasDevice _device;
        private float _width, _height, _time;

        private float _camX, _camZ, _camAngle, _camHeight;
        private float _smoothBass, _smoothBeat;
        private float[] _smoothBands;

        private int _activeColumnCount = ColumnCount;
        private float _smoothFrameSeconds = 1f / 60f;
        private float _qualityCooldown;
        private const float HighLoadThreshold = 0.020f;
        private const float LowLoadThreshold = 0.014f;
        private const float QualityCooldownSeconds = 0.5f;

        public void Initialize(CanvasDevice device) { _device = device; }

        public void Update(AudioData data, TimeSpan elapsed)
        {
            _time = data.Time;
            float dt = (float)elapsed.TotalSeconds;

            _smoothBeat += (data.Beat - _smoothBeat) * 0.35f;
            int bassBands = Math.Min(6, data.BandLevels.Length);
            float bass = 0f;
            for (int i = 0; i < bassBands; i++) bass += data.BandLevels[i];
            bass /= Math.Max(1, bassBands);
            _smoothBass += (bass - _smoothBass) * 0.2f;

            if (_smoothBands == null || _smoothBands.Length != data.BandLevels.Length)
                _smoothBands = new float[data.BandLevels.Length];
            for (int i = 0; i < data.BandLevels.Length; i++)
                _smoothBands[i] += (data.BandLevels[i] - _smoothBands[i]) * 0.25f;

            float forwardSpeed = 9f + _smoothBass * 15f + _smoothBeat * 9f;
            _camAngle += MathF.Sin(_time * 0.09f) * 0.28f * dt + _smoothBeat * 0.15f * dt;
            _camX += MathF.Cos(_camAngle) * forwardSpeed * dt;
            _camZ += MathF.Sin(_camAngle) * forwardSpeed * dt;

            float groundHeight = FractalHeight(_camX, _camZ);
            float desiredHeight = groundHeight + HoverOffset + _smoothBass * 6f;
            float followRate = 1f - MathF.Exp(-2.4f * dt);
            _camHeight += (desiredHeight - _camHeight) * followRate;

            UpdateAdaptiveQuality(elapsed);
        }

        private void UpdateAdaptiveQuality(TimeSpan elapsed)
        {
            float frameSeconds = (float)elapsed.TotalSeconds;
            _smoothFrameSeconds += (frameSeconds - _smoothFrameSeconds) * 0.15f;

            _qualityCooldown -= frameSeconds;
            if (_qualityCooldown > 0f) return;

            if (_smoothFrameSeconds > HighLoadThreshold && _activeColumnCount > MinColumnCount)
            {
                _activeColumnCount = Math.Max(MinColumnCount, _activeColumnCount - 16);
                _qualityCooldown = QualityCooldownSeconds;
            }
            else if (_smoothFrameSeconds < LowLoadThreshold && _activeColumnCount < ColumnCount)
            {
                _activeColumnCount = Math.Min(ColumnCount, _activeColumnCount + 8);
                _qualityCooldown = QualityCooldownSeconds;
            }
        }

        public void Draw(CanvasDrawingSession ds)
        {
            if (_device == null || _width == 0 || _height == 0) return;
            ds.Clear(SkyTop);
            DrawSky(ds);
            DrawSunGlow(ds);
            DrawTerrain(ds);
        }

        public void Resize(float width, float height) { _width = width; _height = height; }
        public void Dispose() { _device = null; }

        private void DrawSky(CanvasDrawingSession ds)
        {
            float horizonY = _height * 0.55f;
            var brush = new CanvasLinearGradientBrush(ds, SkyTop, SkyHorizon)
            {
                StartPoint = new Vector2(0, 0),
                EndPoint = new Vector2(0, horizonY)
            };
            ds.FillRectangle(0, 0, _width, horizonY, brush);
            ds.FillRectangle(0, horizonY, _width, _height - horizonY, SkyHorizon);
            brush.Dispose();
        }

        private void DrawSunGlow(CanvasDrawingSession ds)
        {
            float cx = _width * 0.5f;
            float cy = _height * 0.5f;
            float r = _height * 0.09f * (1f + _smoothBeat * 0.25f);
            ds.FillEllipse(cx, cy, r * 1.8f, r * 1.8f, Color.FromArgb(40, 255, 200, 140));
            ds.FillEllipse(cx, cy, r, r, Color.FromArgb(220, 255, 225, 180));
        }

        private void DrawTerrain(CanvasDrawingSession ds)
        {
            float horizonY = _height * 0.55f;
            float colWidth = _width / _activeColumnCount;
            float fovRad = FovDeg * MathF.PI / 180f;
            float scaleY = _height * 1.0f;
            float clipTop = horizonY - _height * 0.42f;

            for (int c = 0; c < _activeColumnCount; c++)
            {
                float t = (c + 0.5f) / _activeColumnCount - 0.5f;
                float rayAngle = _camAngle + t * fovRad;
                float cosA = MathF.Cos(rayAngle), sinA = MathF.Sin(rayAngle);

                float bandEnergy = 0f;
                if (_smoothBands != null && _smoothBands.Length > 0)
                {
                    int bandIdx = (int)((float)c / _activeColumnCount * _smoothBands.Length);
                    bandEnergy = _smoothBands[Math.Min(bandIdx, _smoothBands.Length - 1)];
                }

                float currentTop = _height;
                float distance = NearZ;
                float step = BaseStep;
                float x0 = c * colWidth;

                for (int s = 0; s < MaxStepsPerColumn && distance < MaxDistance; s++)
                {
                    float sx = _camX + cosA * distance;
                    float sz = _camZ + sinA * distance;
                    float h = FractalHeight(sx, sz);

                    if (distance < NearBoostRadius)
                        h += bandEnergy * SpikeScale * (1f - distance / NearBoostRadius);

                    float screenY = horizonY + (_camHeight - h) / distance * scaleY;
                    if (screenY < clipTop) screenY = clipTop;

                    if (screenY < currentTop)
                    {
                        float fogT = Math.Clamp(distance / MaxDistance, 0f, 1f);
                        fogT *= fogT;
                        Color terrainCol = GetTerrainColor(h);
                        Color finalCol = LerpColor(terrainCol, SkyHorizon, fogT);

                        ds.FillRectangle(x0, screenY, colWidth + 0.6f, currentTop - screenY, finalCol);
                        currentTop = screenY;

                        if (currentTop <= clipTop) break;
                    }

                    distance += step;
                    step *= StepGrowth;
                }
            }
        }

        private static Color GetTerrainColor(float h)
        {
            float n = Math.Clamp(h / MaxHeightScale, 0f, 1.5f);
            if (n < 0.18f) return LerpColor(ColorValley, ColorLowland, n / 0.18f);
            if (n < 0.5f) return LerpColor(ColorLowland, ColorHighland, (n - 0.18f) / 0.32f);
            if (n < 0.85f) return LerpColor(ColorHighland, ColorRock, (n - 0.5f) / 0.35f);
            return LerpColor(ColorRock, ColorSnow, Math.Clamp((n - 0.85f) / 0.3f, 0f, 1f));
        }

        private static Color LerpColor(Color a, Color b, float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            return Color.FromArgb(
                (byte)(a.A + (b.A - a.A) * t),
                (byte)(a.R + (b.R - a.R) * t),
                (byte)(a.G + (b.G - a.G) * t),
                (byte)(a.B + (b.B - a.B) * t));
        }

        private float FractalHeight(float x, float z)
        {
            float amp = 1f, freq = NoiseScale, sum = 0f, acc = 0f;
            for (int oct = 0; oct < 3; oct++)
            {
                acc += ValueNoise(x * freq, z * freq) * amp;
                sum += amp;
                amp *= 0.5f;
                freq *= 2f;
            }
            float n = acc / sum;
            float ridge = 1f - MathF.Abs(n * 2f - 1f);
            float shaped = MathF.Pow(Math.Clamp(ridge, 0f, 1f), 1.6f);
            return shaped * MaxHeightScale * (0.65f + _smoothBass * 0.45f);
        }

        private static float ValueNoise(float x, float z)
        {
            int xi = (int)MathF.Floor(x);
            int zi = (int)MathF.Floor(z);
            float xf = x - xi;
            float zf = z - zi;

            float v00 = Hash(xi, zi);
            float v10 = Hash(xi + 1, zi);
            float v01 = Hash(xi, zi + 1);
            float v11 = Hash(xi + 1, zi + 1);

            float u = xf * xf * (3f - 2f * xf);
            float v = zf * zf * (3f - 2f * zf);

            float top = v00 + (v10 - v00) * u;
            float bottom = v01 + (v11 - v01) * u;
            return top + (bottom - top) * v;
        }

        private static float Hash(int x, int z)
        {
            unchecked
            {
                int n = x * 374761393 + z * 668265263;
                n = (n ^ (n >> 13)) * 1274126177;
                n ^= n >> 16;
                return (n & 0x7fffffff) / (float)0x7fffffff;
            }
        }

        public void ConfigurePipeline(PostProcessPipeline pipeline)
        {
            pipeline.FeedbackOpacity = 0.08f;
            pipeline.FeedbackZoom = 1.0004f;
            pipeline.BloomAmount = 0.14f;
            pipeline.BloomBlur = 4.5f;
            pipeline.BloomThreshold = 0.55f;
            pipeline.VignetteEnabled = true;
        }
    }
}
