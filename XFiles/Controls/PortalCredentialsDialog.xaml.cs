using System;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace XFiles.Controls
{
    public sealed partial class PortalCredentialsDialog : UserControl
    {
        private TaskCompletionSource<PortalCredentialsResult> _tcs;
        public Action OnClosed;

        // 0 = User, 1 = Pass, 2 = Cancel, 3 = Save
        private int _focusIndex = 0;

        public PortalCredentialsDialog()
        {
            this.InitializeComponent();
        }

        /// <summary>
        /// Prompts for portal user + password. Returns null on cancel.
        /// </summary>
        public Task<PortalCredentialsResult> ShowAsync(string title, string prefilledUser)
        {
            Log.Info("PortalCredentialsDialog.ShowAsync: title=\"{Title}\" prefilledUser=\"{User}\"", title, prefilledUser ?? "");
            TitleText.Text = title;
            UserBox.Text = prefilledUser ?? "";
            PassBox.Password = "";
            _tcs = new TaskCompletionSource<PortalCredentialsResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            Canvas.SetZIndex(this, 400);
            Visibility = Visibility.Visible;
            Overlay.Visibility = Visibility.Visible;

            _focusIndex = string.IsNullOrEmpty(UserBox.Text) ? 0 : 1;
            ApplyFocus();
            return _tcs.Task;
        }

        private void OnUserGotFocus(object sender, RoutedEventArgs e)
        {
            UserBox.SelectAll();
        }

        private void OnUserKeyDown(object sender, KeyRoutedEventArgs e)
        {
            switch (e.Key)
            {
                case Windows.System.VirtualKey.Enter:
                    e.Handled = true;
                    PassBox.Focus(FocusState.Programmatic);
                    break;
                case Windows.System.VirtualKey.GamepadMenu:
                    e.Handled = true;
                    Log.Dbg("PortalCredentialsDialog: user Start → next component");
                    MoveFocusNext();
                    break;
                case Windows.System.VirtualKey.Escape:
                case Windows.System.VirtualKey.GamepadB:
                    e.Handled = true;
                    Close(null);
                    break;
            }
        }

        private void OnPassKeyDown(object sender, KeyRoutedEventArgs e)
        {
            switch (e.Key)
            {
                case Windows.System.VirtualKey.Enter:
                    e.Handled = true;
                    Log.Dbg("PortalCredentialsDialog: pass Enter → confirm");
                    Close(MakeResult());
                    break;
                case Windows.System.VirtualKey.GamepadMenu:
                    // Start with the virtual keyboard open must NOT confirm the dialog —
                    // it dismisses the input focus and moves to the next component
                    // (the first button), which also closes the on-screen keyboard.
                    e.Handled = true;
                    Log.Dbg("PortalCredentialsDialog: pass Start → next component");
                    MoveFocusNext();
                    break;
                case Windows.System.VirtualKey.Escape:
                case Windows.System.VirtualKey.GamepadB:
                    e.Handled = true;
                    Log.Dbg("PortalCredentialsDialog: pass key {Key} → cancel", e.Key);
                    Close(null);
                    break;
            }
        }

        private void MoveFocusNext()
        {
            // User → Pass → Cancel → Save. Landing on a button (Cancel/Save) also
            // dismisses the on-screen keyboard.
            if (_focusIndex < 3)
            {
                _focusIndex++;
                ApplyFocus();
            }
        }

        private void OnOverlayKeyDown(object sender, KeyRoutedEventArgs e)
        {
            switch (e.Key)
            {
                case Windows.System.VirtualKey.GamepadA:
                case Windows.System.VirtualKey.Enter:
                    e.Handled = true;
                    Log.Dbg("PortalCredentialsDialog: overlay key {Key} → confirm", e.Key);
                    Close(MakeResult());
                    break;
                case Windows.System.VirtualKey.GamepadB:
                case Windows.System.VirtualKey.Escape:
                    e.Handled = true;
                    Log.Dbg("PortalCredentialsDialog: overlay key {Key} → cancel", e.Key);
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
            Log.Dbg("PortalCredentialsDialog: Save button → confirm");
            Close(MakeResult());
        }

        private void OnCancelClicked(object sender, RoutedEventArgs e)
        {
            Log.Dbg("PortalCredentialsDialog: Cancel button → cancel");
            Close(null);
        }

        private void OnOverlayTapped(object sender, TappedRoutedEventArgs e)
        {
            Log.Dbg("PortalCredentialsDialog: overlay tapped → cancel");
            Close(null);
        }

        private PortalCredentialsResult MakeResult()
        {
            return new PortalCredentialsResult(UserBox.Text.Trim(), PassBox.Password);
        }

        private void Close(PortalCredentialsResult result)
        {
            Log.Dbg("PortalCredentialsDialog.Close: result={Result}", result == null ? "null" : "set");
            Overlay.Visibility = Visibility.Collapsed;
            Visibility = Visibility.Collapsed;
            _tcs?.TrySetResult(result);
            OnClosed?.Invoke();
        }

        public void HandleButton(Windows.System.VirtualKey key)
        {
            Log.Dbg("PortalCredentialsDialog.HandleButton: key={Key}", key);
            switch (key)
            {
                case Windows.System.VirtualKey.GamepadA:
                case Windows.System.VirtualKey.Enter:
                    if (_focusIndex == 2)
                    {
                        Log.Dbg("PortalCredentialsDialog: A on Cancel → cancel");
                        Close(null);
                    }
                    else
                    {
                        Log.Dbg("PortalCredentialsDialog: A → confirm");
                        Close(MakeResult());
                    }
                    break;
                case Windows.System.VirtualKey.GamepadB:
                case Windows.System.VirtualKey.Escape:
                    Close(null);
                    break;
            }
        }

        public void HandleDPad(Windows.System.VirtualKey key)
        {
            Log.Dbg("PortalCredentialsDialog.HandleDPad: key={Key}", key);
            if (key == Windows.System.VirtualKey.GamepadDPadUp)
                _focusIndex = (_focusIndex + 3) % 4;
            else if (key == Windows.System.VirtualKey.GamepadDPadDown)
                _focusIndex = (_focusIndex + 1) % 4;
            else
                return;
            ApplyFocus();
        }

        private void ApplyFocus()
        {
            switch (_focusIndex)
            {
                case 0:
                    UserBox.Focus(FocusState.Programmatic);
                    UserBox.SelectAll();
                    break;
                case 1:
                    PassBox.Focus(FocusState.Programmatic);
                    break;
                case 2:
                    CancelButton.Focus(FocusState.Programmatic);
                    break;
                case 3:
                    OkButton.Focus(FocusState.Programmatic);
                    break;
            }
        }
    }

    public sealed class PortalCredentialsResult
    {
        public string User { get; }
        public string Password { get; }

        public PortalCredentialsResult(string user, string password)
        {
            User = user;
            Password = password;
        }
    }
}
