using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace XFiles.Controls
{
    /// <summary>
    /// File operation confirmation dialog with scrollable file list.
    /// Used for both delete and move operations.
    /// Returns true=confirm, false=cancel.
    /// </summary>
    public sealed partial class FileOperationConfirmDialog : UserControl
    {
        private TaskCompletionSource<bool> _tcs;

        public FileOperationConfirmDialog()
        {
            this.InitializeComponent();
        }

        /// <summary>
        /// Show delete confirmation with file list.
        /// </summary>
        /// <param name="itemName">Name of file/folder being deleted</param>
        /// <param name="isFolder">True if deleting a folder</param>
        /// <param name="files">List of file paths that will be deleted</param>
        /// <param name="folderCount">Number of folders in the list</param>
        public Task<bool> ShowAsync(string itemName, bool isFolder, List<string> files, int folderCount)
        {
            string suffix = isFolder ? " (including all contents)" : "";
            SummaryText.Text = $"Delete '{itemName}'{suffix}?";

            int fileCount = files.Count - folderCount;
            CountText.Text = $"{fileCount} file(s), {folderCount} folder(s)";

            FileListText.Text = string.Join("\n", files);

            DeleteButton.Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x44, 0x44, 0x44));
            DeleteButtonText.Text = "DELETE";

            _tcs = new TaskCompletionSource<bool>();
            Visibility = Visibility.Visible;
            Overlay.Visibility = Visibility.Visible;

            FileListScroll.ScrollToVerticalOffset(0);
            return _tcs.Task;
        }

        /// <summary>
        /// Show move confirmation with file list.
        /// </summary>
        public Task<bool> ShowMoveAsync(string itemName, string destPath, List<string> files, int folderCount)
        {
            SummaryText.Text = $"Move '{itemName}' to '{destPath}'?";

            int fileCount = files.Count - folderCount;
            CountText.Text = $"{fileCount} file(s), {folderCount} folder(s)";

            FileListText.Text = string.Join("\n", files);

            DeleteButton.Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x1B, 0x6E, 0xD1));
            DeleteButtonText.Text = "MOVE";

            _tcs = new TaskCompletionSource<bool>();
            Visibility = Visibility.Visible;
            Overlay.Visibility = Visibility.Visible;

            FileListScroll.ScrollToVerticalOffset(0);
            return _tcs.Task;
        }

        private void OnDeleteClicked(object sender, RoutedEventArgs e)
        {
            Close(true);
        }

        private void OnCancelClicked(object sender, RoutedEventArgs e)
        {
            Close(false);
        }

        private void OnKeyDown(object sender, KeyRoutedEventArgs e)
        {
            switch (e.Key)
            {
                case VirtualKey.Enter:
                    e.Handled = true;
                    Close(true);
                    break;
                case VirtualKey.Escape:
                    e.Handled = true;
                    Close(false);
                    break;
                case VirtualKey.GamepadDPadUp:
                case VirtualKey.Up:
                    e.Handled = true;
                    ScrollList(-1);
                    break;
                case VirtualKey.GamepadDPadDown:
                case VirtualKey.Down:
                    e.Handled = true;
                    ScrollList(1);
                    break;
                case VirtualKey.GamepadDPadLeft:
                case VirtualKey.GamepadDPadRight:
                case VirtualKey.Left:
                case VirtualKey.Right:
                    e.Handled = true;
                    break;
            }
        }

        private void ScrollList(int direction)
        {
            double offset = FileListScroll.VerticalOffset + (direction * 40);
            offset = Math.Max(0, Math.Min(offset, FileListScroll.ExtentHeight - FileListScroll.ViewportHeight));
            FileListScroll.ScrollToVerticalOffset(offset);
        }

        /// <summary>
        /// Scroll the file list based on analog stick Y input.
        /// Called by MillerColumnsPage.OnLeftStickMove/OnRightStickMove.
        /// </summary>
        public void HandleStick(float x, float y)
        {
            if (Math.Abs(y) < 0.15f) return; // deadzone

            double maxScroll = FileListScroll.ExtentHeight - FileListScroll.ViewportHeight;
            if (maxScroll <= 0) return;

            double speed = 8.0;
            double offset = FileListScroll.VerticalOffset - (y * speed);
            offset = Math.Max(0, Math.Min(offset, maxScroll));
            FileListScroll.ScrollToVerticalOffset(offset);
        }

        private void OnOverlayTapped(object sender, TappedRoutedEventArgs e)
        {
            Close(false);
        }

        private void Close(bool result)
        {
            Log.Debug("FileOperationConfirmDialog.Close: result={Result}", result);
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
                    Close(true);
                    break;
                case VirtualKey.GamepadB:
                case VirtualKey.Escape:
                    Close(false);
                    break;
            }
        }
    }
}
