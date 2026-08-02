using System;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Windows.UI;

namespace XFiles.Visualizers.Visualizers
{
    /// <summary>
    /// Vintage 1980s Hi-Fi LED VU meter.
    /// Matte LED/VFD segment aesthetic with physical grid look and no artificial neon glow.
    /// </summary>
    public sealed class ClassicVUMeterVisualizer : IAudioVisualizer
    {
        public string Name => "Classic VU Meter";
        public string Id => "classic-vumeter";

        private CanvasDevice _device;
        private CanvasTextFormat _labelFormat;
        private CanvasTextFormat _smallFormat;
        private float _width, _height, _time;

        private const int ColumnCount = 44;
        private const int SegmentCount = 14;
        private const float AudioSmooth = 0.30f;
        private const float Gravity = 1.8f;

        private readonly float[] _smoothBands = new float[ColumnCount];
        private readonly float[] _peakHeights = new float[ColumnCount];
        private readonly float[] _peakVelocities = new float[ColumnCount];

        public void Initialize(CanvasDevice device)
        {
            _device = device;
            _labelFormat = new CanvasTextFormat
            {
                FontSize = 14f,
                HorizontalAlignment = CanvasHorizontalAlignment.Center,
                VerticalAlignment = CanvasVerticalAlignment.Center,
                FontWeight = Windows.UI.Text.FontWeights.SemiBold
            };
            _smallFormat = new CanvasTextFormat
            {
                FontSize = 10f,
                HorizontalAlignment = CanvasHorizontalAlignment.Center,
                VerticalAlignment = CanvasVerticalAlignment.Center
            };
        }

        public void Update(AudioData data, TimeSpan elapsed)
        {
            if (data.BandLevels == null || data.BandLevels.Length == 0) return;
            _time = data.Time;
            float dt = (float)elapsed.TotalSeconds;

            for (int i = 0; i < ColumnCount; i++)
            {
                int srcIdx = (int)((float)i / ColumnCount * data.BandLevels.Length);
                float target = data.BandLevels[Math.Min(srcIdx, data.BandLevels.Length - 1)];
                _smoothBands[i] += (target - _smoothBands[i]) * AudioSmooth;

                if (_smoothBands[i] >= _peakHeights[i])
                {
                    _peakHeights[i] = _smoothBands[i];
                    _peakVelocities[i] = 0f; // Trava instantânea no topo
                }
                else
                {
                    // Queda por gravidade estilo mecânico/físico
                    _peakVelocities[i] += Gravity * dt;
                    _peakHeights[i] -= _peakVelocities[i] * dt;
                    if (_peakHeights[i] < 0f) _peakHeights[i] = 0f;
                }
            }
        }

        public void Draw(CanvasDrawingSession ds)
        {
            if (_device == null || _width == 0 || _height == 0) return;

            // Fundo escuro do chassi do aparelho (dark charcoal)
            ds.Clear(Color.FromArgb(255, 14, 15, 18));

            float panelX = _width * 0.02f;
            float panelW = _width * 0.96f;
            float panelY = _height * 0.02f;
            float panelH = _height * 0.96f;

            DrawPanel(ds, panelX, panelY, panelW, panelH);

            float bankGap = _height * 0.04f;
            float labelH = _height * 0.08f;
            float bankH = (panelH - bankGap - labelH * 2f) * 0.5f;
            if (bankH <= 10f) return;

            DrawBank(ds, panelX + 12f, panelY + labelH, panelW - 24f, bankH);
            DrawBank(ds, panelX + 12f, panelY + labelH + bankH + bankGap, panelW - 24f, bankH);
        }

        private void DrawPanel(CanvasDrawingSession ds, float x, float y, float w, float h)
        {
            // Moldura rebaixada do painel de vidro/acrílico
            ds.DrawRectangle(x, y, w, h, Color.FromArgb(255, 35, 38, 44), 2.0f);
            ds.FillRectangle(x + 2f, y + 2f, w - 4f, h - 4f, Color.FromArgb(255, 20, 22, 26));

            // Serigrafia vintage dos canais (cinza serigrafado matte)
            Color silkColor = Color.FromArgb(255, 130, 135, 145);
            float channelLabelY = y + _height * 0.045f;

            ds.DrawText("CH L", x + 16f, channelLabelY, silkColor, _labelFormat);
            ds.DrawText("CH R", x + 16f, channelLabelY + _height * 0.48f, silkColor, _labelFormat);

            ds.DrawText("dB", x + w - 24f, channelLabelY, silkColor, _smallFormat);
            ds.DrawText("dB", x + w - 24f, channelLabelY + _height * 0.48f, silkColor, _smallFormat);
        }

        private void DrawBank(CanvasDrawingSession ds, float bx, float by, float bw, float bh)
        {
            float gap = 2f;
            float colW = (bw - (ColumnCount - 1) * gap) / ColumnCount;
            float segGap = 2f;
            float segH = (bh - (SegmentCount - 1) * segGap) / SegmentCount;

            for (int i = 0; i < ColumnCount; i++)
            {
                float x = bx + i * (colW + gap);
                float level = Math.Clamp(_smoothBands[i], 0f, 1f);
                int litSegments = (int)(level * SegmentCount);

                float peak = Math.Clamp(_peakHeights[i], 0f, 1f);
                int peakSeg = (int)(peak * SegmentCount);
                if (peakSeg >= SegmentCount) peakSeg = SegmentCount - 1;

                for (int s = 0; s < SegmentCount; s++)
                {
                    float y = by + bh - (s * (segH + segGap)) - segH;
                    float ratio = (float)(s + 1) / SegmentCount;

                    bool isLit = s < litSegments;
                    bool isPeak = (s == peakSeg && peakSeg > 0);

                    // Cores físicas estilo LED/VFD
                    Color segColor = GetClassicLEDColor(ratio, isLit || isPeak);

                    ds.FillRectangle(x, y, colW, segH, segColor);
                }
            }
        }

        /// <summary>
        /// Paleta de cores sólida/matte baseada em displays VFD e LEDs retro.
        /// </summary>
        private static Color GetClassicLEDColor(float ratio, bool active)
        {
            if (active)
            {
                // Verde Hi-Fi clássico / Amarelo Âmbar / Vermelho Sobrio
                if (ratio < 0.60f) return Color.FromArgb(255, 40, 190, 90);   // Verde VFD
                if (ratio < 0.85f) return Color.FromArgb(255, 230, 170, 30);  // Amarelo/Laranja Vintage
                return Color.FromArgb(255, 220, 50, 45);                      // Vermelho dB Over
            }
            else
            {
                // Fundo do segmento desligado (silhueta opaca de plástico/vidro)
                if (ratio < 0.60f) return Color.FromArgb(255, 18, 38, 25);
                if (ratio < 0.85f) return Color.FromArgb(255, 40, 32, 15);
                return Color.FromArgb(255, 40, 18, 18);
            }
        }

        public void Resize(float width, float height)
        {
            _width = width;
            _height = height;
        }

        public void Dispose()
        {
            _labelFormat?.Dispose();
            _labelFormat = null;
            _smallFormat?.Dispose();
            _smallFormat = null;
            _device = null;
        }

        public void ConfigurePipeline(PostProcessPipeline pipeline)
        {
            // Sem Bloom para manter o aspecto focado, opaco e direto da serigrafia de hardware antigo
            pipeline.BloomAmount = 0.0f;
            pipeline.BloomBlur = 0.0f;
            pipeline.BloomThreshold = 1.0f;
        }
    }
}