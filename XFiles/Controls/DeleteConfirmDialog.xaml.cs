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
    /// Delete confirmation dialog with scrollable file list.
    /// Returns true=delete, false=cancel.
    /// </summary>
    public sealed partial class DeleteConfirmDialog : UserControl
    {
        private TaskCompletionSource<bool> _tcs;

        public DeleteConfirmDialog()
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
                case VirtualKey.GamepadA:
                case VirtualKey.Enter:
                    e.Handled = true;
                    Close(true);
                    break;
                case VirtualKey.GamepadB:
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

        private void OnOverlayTapped(object sender, TappedRoutedEventArgs e)
        {
            Close(false);
        }

        private void Close(bool result)
        {
            Log.Information("DeleteConfirmDialog.Close: result={Result}", result);
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
