using System;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;
using XFiles.Audio;

namespace XFiles.Controls
{
    public sealed partial class VuMeterBar : UserControl
    {
        private const int BarCount = 26;
        private const int SegmentsPerBar = 12;
        private const double SegmentHeight = 5.0;
        private const double SegmentGap = 1.0;
        private const double PeakHeight = 2.0;
        private const double BarGap = 3.0;

        private readonly Rectangle[][] _segments = new Rectangle[BarCount][];
        private readonly Rectangle[] _peakIndicators = new Rectangle[BarCount];

        private AudioLevelService _service;
        private DispatcherTimer _renderTimer;
        private bool _initialized;
        private int _renderTickLogCounter;
        private readonly double[] _segmentYPositions = new double[SegmentsPerBar];

        private static readonly SolidColorBrush DimBrush = new SolidColorBrush(ColorFromHex("#1A1D23"));
        private static readonly SolidColorBrush GreenBrush = new SolidColorBrush(ColorFromHex("#93C43C"));
        private static readonly SolidColorBrush YellowBrush = new SolidColorBrush(ColorFromHex("#E0C040"));
        private static readonly SolidColorBrush RedBrush = new SolidColorBrush(ColorFromHex("#E04040"));

        public VuMeterBar()
        {
            this.InitializeComponent();
            this.Loaded += OnLoaded;
            this.Unloaded += OnUnloaded;
        }

        public void AttachService(AudioLevelService service)
        {
            Log.Information("VuMeterBar: AttachService called, initialized={Init}", _initialized);
            _service = service;
            if (_initialized && _service != null)
                StartRendering();
        }

        public void DetachService()
        {
            Log.Verbose("VuMeterBar: DetachService");
            StopRendering();
            _service = null;
            ResetAllBars();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Log.Information("VuMeterBar: OnLoaded — building bars");
            BuildBars();
            _initialized = true;
            Log.Information("VuMeterBar: bars built, service attached={HasService}", _service != null);
            if (_service != null)
                StartRendering();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            StopRendering();
        }

        private void BuildBars()
        {
            BarsContainer.ColumnDefinitions.Clear();
            BarsContainer.Children.Clear();

            for (int b = 0; b < BarCount; b++)
            {
                BarsContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });

                var barOuter = new Grid
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Margin = new Thickness(0.5, 0, 0.5, 0),
                };

                var segs = new Rectangle[SegmentsPerBar];

                for (int s = 0; s < SegmentsPerBar; s++)
                {
                    int fromBottom = SegmentsPerBar - 1 - s;
                    double yPos = fromBottom * (SegmentHeight + SegmentGap);
                    _segmentYPositions[fromBottom] = yPos;
                    var rect = new Rectangle
                    {
                        Height = SegmentHeight,
                        Fill = DimBrush,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(0, yPos, 0, 0),
                    };
                    barOuter.Children.Add(rect);
                    segs[s] = rect;
                }

                // Peak indicator (thin colored line)
                var peak = new Rectangle
                {
                    Height = PeakHeight,
                    Fill = new SolidColorBrush(Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Top,
                    Opacity = 0,
                };
                barOuter.Children.Add(peak);

                _segments[b] = segs;
                _peakIndicators[b] = peak;
                Grid.SetColumn(barOuter, b);
                BarsContainer.Children.Add(barOuter);
            }
        }

        private void StartRendering()
        {
            if (_renderTimer != null && _renderTimer.IsEnabled) return;

            _renderTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16) // ~60fps
            };
            _renderTimer.Tick += OnRenderTick;
            _renderTimer.Start();
        }

        private void StopRendering()
        {
            if (_renderTimer != null)
            {
                _renderTimer.Tick -= OnRenderTick;
                _renderTimer.Stop();
                _renderTimer = null;
            }
        }

        private void OnRenderTick(object sender, object e)
        {
            if (_service == null || !_service.IsAnalyzing)
            {
                ResetAllBars();
                return;
            }

            _renderTickLogCounter++;
            if (_renderTickLogCounter == 1 || _renderTickLogCounter % 300 == 0)
            {
                float sampleLevel = _service.BandLevels.Length > 5 ? _service.BandLevels[5] : 0f;
                Log.Information("VuMeterBar: tick#{Tick} service={Svc} analyzing={Analyzing} sampleLevel[5]={Level:F3}",
                    _renderTickLogCounter, _service != null, _service?.IsAnalyzing, sampleLevel);
            }

            float[] levels = _service.BandLevels;
            float[] peaks = _service.BandPeaks;

            for (int b = 0; b < BarCount && b < levels.Length; b++)
            {
                float level = Math.Min(1.0f, levels[b]);
                float peak = Math.Min(1.0f, peaks[b]);

                int litSegments = (int)(level * SegmentsPerBar);
                litSegments = Math.Max(0, Math.Min(SegmentsPerBar, litSegments));

                for (int s = 0; s < SegmentsPerBar; s++)
                {
                    bool isLit = s < litSegments;

                    if (isLit)
                    {
                        double ratio = (double)s / (SegmentsPerBar - 1);
                        _segments[b][s].Fill = GetSegmentColor(ratio);
                    }
                    else
                    {
                        _segments[b][s].Fill = DimBrush;
                    }
                }

                // Peak indicator positioning
                if (peak > 0.01f)
                {
                    int peakSegment = (int)(peak * SegmentsPerBar);
                    peakSegment = Math.Max(0, Math.Min(SegmentsPerBar - 1, peakSegment));

                    int peakFromBottom = SegmentsPerBar - 1 - peakSegment;
                    double peakY = _segmentYPositions[peakFromBottom];
                    _peakIndicators[b].Margin = new Thickness(0, peakY, 0, 0);
                    _peakIndicators[b].Opacity = 1.0;

                    double peakRatio = (double)peakSegment / (SegmentsPerBar - 1);
                    _peakIndicators[b].Fill = GetSegmentColor(peakRatio);
                }
                else
                {
                    _peakIndicators[b].Opacity = 0;
                }
            }
        }

        private static SolidColorBrush GetSegmentColor(double ratio)
        {
            if (ratio < 0.55)
                return GreenBrush;
            if (ratio < 0.80)
                return YellowBrush;
            return RedBrush;
        }

        private void ResetAllBars()
        {
            if (!_initialized) return;

            for (int b = 0; b < BarCount; b++)
            {
                for (int s = 0; s < SegmentsPerBar; s++)
                {
                    if (_segments[b] != null && _segments[b][s] != null)
                        _segments[b][s].Fill = DimBrush;
                }
                if (_peakIndicators[b] != null)
                    _peakIndicators[b].Opacity = 0;
            }
        }

        private static Color ColorFromHex(string hex)
        {
            hex = hex.TrimStart('#');
            if (hex.Length == 6)
            {
                byte r = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
                byte g = byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
                byte bv = byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
                return Color.FromArgb(255, r, g, bv);
            }
            return Colors.White;
        }
    }
}
