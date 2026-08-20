using System;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using XFiles.FileSystem;

namespace XFiles.Controls
{
    /// <summary>
    /// Name-collision dialog for copy/move: REPLACE ALL / RENAME ALL / CANCEL,
    /// and partial-file dialog: RESUME / OVERWRITE / CANCEL.
    /// </summary>
    public sealed partial class FileConflictDialog : UserControl
    {
        private TaskCompletionSource<ConflictDecision> _tcs;

        public FileConflictDialog()
        {
            this.InitializeComponent();
        }

        /// <summary>
        /// Show the collision dialog for the given destination path.
        /// Returns ReplaceAll, RenameAll, or Cancel.
        /// </summary>
        public Task<ConflictDecision> ShowAsync(string path)
        {
            MessageText.Text = $"A file with this name already exists:\n{path}\n\nReplace it, keep both, or cancel?";
            SetMode(false);
            _tcs = new TaskCompletionSource<ConflictDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
            Visibility = Visibility.Visible;
            Overlay.Visibility = Visibility.Visible;
            return _tcs.Task;
        }

        /// <summary>
        /// Show the partial-file dialog. Returns Resume, ReplaceAll, or Cancel.
        /// </summary>
        public Task<ConflictDecision> ShowPartialAsync(string path, long existingBytes, long totalBytes)
        {
            string existing = FormatBytes(existingBytes);
            string total = FormatBytes(totalBytes);
            int pct = totalBytes > 0 ? (int)(existingBytes * 100 / totalBytes) : 0;
            MessageText.Text = $"File partially copied ({existing} of {total}, {pct}%):\n{path}\n\nResume from where it stopped, overwrite from start, or cancel?";
            SetMode(true);
            _tcs = new TaskCompletionSource<ConflictDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
            Visibility = Visibility.Visible;
            Overlay.Visibility = Visibility.Visible;
            return _tcs.Task;
        }

        private void SetMode(bool partial)
        {
            _partialMode = partial;
            if (partial)
            {
                ReplaceButton.Content = MakeButtonContent("OVERWRITE", "abxy/x.png");
                RenameButton.Content = MakeButtonContent("RESUME", "abxy/a.png");
                ButtonsPanel.Children.Clear();
                ButtonsPanel.Children.Add(RenameButton);
                ButtonsPanel.Children.Add(ReplaceButton);
                ButtonsPanel.Children.Add(CancelButton);
            }
            else
            {
                ReplaceButton.Content = MakeButtonContent("REPLACE ALL", "abxy/a.png");
                RenameButton.Content = MakeButtonContent("RENAME ALL", "abxy/y.png");
                ButtonsPanel.Children.Clear();
                ButtonsPanel.Children.Add(ReplaceButton);
                ButtonsPanel.Children.Add(RenameButton);
                ButtonsPanel.Children.Add(CancelButton);
            }
        }

        private static StackPanel MakeButtonContent(string label, string icon)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new Image
            {
                Source = new Windows.UI.Xaml.Media.Imaging.BitmapImage(
                    new Uri($"ms-appx:///Assets/GamepadButtons/{icon}")),
                Width = 18, Height = 18, Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            sp.Children.Add(new TextBlock
            {
                Text = label,
                FontFamily = new FontFamily("ms-appx:///Assets/Fonts/Oxanium-Bold.ttf#Oxanium"),
                Foreground = (Brush)Application.Current.Resources["XFilesForegroundBrush"],
                VerticalAlignment = VerticalAlignment.Center
            });
            return sp;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1073741824) return $"{bytes / 1073741824.0:F1} GB";
            if (bytes >= 1048576) return $"{bytes / 1048576.0:F1} MB";
            if (bytes >= 1024) return $"{bytes / 1024.0:F0} KB";
            return $"{bytes} B";
        }

        private void OnReplaceAllClicked(object sender, RoutedEventArgs e)
        {
            Close(_partialMode ? ConflictDecision.ReplaceAll : ConflictDecision.ReplaceAll);
        }

        private void OnRenameAllClicked(object sender, RoutedEventArgs e)
        {
            Close(_partialMode ? ConflictDecision.Resume : ConflictDecision.RenameAll);
        }

        private void OnCancelClicked(object sender, RoutedEventArgs e)
        {
            Close(ConflictDecision.Cancel);
        }

        private bool _partialMode;

        private void OnKeyDown(object sender, KeyRoutedEventArgs e)
        {
            switch (e.Key)
            {
                case VirtualKey.GamepadA:
                case VirtualKey.Enter:
                    e.Handled = true;
                    Close(_partialMode ? ConflictDecision.Resume : ConflictDecision.ReplaceAll);
                    break;
                case VirtualKey.GamepadY:
                    e.Handled = true;
                    if (!_partialMode) Close(ConflictDecision.RenameAll);
                    break;
                case VirtualKey.GamepadX:
                    e.Handled = true;
                    if (_partialMode) Close(ConflictDecision.ReplaceAll);
                    break;
                case VirtualKey.GamepadB:
                case VirtualKey.Escape:
                    e.Handled = true;
                    Close(ConflictDecision.Cancel);
                    break;
                case VirtualKey.GamepadDPadUp:
                case VirtualKey.GamepadDPadDown:
                case VirtualKey.GamepadDPadLeft:
                case VirtualKey.GamepadDPadRight:
                case VirtualKey.Up:
                case VirtualKey.Down:
                case VirtualKey.Left:
                case VirtualKey.Right:
                    e.Handled = true;
                    break;
            }
        }

        private void OnOverlayTapped(object sender, TappedRoutedEventArgs e)
        {
            Close(ConflictDecision.Cancel);
        }

        private void Close(ConflictDecision result)
        {
            Log.Dbg("FileConflictDialog.Close: result={Result}", result);
            Overlay.Visibility = Visibility.Collapsed;
            Visibility = Visibility.Collapsed;
            _tcs?.TrySetResult(result);
        }

        public bool IsDialogVisible => Visibility == Visibility.Visible;

        public void HandleButton(VirtualKey key)
        {
            switch (key)
            {
                case VirtualKey.GamepadA:
                case VirtualKey.Enter:
                    Close(_partialMode ? ConflictDecision.Resume : ConflictDecision.ReplaceAll);
                    break;
                case VirtualKey.GamepadY:
                    if (!_partialMode) Close(ConflictDecision.RenameAll);
                    break;
                case VirtualKey.GamepadX:
                    if (_partialMode) Close(ConflictDecision.ReplaceAll);
                    break;
                case VirtualKey.GamepadB:
                case VirtualKey.Escape:
                    Close(ConflictDecision.Cancel);
                    break;
            }
        }
    }
}
