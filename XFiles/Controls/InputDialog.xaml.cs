using System;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace XFiles.Controls
{
    public sealed partial class InputDialog : UserControl
    {
        private TaskCompletionSource<string> _tcs;
        public Action OnClosed;

        public InputDialog()
        {
            this.InitializeComponent();
        }

        public Task<string> ShowAsync(string title, string defaultValue)
        {
            return ShowAsync(title, defaultValue, false);
        }

        public Task<string> ShowAsync(string title, string defaultValue, bool isPassword)
        {
            Log.Info("InputDialog.ShowAsync: title=\"{Title}\" default=\"{Default}\" isPassword={IsPassword}", title, defaultValue, isPassword);
            TitleText.Text = title;

            if (isPassword)
            {
                InputBox.Visibility = Visibility.Collapsed;
                PasswordBox.Visibility = Visibility.Visible;
                PasswordBox.Password = defaultValue ?? "";
                _tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                Visibility = Visibility.Visible;
                Overlay.Visibility = Visibility.Visible;
                PasswordBox.Focus(FocusState.Programmatic);
            }
            else
            {
                InputBox.Visibility = Visibility.Visible;
                PasswordBox.Visibility = Visibility.Collapsed;
                InputBox.Text = defaultValue ?? "";
                _tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                Visibility = Visibility.Visible;
                Overlay.Visibility = Visibility.Visible;
                InputBox.Focus(FocusState.Programmatic);
                InputBox.SelectAll();
            }
            return _tcs.Task;
        }

        private void OnInputGotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb) tb.SelectAll();
            else if (sender is PasswordBox pb) { /* PasswordBox doesn't have SelectAll */ }
        }

        private void OnInputKeyDown(object sender, KeyRoutedEventArgs e)
        {
            switch (e.Key)
            {
                case Windows.System.VirtualKey.Enter:
                case Windows.System.VirtualKey.GamepadMenu:
                    e.Handled = true;
                    Log.Dbg("InputDialog: key {Key} → confirm", e.Key);
                    Close(sender is PasswordBox pb ? pb.Password : InputBox.Text);
                    break;
                case Windows.System.VirtualKey.Escape:
                case Windows.System.VirtualKey.GamepadB:
                    e.Handled = true;
                    Log.Dbg("InputDialog: key {Key} → cancel", e.Key);
                    Close(null);
                    break;
            }
        }

        private void OnOverlayKeyDown(object sender, KeyRoutedEventArgs e)
        {
            switch (e.Key)
            {
                case Windows.System.VirtualKey.GamepadA:
                case Windows.System.VirtualKey.Enter:
                    e.Handled = true;
                    Log.Dbg("InputDialog: overlay key {Key} → confirm", e.Key);
                    Close(PasswordBox.Visibility == Visibility.Visible ? PasswordBox.Password : InputBox.Text);
                    break;
                case Windows.System.VirtualKey.GamepadB:
                case Windows.System.VirtualKey.Escape:
                    e.Handled = true;
                    Log.Dbg("InputDialog: overlay key {Key} → cancel", e.Key);
                    Close(null);
                    break;
                case Windows.System.VirtualKey.GamepadDPadUp:
                case Windows.System.VirtualKey.GamepadDPadDown:
                case Windows.System.VirtualKey.GamepadDPadLeft:
                case Windows.System.VirtualKey.GamepadDPadRight:
                case Windows.System.VirtualKey.Up:
                case Windows.System.VirtualKey.Down:
                case Windows.System.VirtualKey.Left:
                case Windows.System.VirtualKey.Right:
                    e.Handled = true;
                    break;
            }
        }

        private void OnOkClicked(object sender, RoutedEventArgs e)
        {
            Log.Dbg("InputDialog: OK button → confirm");
            Close(PasswordBox.Visibility == Visibility.Visible ? PasswordBox.Password : InputBox.Text);
        }

        private void OnOverlayTapped(object sender, TappedRoutedEventArgs e)
        {
            Log.Dbg("InputDialog: overlay tapped → cancel");
            Close(null);
        }

        private void Close(string result)
        {
            Log.Dbg("InputDialog.Close: result={Result}", result == null ? "null" : $"\"{result}\"");
            Overlay.Visibility = Visibility.Collapsed;
            Visibility = Visibility.Collapsed;
            _tcs?.TrySetResult(result);
            OnClosed?.Invoke();
        }

        public void HandleButton(Windows.System.VirtualKey key)
        {
            Log.Dbg("InputDialog.HandleButton: key={Key}", key);
            switch (key)
            {
                case Windows.System.VirtualKey.GamepadA:
                case Windows.System.VirtualKey.Enter:
                    Close(PasswordBox.Visibility == Visibility.Visible ? PasswordBox.Password : InputBox.Text);
                    break;
                case Windows.System.VirtualKey.GamepadB:
                case Windows.System.VirtualKey.Escape:
                    Close(null);
                    break;
            }
        }
    }
}
