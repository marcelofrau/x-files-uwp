using System;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace XFiles.Controls
{
    /// <summary>
    /// Conflict dialog for file extraction: Overwrite / Overwrite All / Skip.
    /// Returns 0=Skip, 1=Overwrite, 2=OverwriteAll.
    /// </summary>
    public sealed partial class OverwriteDialog : UserControl
    {
        private TaskCompletionSource<int> _tcs;

        public OverwriteDialog()
        {
            this.InitializeComponent();
        }

        /// <summary>
        /// Show overwrite conflict for the given file.
        /// Returns 0=Skip, 1=Overwrite, 2=OverwriteAll.
        /// </summary>
        public Task<int> ShowAsync(string fileName)
        {
            MessageText.Text = $"File already exists:\n{fileName}\n\nOverwrite?";
            _tcs = new TaskCompletionSource<int>();
            Visibility = Visibility.Visible;
            Overlay.Visibility = Visibility.Visible;
            return _tcs.Task;
        }

        private void OnOverwriteClicked(object sender, RoutedEventArgs e)
        {
            Close(1);
        }

        private void OnOverwriteAllClicked(object sender, RoutedEventArgs e)
        {
            Close(2);
        }

        private void OnSkipClicked(object sender, RoutedEventArgs e)
        {
            Close(0);
        }

        private void OnKeyDown(object sender, KeyRoutedEventArgs e)
        {
            switch (e.Key)
            {
                case VirtualKey.GamepadA:
                case VirtualKey.Enter:
                    e.Handled = true;
                    Close(1); // Overwrite
                    break;
                case VirtualKey.GamepadY:
                    e.Handled = true;
                    Close(2); // Overwrite All
                    break;
                case VirtualKey.GamepadB:
                case VirtualKey.Escape:
                    e.Handled = true;
                    Close(0); // Skip
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
            Close(0); // Skip on tap
        }

        private void Close(int result)
        {
            Log.Information("OverwriteDialog.Close: result={Result}", result);
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
                    Close(1);
                    break;
                case VirtualKey.GamepadY:
                    Close(2);
                    break;
                case VirtualKey.GamepadB:
                case VirtualKey.Escape:
                    Close(0);
                    break;
            }
        }
    }
}
