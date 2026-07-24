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
            ProgressBar.Value = 0;
            _isIndeterminate = false;
            ProgressBar.IsIndeterminate = false;
            _completedFileCount = 0;
            _completedBytes = 0;
            _completedFiles.Clear();

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

            if (progress.FileTotal > 0)
            {
                // Multi-file mode: overall progress tracks file count
                if (_isIndeterminate)
                {
                    _isIndeterminate = false;
                    ProgressBar.IsIndeterminate = false;
                }
                OverallProgressBar.Value = progress.FileIndex;
                ProgressBar.Value = progress.PercentComplete >= 0 ? progress.PercentComplete : 0;
                if (progress.PercentComplete < 0)
                    ProgressBar.IsIndeterminate = true;
            }
            else if (progress.PercentComplete < 0)
            {
                if (!_isIndeterminate)
                {
                    _isIndeterminate = true;
                    ProgressBar.IsIndeterminate = true;
                }
            }
            else
            {
                if (_isIndeterminate)
                {
                    _isIndeterminate = false;
                    ProgressBar.IsIndeterminate = false;
                }
                ProgressBar.Value = progress.PercentComplete;
            }

            if (progress.TotalBytes > 0)
            {
                string copied = FormatBytes(progress.BytesCopied);
                string total = FormatBytes(progress.TotalBytes);
                BytesText.Text = $"{copied} / {total}";
            }
            else
            {
                BytesText.Text = "";
            }
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

        public void Complete()
        {
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = 100;
            if (OverallProgressPanel.Visibility == Visibility.Visible)
                OverallProgressBar.Value = OverallProgressBar.Maximum;
            CurrentFileText.Text = "Done";
            BytesText.Text = "";
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
