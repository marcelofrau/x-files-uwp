using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using XFiles.FileSystem;
using static XFiles.FileSystem.FileOperations;

namespace XFiles.Controls
{
    public sealed partial class OperationProgressDialog : UserControl
    {
        private CancellationTokenSource _cts;
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

        public bool IsOpen => Visibility == Visibility.Visible;
        public CancellationToken CancelToken => _cts?.Token ?? CancellationToken.None;

        public OperationProgressDialog()
        {
            this.InitializeComponent();
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

            // Show/hide overall progress bar for multi-file operations
            if (fileTotal > 1)
            {
                OverallProgressPanel.Visibility = Visibility.Visible;
                OverallProgressBar.Maximum = fileTotal;
                OverallProgressBar.Value = fileIndex;
                FileLabel.Text = "Current file";
            }
            else
            {
                OverallProgressPanel.Visibility = Visibility.Collapsed;
                FileLabel.Text = "";
            }

            // Show cancel hint, hide summary
            CancelHint.Visibility = Visibility.Visible;
            SummaryPanel.Visibility = Visibility.Collapsed;

            Visibility = Visibility.Visible;
            Overlay.Visibility = Visibility.Visible;
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
        }

        public void Close()
        {
            Overlay.Visibility = Visibility.Collapsed;
            Visibility = Visibility.Collapsed;
            _cts?.Cancel();
            _cts = null;
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

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }
    }
}
