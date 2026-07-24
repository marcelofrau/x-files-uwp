using System;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace XFiles.Controls
{
    public enum AlertType
    {
        Info,
        Question,
        Warning,
        Error,
        Success
    }

    public sealed partial class AlertDialog : UserControl
    {
        private TaskCompletionSource<bool> _tcs;
        public Action OnClosed;

        private static class Icons
        {
            public const string Info = "ms-appx:///Assets/Views/AlertDialog/alert-info-48.png";
            public const string Question = "ms-appx:///Assets/Views/AlertDialog/alert-question-48.png";
            public const string Warning = "ms-appx:///Assets/Views/AlertDialog/alert-warning-48.png";
            public const string Error = "ms-appx:///Assets/Views/AlertDialog/alert-error-48.png";
            public const string Success = "ms-appx:///Assets/Views/AlertDialog/alert-success-48.png";
        }

        public AlertDialog()
        {
            this.InitializeComponent();
        }

        /// <summary>
        /// Show alert with one button (OK). Returns true when dismissed.
        /// Backward-compatible: existing ShowAsync(string) calls still work.
        /// </summary>
        public Task<bool> ShowAsync(string message)
        {
            return ShowInternal(message, AlertType.Info, false, null, null);
        }

        /// <summary>
        /// Show alert with specified type and one OK button.
        /// </summary>
        public Task<bool> ShowAsync(string message, AlertType type)
        {
            return ShowInternal(message, type, false, null, null);
        }

        /// <summary>
        /// Show confirm dialog (Yes/No). Returns true for Yes, false for No.
        /// </summary>
        public Task<bool> ShowConfirmAsync(string message)
        {
            return ShowInternal(message, AlertType.Question, true, "YES", "NO");
        }

        /// <summary>
        /// Show confirm dialog with custom button labels.
        /// </summary>
        public Task<bool> ShowConfirmAsync(string message, AlertType type,
            string yesLabel = "YES", string noLabel = "NO")
        {
            return ShowInternal(message, type, true, yesLabel, noLabel);
        }

        private Task<bool> ShowInternal(string message, AlertType type,
            bool showNoButton, string yesLabel, string noLabel)
        {
            MessageText.Text = message;

            switch (type)
            {
                case AlertType.Warning:
                    TypeIcon.Source = new Windows.UI.Xaml.Media.Imaging.BitmapImage(
                        new Uri(Icons.Warning));
                    break;
                case AlertType.Error:
                    TypeIcon.Source = new Windows.UI.Xaml.Media.Imaging.BitmapImage(
                        new Uri(Icons.Error));
                    break;
                case AlertType.Success:
                    TypeIcon.Source = new Windows.UI.Xaml.Media.Imaging.BitmapImage(
                        new Uri(Icons.Success));
                    break;
                case AlertType.Question:
                    TypeIcon.Source = new Windows.UI.Xaml.Media.Imaging.BitmapImage(
                        new Uri(Icons.Question));
                    break;
                case AlertType.Info:
                default:
                    TypeIcon.Source = new Windows.UI.Xaml.Media.Imaging.BitmapImage(
                        new Uri(Icons.Info));
                    break;
            }

            YesLabel.Text = yesLabel ?? (showNoButton ? "YES" : "OK");
            NoButton.Visibility = showNoButton ? Visibility.Visible : Visibility.Collapsed;
            if (showNoButton)
                NoLabel.Text = noLabel ?? "NO";

            _tcs = new TaskCompletionSource<bool>();
            Visibility = Visibility.Visible;
            Overlay.Visibility = Visibility.Visible;
            return _tcs.Task;
        }

        private void OnYesClicked(object sender, RoutedEventArgs e)
        {
            Close(true);
        }

        private void OnNoClicked(object sender, RoutedEventArgs e)
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
                    Close(NoButton.Visibility == Visibility.Visible);
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
            Close(NoButton.Visibility != Visibility.Visible);
        }

        private void Close(bool result)
        {
            Log.Information("AlertDialog.Close: result={Result}", result);
            Overlay.Visibility = Visibility.Collapsed;
            Visibility = Visibility.Collapsed;
            _tcs?.TrySetResult(result);
            OnClosed?.Invoke();
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
                    Close(NoButton.Visibility == Visibility.Visible);
                    break;
            }
        }
    }
}
