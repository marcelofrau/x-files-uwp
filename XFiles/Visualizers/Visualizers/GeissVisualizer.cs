using System;
using Microsoft.Graphics.Canvas;
using Windows.UI;

namespace XFiles.Visualizers.Visualizers
{
    public sealed class GeissVisualizer : IAudioVisualizer
    {
        public string Name => "Geiss Liquid Fluid";
        public string Id => "geiss-fluid";

        private CanvasDevice _device;
        private float _width, _height, _time;
        private float _zScroll;

        private const int Rings = 12;
        private const int Angles = 80;
        private const float MaxZ = 8f;
        private const float FocalLen = 2.5f;
        private const float NearPlane = 0.5f;
        private const float BaseRadius = 1.8f;
        private const float RingStep = 1.2f;
        private const float ScrollSpeed = 0.025f;

        private const int GridSize = Rings * Angles;
        private readonly float[] _gx = new float[GridSize];
        private readonly float[] _gy = new float[GridSize];
        private readonly byte[] _ga = new byte[GridSize];
        private readonly byte[] _gr = new byte[GridSize];
        private readonly byte[] _gg = new byte[GridSize];
        private readonly byte[] _gb = new byte[GridSize];

        private readonly float[] _smoothBands = new float[AudioData.BandCount];
        private float _smoothBass, _smoothBeat, _smoothTreble;
        private const float AudioSmooth = 0.15f;

        public void Initialize(CanvasDevice device) { _device = device; }

        public void Update(AudioData data, TimeSpan elapsed)
        {
            if (data.BandLevels == null || data.BandLevels.Length == 0) return;
            _time = data.Time;
            _zScroll += ScrollSpeed;
            if (_zScroll > MaxZ) _zScroll -= MaxZ;

            float bass = 0f, treble = 0f;
            int halfBands = Math.Min(6, data.BandLevels.Length);
            for (int i = 0; i < halfBands; i++) bass += data.BandLevels[i];
            bass /= halfBands;
            int trebleBands = Math.Max(1, data.BandLevels.Length - halfBands);
            for (int i = halfBands; i < data.BandLevels.Length; i++) treble += data.BandLevels[i];
            treble /= trebleBands;

            _smoothBass += (bass - _smoothBass) * AudioSmooth;
            _smoothTreble += (treble - _smoothTreble) * AudioSmooth;
            _smoothBeat += (data.Beat - _smoothBeat) * 0.3f;
            for (int i = 0; i < Math.Min(AudioData.BandCount, data.BandLevels.Length); i++)
                _smoothBands[i] += (data.BandLevels[i] - _smoothBands[i]) * AudioSmooth;
        }

        public void Draw(CanvasDrawingSession ds)
        {
            if (_device == null || _width == 0 || _height == 0) return;

            float cx = _width * 0.5f;
            float cy = _height * 0.5f;
            float scale = Math.Min(_width, _height) * 0.22f;

            float bassPull = 1f + _smoothBass * 0.6f;
            float trebleShimmer = _smoothTreble * 0.5f;

            float hueOffset = (_time * 0.06f) % 1f;

            int idx = 0;
            for (int ring = 0; ring < Rings; ring++)
            {
                float rawZ = (ring / (float)Rings) * MaxZ;
                float z = (rawZ + _zScroll) % MaxZ;
                float zNorm = z / MaxZ;
                float invZ = 1f - zNorm;
                float persp = FocalLen / (z + NearPlane);
                float ringR = (BaseRadius + ring * RingStep * 0.12f) * bassPull * persp * scale;

                for (int a = 0; a < Angles; a++)
                {
                    float angle = (a / (float)Angles) * MathF.PI * 2f;
                    int bandIdx = (int)((float)a / Angles * AudioData.BandCount) % AudioData.BandCount;
                    float audioDeform = _smoothBands[bandIdx] * 0.35f;

                    float mountain = MathF.Sin(angle * 4f + z * 5f + _time * 0.5f) * 0.15f
                        + MathF.Cos(angle * 7f - z * 3f) * 0.10f
                        + MathF.Sin(angle * 12f + z * 8f + _time) * 0.06f;

                    float r = ringR * (1f + mountain + audioDeform + trebleShimmer * 0.1f);
                    float hue = (hueOffset + zNorm * 0.3f + angle * 0.08f) % 1f;
                    float lightness = 0.3f + 0.5f * invZ + 0.15f * _smoothBeat;
                    byte alpha = (byte)(180 * invZ + 40);

                    Color c = HslToRgb(hue, 0.85f, lightness);

                    _gx[idx] = cx + r * MathF.Cos(angle);
                    _gy[idx] = cy + r * MathF.Sin(angle);
                    _ga[idx] = alpha;
                    _gr[idx] = c.R;
                    _gg[idx] = c.G;
                    _gb[idx] = c.B;
                    idx++;
                }
            }

            int thickness = (int)(1.5f + _smoothBass * 2f);
            for (int ring = 0; ring < Rings; ring++)
            {
                for (int a = 0; a < Angles; a++)
                {
                    int cur = ring * Angles + a;
                    int next = ring * Angles + (a + 1) % Angles;

                    byte avgA = (byte)((_ga[cur] + _ga[next]) / 2);
                    Color lineColor = Color.FromArgb(avgA, (byte)((_gr[cur] + _gr[next]) / 2),
                        (byte)((_gg[cur] + _gg[next]) / 2), (byte)((_gb[cur] + _gb[next]) / 2));
                    ds.DrawLine(_gx[cur], _gy[cur], _gx[next], _gy[next], lineColor, thickness);

                    if (ring > 0)
                    {
                        int prevCur = (ring - 1) * Angles + a;
                        Color vertColor = Color.FromArgb(_ga[cur], _gr[cur], _gg[cur], _gb[cur]);
                        ds.DrawLine(_gx[cur], _gy[cur], _gx[prevCur], _gy[prevCur], vertColor, thickness);
                    }
                }
            }

            if (_smoothTreble > 0.15f)
            {
                int sparkCount = (int)(_smoothTreble * 30);
                for (int i = 0; i < sparkCount; i++)
                {
                    float t = (float)i / Math.Max(1, sparkCount);
                    float sparkZ = t * MaxZ;
                    float perspS = FocalLen / (sparkZ + NearPlane);
                    float sparkR = 0.05f * perspS * scale;
                    float sparkAngle = (_time * 3f + i * 2.1f + sparkZ) % (MathF.PI * 2f);

                    float bandS = _smoothBands[i % AudioData.BandCount];
                    float dist = (BaseRadius * 1.2f + bandS * 0.3f) * perspS * scale;

                    float sx = cx + dist * MathF.Cos(sparkAngle);
                    float sy = cy + dist * MathF.Sin(sparkAngle);
                    float invZS = 1f - sparkZ / MaxZ;
                    Color sparkColor = HslToRgb((hueOffset + t * 0.5f) % 1f, 1f, 0.7f + 0.3f * _smoothBeat);
                    ds.FillCircle(sx, sy, sparkR + _smoothTreble * 1.5f, sparkColor);
                }
            }
        }

        public void Resize(float width, float height) { _width = width; _height = height; }
        public void Dispose() { _device = null; }

        private static Color HslToRgb(float h, float s, float l)
        {
            h -= MathF.Floor(h); float hue = h * 360f;
            float c = (1f - MathF.Abs(2f * l - 1f)) * s;
            float x = c * (1f - MathF.Abs((hue / 60f) % 2f - 1f));
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
            pipeline.FeedbackOpacity = 0.88f;
            pipeline.FeedbackZoom = 1.018f;
            pipeline.FeedbackDecay = 0.018f;
            pipeline.BloomAmount = 0.4f;
            pipeline.BloomBlur = 3f;
            pipeline.BloomThreshold = 0.1f;
        }
    }
}
