using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Windows.Foundation;
using Windows.System.Display;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using XFiles.FileSystem;
using static XFiles.FileSystem.FileOperations;

namespace XFiles.Controls
{
    public sealed partial class OperationProgressDialog : UserControl
    {
        private CancellationTokenSource _cts;
        private readonly DisplayRequest _displayRequest = new DisplayRequest();
        private bool _displayActive;
        private bool _isIndeterminate;
        private int _completedFileCount;
        private long _completedBytes;
        private List<string> _completedFiles = new List<string>();
        private string _currentOperationTitle = "";
        public Action OnClosed;
        public Action OnCancelled;

        // Speed / ETA estimation
        private readonly System.Diagnostics.Stopwatch _sw = new System.Diagnostics.Stopwatch();
        private readonly TransferStats _stats = new TransferStats();

        // Speed chart (60s window, ~3 samples/sec for a smooth trace)
        private const int ChartWindowSamples = 200;
        private readonly List<double> _chartSpeeds = new List<double>(ChartWindowSamples + 4);
        private double _lastChartSampleSec;
        private readonly DispatcherTimer _chartTimer;

        public bool IsOpen => Visibility == Visibility.Visible;
        public CancellationToken CancelToken => _cts?.Token ?? CancellationToken.None;

        public OperationProgressDialog()
        {
            this.InitializeComponent();
            _chartTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _chartTimer.Tick += OnChartTimerTick;
        }

        public void Show(string title, string source, string destination, int fileIndex = 0, int fileTotal = 0)
        {
            _cts = new CancellationTokenSource();
            _currentOperationTitle = title;
            TitleText.Text = title;
            SourceText.Text = source;
            DestText.Text = destination;
            CurrentFileText.Text = "";
            BytesText.Text = "";
            PercentText.Text = "";
            SpeedText.Text = "";
            EtaText.Text = "";
            ProgressBar.Value = 0;
            _isIndeterminate = false;
            ProgressBar.IsIndeterminate = false;
            _completedFileCount = 0;
            _completedBytes = 0;
            _completedFiles.Clear();
            _sw.Restart();
            _stats.Reset();

            // Reset speed chart
            _chartSpeeds.Clear();
            _lastChartSampleSec = 0;
            SpeedValueText.Text = "";
            SpeedChartPanel.Visibility = Visibility.Collapsed;
            SpeedFillArea.Points?.Clear();
            SpeedPolyline.Points?.Clear();

            // Show/hide overall progress bar for multi-file operations
            if (fileTotal > 1)
            {
                OverallProgressPanel.Visibility = Visibility.Visible;
                OverallProgressBar.Maximum = fileTotal;
                OverallProgressBar.Value = fileIndex;
            }
            else
            {
                OverallProgressPanel.Visibility = Visibility.Collapsed;
            }

            // Show cancel hint, hide summary
            CancelHint.Visibility = Visibility.Visible;
            SummaryPanel.Visibility = Visibility.Collapsed;

            Visibility = Visibility.Visible;
            Overlay.Visibility = Visibility.Visible;

            _chartTimer.Start();

            // Keep the display awake so the Xbox idle dim doesn't throttle the
            // transfer (dimming drops copy throughput dramatically).
            if (!_displayActive)
            {
                _displayRequest.RequestActive();
                _displayActive = true;
            }
        }

        /// <summary>
        /// Updates the operation title / source / destination labels between phases
        /// (e.g. portal zip: download → compress → upload) without resetting progress.
        /// </summary>
        public void SetPhase(string title, string source, string destination)
        {
            _currentOperationTitle = title;
            TitleText.Text = title;
            SourceText.Text = source;
            DestText.Text = destination;
        }

        public void UpdateProgress(OperationProgress progress)
        {
            if (progress == null) return;

            if (!string.IsNullOrEmpty(progress.FileName))
            {
                if (progress.FileTotal > 0)
                    CurrentFileText.Text = $"[{progress.FileIndex}/{progress.FileTotal}] {progress.FileName}";
                else
                    CurrentFileText.Text = progress.FileName;
            }

            if (progress.TotalBytes > 0)
            {
                // Byte-accurate progress dominates: percent, speed, ETA from real bytes.
                double pct = Math.Max(0, Math.Min(100,
                    (double)progress.BytesCopied / progress.TotalBytes * 100.0));

                if (_isIndeterminate)
                {
                    _isIndeterminate = false;
                    ProgressBar.IsIndeterminate = false;
                }
                ProgressBar.Value = pct;
                PercentText.Text = $"{(int)Math.Round(pct)}%";

                string copied = FormatBytes(progress.BytesCopied);
                string total = FormatBytes(progress.TotalBytes);
                BytesText.Text = $"{copied} / {total}";

                UpdateSpeedEta(progress.BytesCopied, progress.TotalBytes);
            }
            else
            {
                BytesText.Text = "";

                if (progress.PercentComplete >= 0)
                {
                    if (_isIndeterminate)
                    {
                        _isIndeterminate = false;
                        ProgressBar.IsIndeterminate = false;
                    }
                    ProgressBar.Value = progress.PercentComplete;
                    PercentText.Text = $"{(int)Math.Round(progress.PercentComplete)}%";
                }
                else
                {
                    if (!_isIndeterminate)
                    {
                        _isIndeterminate = true;
                        ProgressBar.IsIndeterminate = true;
                    }
                    PercentText.Text = "";
                }
            }

            if (progress.FileTotal > 0)
                OverallProgressBar.Value = progress.FileIndex;
        }

        /// <summary>
        /// Track a file as completed (for cancel summary).
        /// </summary>
        public void TrackCompleted(string fileName, long bytesCopied = 0)
        {
            _completedFileCount++;
            _completedBytes += bytesCopied;
            if (!string.IsNullOrEmpty(fileName))
                _completedFiles.Add(fileName);
        }

        /// <summary>
        /// Sets the progress bar from a 0..1 fraction (single-file portal transfers).
        /// </summary>
        public void SetProgress(double fraction)
        {
            if (_isIndeterminate)
            {
                _isIndeterminate = false;
                ProgressBar.IsIndeterminate = false;
            }
            double pct = Math.Max(0, Math.Min(1, fraction)) * 100;
            ProgressBar.Value = pct;
            PercentText.Text = $"{(int)Math.Round(pct)}%";
            CurrentFileText.Text = $"{(int)pct}%";
        }

        public void Complete()
        {
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = 100;
            if (OverallProgressPanel.Visibility == Visibility.Visible)
                OverallProgressBar.Value = OverallProgressBar.Maximum;
            CurrentFileText.Text = "Done";
            BytesText.Text = "";
            PercentText.Text = "100%";
            SpeedText.Text = "";
            EtaText.Text = "";
            _sw.Stop();
            _stats.Reset();
            _chartSpeeds.Clear();
            _lastChartSampleSec = 0;
            SpeedValueText.Text = "";
            SpeedChartPanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Cancel the operation and show summary of what was completed.
        /// </summary>
        public void Cancel()
        {
            _cts?.Cancel();

            ProgressBar.IsIndeterminate = false;
            if (OverallProgressPanel.Visibility == Visibility.Visible)
                OverallProgressBar.IsIndeterminate = false;

            // Build summary
            var sb = new StringBuilder();
            sb.AppendLine($"{_completedFileCount} file(s) completed before cancel.");

            if (_completedBytes > 0)
                sb.AppendLine($"{FormatBytes(_completedBytes)} transferred.");

            if (_completedFiles.Count > 0)
            {
                sb.AppendLine();
                sb.Append("Completed: ");
                int showCount = Math.Min(_completedFiles.Count, 5);
                for (int i = 0; i < showCount; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(_completedFiles[i]);
                }
                if (_completedFiles.Count > 5)
                    sb.Append($" ...and {_completedFiles.Count - 5} more");
            }

            SummaryText.Text = sb.ToString();
            SummaryPanel.Visibility = Visibility.Visible;
            CancelHint.Visibility = Visibility.Collapsed;
            CurrentFileText.Text = "Cancelled";
            _chartSpeeds.Clear();
            _lastChartSampleSec = 0;
            SpeedValueText.Text = "";
            SpeedChartPanel.Visibility = Visibility.Collapsed;
        }

        public void Close()
        {
            _chartTimer.Stop();
            Overlay.Visibility = Visibility.Collapsed;
            Visibility = Visibility.Collapsed;
            _cts?.Cancel();
            _cts = null;
            if (_displayActive)
            {
                _displayRequest.RequestRelease();
                _displayActive = false;
            }
            OnClosed?.Invoke();
        }

        /// <summary>
        /// Windowed speed (last ~4s of samples) + ETA from remaining bytes.
        /// </summary>
        private void UpdateSpeedEta(long bytesCopied, long totalBytes)
        {
            double now = _sw.Elapsed.TotalSeconds;
            _stats.Sample(now, bytesCopied);

            double speedBps = _stats.SpeedBytesPerSecond();
            if (speedBps <= 0)
            {
                SpeedText.Text = "—";
                EtaText.Text = "";
                return;
            }

            // Feed the speed chart every ~300ms (60s window). Each chart sample is the
            // ~2s sliding-average speed from TransferStats (monotonic-clamped), not the
            // raw delta between progress callbacks — those arrive at uneven rates and
            // reflect per-chunk bursts that never show up in the real sustained speed.
            // The window is fixed: the oldest sample sits at the left edge and the trace
            // fills left-to-right, then scrolls once the window is full.
            if (now - _lastChartSampleSec >= 0.3)
            {
                _lastChartSampleSec = now;
                _chartSpeeds.Add(_stats.SpeedBytesPerSecond(2.0));
                while (_chartSpeeds.Count > ChartWindowSamples)
                    _chartSpeeds.RemoveAt(0);
            }

            SpeedText.Text = FormatBytes((long)speedBps) + "/s";

            double etaSec = _stats.EtaSeconds(totalBytes, bytesCopied);
            if (etaSec < 0)
            {
                EtaText.Text = "";
            }
            else if (etaSec >= 3600)
                EtaText.Text = $"ETA {(int)(etaSec / 3600)}:{(int)(etaSec % 3600 / 60):00}:{(int)(etaSec % 60):00}";
            else if (etaSec > 0)
                EtaText.Text = $"ETA {(int)(etaSec / 60)}:{(int)(etaSec % 60):00}";
            else
                EtaText.Text = "";
        }

        private void OnOverlayTapped(object sender, TappedRoutedEventArgs e)
        {
            // Don't close on tap — only cancel via B button
        }

        private void OnChartTimerTick(object sender, object e)
        {
            if (Visibility != Visibility.Visible) return;
            RedrawChart();
        }

        /// <summary>
        /// Rebuilds the speed chart from the last 60s of samples. Hides the panel when
        /// there is no byte-accurate progress (e.g. portal transfers use fraction only).
        /// The panel stretches to the dialog width (same as the progress bars), so the
        /// trace always matches their width. The x-axis maps to the full fixed window
        /// (ChartWindowSamples), inset by a small pad on each side. The newest sample
        /// sits at the RIGHT edge and the trace fills right-to-left over the first 60s,
        /// then scrolls once full. Unfilled leading slots are zero-padded to pin the
        /// baseline. The vertical scale rounds up to a "nice" 1/2/5×10^n value so the
        /// trace fluctuates visibly instead of being pinned to the top by a max-fit scale.
        /// </summary>
        private void RedrawChart()
        {
            if (_chartSpeeds.Count < 2)
            {
                SpeedChartPanel.Visibility = Visibility.Collapsed;
                return;
            }

            SpeedChartPanel.Visibility = Visibility.Visible;

            double w = SpeedChartArea.ActualWidth;
            double h = SpeedChartArea.ActualHeight;
            if (w <= 0 || h <= 0) return; // just became visible — layout not done yet; draw next tick

            double xPad = Math.Min(10, w * 0.08);

            double max = 0;
            for (int i = 0; i < _chartSpeeds.Count; i++)
                if (_chartSpeeds[i] > max) max = _chartSpeeds[i];
            max = RoundUpNice(max);
            if (max <= 0)
            {
                SpeedChartPanel.Visibility = Visibility.Collapsed;
                return;
            }

            int pad = ChartWindowSamples - _chartSpeeds.Count;
            var line = new PointCollection();
            for (int i = 0; i < ChartWindowSamples; i++)
            {
                double value = i < pad ? 0 : _chartSpeeds[i - pad];
                double x = xPad + i / (double)(ChartWindowSamples - 1) * (w - 2 * xPad);
                double y = h - (Math.Min(value, max) / max) * h;
                line.Add(new Point(x, y));
            }
            SpeedPolyline.Points = line;

            var area = new PointCollection();
            area.Add(new Point(xPad, h));
            for (int i = 0; i < line.Count; i++)
                area.Add(line[i]);
            area.Add(new Point(w - xPad, h));
            SpeedFillArea.Points = area;

            SpeedValueText.Text = FormatBytes((long)_chartSpeeds[_chartSpeeds.Count - 1]) + "/s";
        }

        /// <summary>
        /// Rounds a value up to the nearest "nice" 1/2/5×10^n number, giving a stable
        /// chart scale (1, 2, 5, 10, 20, 50, 100 MB/s …).
        /// </summary>
        private static double RoundUpNice(double value)
        {
            if (value <= 0) return 1;
            double mag = Math.Pow(10, Math.Floor(Math.Log10(value)));
            double norm = value / mag;
            double step;
            if (norm <= 1) step = 1;
            else if (norm <= 2) step = 2;
            else if (norm <= 5) step = 5;
            else step = 10;
            return step * mag;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }
    }
}
