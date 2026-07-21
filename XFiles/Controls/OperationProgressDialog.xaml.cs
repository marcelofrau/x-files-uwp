using System;
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

        public bool IsOpen => Visibility == Visibility.Visible;
        public CancellationToken CancelToken => _cts?.Token ?? CancellationToken.None;

        public OperationProgressDialog()
        {
            this.InitializeComponent();
        }

        public void Show(string title, string source, string destination)
        {
            _cts = new CancellationTokenSource();
            TitleText.Text = title;
            SourceText.Text = source;
            DestText.Text = destination;
            CurrentFileText.Text = "";
            BytesText.Text = "";
            ProgressBar.Value = 0;
            _isIndeterminate = false;
            ProgressBar.IsIndeterminate = false;
            Visibility = Visibility.Visible;
            Overlay.Visibility = Visibility.Visible;
        }

        public void UpdateProgress(OperationProgress progress)
        {
            if (progress == null) return;

            if (!string.IsNullOrEmpty(progress.FileName))
                CurrentFileText.Text = progress.FileName;

            if (progress.PercentComplete < 0)
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
        }

        public void Complete()
        {
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = 100;
            CurrentFileText.Text = "Done";
            BytesText.Text = "";
        }

        public void Close()
        {
            Overlay.Visibility = Visibility.Collapsed;
            Visibility = Visibility.Collapsed;
            _cts?.Cancel();
            _cts = null;
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
