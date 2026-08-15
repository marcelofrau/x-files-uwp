using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using XFiles.Network;

namespace XFiles.Controls
{
    public sealed partial class NetworkLocationDialog : UserControl
    {
        private TaskCompletionSource<NetworkLocationResult> _tcs;
        private NetworkProtocol _protocol = NetworkProtocol.Smb;
        private bool _isEdit;
        private bool _testing;
        private bool _protocolOpen;
        public Action OnClosed;

        // 3-step wizard: Connection (name/protocol/host) → Login (user/pass) → Folder (share).
        private const int StepCount = 3;
        private int _step;
        private int _focusIndex;

        public NetworkLocationDialog()
        {
            this.InitializeComponent();

            // Floating labels: the protocol chip follows combo focus; text fields
            // toggle via the XAML GotFocus/LostFocus/TextChanged handlers.
            ProtocolCombo.GotFocus += (s, e) => ProtocolLabel.Visibility = Visibility.Visible;
            ProtocolCombo.LostFocus += (s, e) => ProtocolLabel.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Prompts for a network location (3-step wizard). Returns null on cancel.
        /// In edit mode (isEdit) the password box left empty reports PasswordEdited=false,
        /// so the caller keeps the existing credential.
        /// </summary>
        public Task<NetworkLocationResult> ShowAsync(string title, NetworkServerConfig prefill, bool isEdit)
        {
            Log.Info("NetworkLocationDialog.ShowAsync: title=\"{Title}\" isEdit={IsEdit} host=\"{Host}\"",
                title, isEdit, prefill?.Host ?? "");

            TitleText.Text = title;
            _isEdit = isEdit;
            _protocol = prefill?.Protocol ?? NetworkProtocol.Smb;
            SelectProtocolItem();

            NameBox.Text = prefill?.DisplayName ?? "";
            HostBox.Text = prefill?.Host ?? "";
            UserBox.Text = prefill?.Username ?? "";
            ShareBox.Text = prefill?.Share ?? "";
            PassBox.Password = "";
            PassHintText.Visibility = isEdit ? Visibility.Visible : Visibility.Collapsed;
            ShowStatus(null, false);

            _tcs = new TaskCompletionSource<NetworkLocationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            Canvas.SetZIndex(this, 400);
            Visibility = Visibility.Visible;
            Overlay.Visibility = Visibility.Visible;

            GoToStep(0);
            return _tcs.Task;
        }

        /// <summary>Ordered focus targets for the current step. Fields (and the
        /// protocol combo) only — Test/Cancel/Save never take gamepad focus;
        /// A/B/Start drive steps and the X button runs Test on the last step.</summary>
        private List<Control> FocusList()
        {
            var list = new List<Control>();
            switch (_step)
            {
                case 0: list.Add(NameBox); list.Add(ProtocolCombo); list.Add(HostBox); break;
                case 1: list.Add(UserBox); list.Add(PassBox); break;
                default: list.Add(ShareBox); break;
            }
            return list;
        }

        private void GoToStep(int step)
        {
            _step = step;
            Step1Panel.Visibility = step == 0 ? Visibility.Visible : Visibility.Collapsed;
            Step2Panel.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
            Step3Panel.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
            switch (step)
            {
                case 0: StepIndicator.Text = "1 / 3 — Connection"; break;
                case 1: StepIndicator.Text = "2 / 3 — Login"; break;
                default: StepIndicator.Text = "3 / 3 — Folder"; break;
            }
            // Test lives on the last step only; the primary button reads "Next"
            // while there are steps ahead, "Save" on the last one.
            TestButton.Visibility = step == StepCount - 1 ? Visibility.Visible : Visibility.Collapsed;
            OkButtonText.Text = step == StepCount - 1 ? "Save" : "Next";
            RefreshUrlPreview();
            _focusIndex = 0;
            ApplyFocus();
        }

        /// <summary>Advances to the next step, or saves on the last one. Host is
        /// required before leaving the Connection step.</summary>
        private void NextStep()
        {
            if (_step == 0 && string.IsNullOrWhiteSpace(HostBox.Text))
            {
                Log.Dbg("NetworkLocationDialog: host empty — staying on step 1");
                ShowStatus("IP / Host is required.", true);
                _focusIndex = 2;
                ApplyFocus();
                return;
            }
            if (_step < StepCount - 1)
            {
                Log.Dbg("NetworkLocationDialog: Next → step {Step}", _step + 2);
                GoToStep(_step + 1);
            }
            else
            {
                ConfirmSave();
            }
        }

        private void SelectProtocolItem()
        {
            for (int i = 0; i < ProtocolCombo.Items.Count; i++)
            {
                if (ProtocolCombo.Items[i] is ComboBoxItem item
                    && item.Tag is string tag
                    && tag == _protocol.ToString())
                {
                    ProtocolCombo.SelectedIndex = i;
                    return;
                }
            }
            ProtocolCombo.SelectedIndex = 0;
        }

        private void ShowStatus(string text, bool isError)
        {
            if (string.IsNullOrEmpty(text))
            {
                StatusText.Visibility = Visibility.Collapsed;
                return;
            }
            StatusText.Text = text;
            StatusText.Foreground = isError
                ? new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.IndianRed)
                : new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.LightSeaGreen);
            StatusText.Visibility = Visibility.Visible;
        }

        private void OnTextBoxGotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox box)
            {
                box.SelectAll();
                LabelFor(box).Visibility = Visibility.Visible;
            }
            else if (sender is PasswordBox pass)
            {
                LabelFor(pass).Visibility = Visibility.Visible;
            }
        }

        private void OnTextBoxLostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox box)
                LabelFor(box).Visibility = box.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            else if (sender is PasswordBox pass)
                LabelFor(pass).Visibility = pass.Password.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox box)
            {
                LabelFor(box).Visibility = box.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                if (box == HostBox || box == UserBox || box == ShareBox)
                    RefreshUrlPreview();
            }
        }

        private void OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            PassLabel.Visibility = PassBox.Password.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private Border LabelFor(Control control)
        {
            if (control == NameBox) return NameLabel;
            if (control == HostBox) return HostLabel;
            if (control == UserBox) return UserLabel;
            if (control == PassBox) return PassLabel;
            if (control == ShareBox) return ShareLabel;
            return ProtocolLabel;
        }

        private void RefreshUrlPreview()
        {
            string host = HostBox.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(host))
            {
                UrlPreviewText.Text = "";
                return;
            }
            string url = "smb://";
            string user = UserBox.Text?.Trim() ?? "";
            if (!string.IsNullOrEmpty(user))
                url += user + "@";
            url += host;
            string share = ShareBox.Text?.Trim() ?? "";
            if (!string.IsNullOrEmpty(share))
                url += "/" + share;
            UrlPreviewText.Text = url;
        }

        private void OnTextFieldKeyDown(object sender, KeyRoutedEventArgs e)
        {
            switch (e.Key)
            {
                case VirtualKey.Enter:
                case VirtualKey.GamepadMenu:
                    e.Handled = true;
                    Log.Dbg("NetworkLocationDialog: field key {Key} → next component", e.Key);
                    MoveFocusNext();
                    break;
                case VirtualKey.Escape:
                case VirtualKey.GamepadB:
                    e.Handled = true;
                    HandleBack();
                    break;
            }
        }

        private void MoveFocusNext()
        {
            var list = FocusList();
            if (_focusIndex < 0)
            {
                // Keyboard was closed: Start re-opens the first field.
                _focusIndex = 0;
                ApplyFocus();
                return;
            }
            if (_focusIndex < list.Count - 1)
            {
                _focusIndex++;
                ApplyFocus();
            }
            else
            {
                // Last field: drop focus so the OSK closes; from here on A and
                // B drive the steps (advance / back).
                Log.Dbg("NetworkLocationDialog: last field — closing keyboard");
                _focusIndex = -1;
                UnfocusAll();
            }
        }

        private void UnfocusAll()
        {
            // Focusing a non-text control dismisses the on-screen keyboard and
            // keeps focus inside the dialog, so A/B take over the steps here.
            FocusSink.Focus(FocusState.Programmatic);
            try
            {
                Windows.UI.ViewManagement.InputPane.GetForCurrentView().TryHide();
            }
            catch { }
        }

        private void OnOverlayKeyDown(object sender, KeyRoutedEventArgs e)
        {
            switch (e.Key)
            {
                case VirtualKey.GamepadA:
                case VirtualKey.Enter:
                    e.Handled = true;
                    Log.Dbg("NetworkLocationDialog: overlay key {Key} → next step", e.Key);
                    NextStep();
                    break;
                case VirtualKey.GamepadB:
                case VirtualKey.Escape:
                    e.Handled = true;
                    HandleBack();
                    break;
                case VirtualKey.GamepadMenu:
                    e.Handled = true;
                    MoveFocusNext();
                    break;
                case VirtualKey.GamepadDPadUp:
                case VirtualKey.GamepadDPadDown:
                case VirtualKey.GamepadDPadLeft:
                case VirtualKey.GamepadDPadRight:
                case VirtualKey.Up:
                case VirtualKey.Down:
                case VirtualKey.Left:
                case VirtualKey.Right:
                case VirtualKey.GamepadX:
                case VirtualKey.GamepadY:
                case VirtualKey.GamepadView:
                case VirtualKey.GamepadLeftShoulder:
                case VirtualKey.GamepadRightShoulder:
                    e.Handled = true;
                    break;
            }
        }

        private void OnProtocolSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProtocolCombo.SelectedItem is ComboBoxItem item
                && item.Tag is string tag
                && Enum.TryParse<NetworkProtocol>(tag, out var parsed))
            {
                _protocol = parsed;
            }
        }

        private void OnProtocolDropDownClosed(object sender, object e)
        {
            _protocolOpen = false;
            ApplyFocus();
        }

        private void OnOkClicked(object sender, RoutedEventArgs e)
        {
            Log.Dbg("NetworkLocationDialog: {Label} button → {Action}",
                OkButtonText.Text, _step < StepCount - 1 ? "next step" : "save");
            NextStep();
        }

        private void OnCancelClicked(object sender, RoutedEventArgs e)
        {
            Log.Dbg("NetworkLocationDialog: Cancel button → cancel");
            Close(null);
        }

        private void OnTestClicked(object sender, RoutedEventArgs e)
        {
            Log.Dbg("NetworkLocationDialog: Test button clicked");
            _ = RunTestAsync();
        }

        private async Task RunTestAsync()
        {
            string host = HostBox.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(host))
            {
                ShowStatus("Enter the IP / Host first.", true);
                GoToStep(0);
                _focusIndex = 2;
                ApplyFocus();
                return;
            }

            var config = BuildConfig();
            string password = PassBox.Password;
            if (password.Length == 0 && _isEdit)
            {
                try { password = await NetworkServerManager.GetPasswordAsync(config) ?? ""; }
                catch { /* vault miss → treat as empty */ }
            }

            _testing = true;
            TestButton.IsEnabled = false;
            ShowStatus("Testing connection…", false);
            try
            {
                using (var session = new SmbSession(config))
                {
                    await session.EnsureConnectedAsync(password, CancellationToken.None);
                    var shares = await session.ListSharesAsync(CancellationToken.None);
                    if (!string.IsNullOrEmpty(config.Share))
                    {
                        var entries = await session.ListDirectoryAsync(config.Share, "", CancellationToken.None);
                        ShowStatus($"Connected — share \"{config.Share}\" OK ({entries.Count} items).", false);
                    }
                    else
                    {
                        ShowStatus($"Connected — {shares.Count} share(s) found.", false);
                    }
                }
            }
            catch (NetworkOperationException ex)
            {
                Log.Warn("NetworkLocationDialog: test failed {Reason}", ex.Reason);
                ShowStatus(NetworkOperationException.FriendlyMessage(ex.Reason, ex.Message), true);
            }
            catch (Exception ex)
            {
                Log.Warn("NetworkLocationDialog: test failed unexpectedly", ex);
                ShowStatus("Test failed: " + ex.Message, true);
            }
            finally
            {
                _testing = false;
                TestButton.IsEnabled = true;
            }
        }

        private void ConfirmSave()
        {
            if (_testing) return;

            if (string.IsNullOrWhiteSpace(HostBox.Text))
            {
                Log.Dbg("NetworkLocationDialog: host empty — going to step 1");
                ShowStatus("IP / Host is required.", true);
                GoToStep(0);
                _focusIndex = 2;
                ApplyFocus();
                return;
            }

            Close(MakeResult());
        }

        private NetworkServerConfig BuildConfig()
        {
            return new NetworkServerConfig
            {
                Protocol = _protocol,
                DisplayName = string.IsNullOrWhiteSpace(NameBox.Text) ? null : NameBox.Text.Trim(),
                Host = HostBox.Text.Trim(),
                Username = string.IsNullOrWhiteSpace(UserBox.Text) ? null : UserBox.Text.Trim(),
                Share = string.IsNullOrWhiteSpace(ShareBox.Text) ? null : ShareBox.Text.Trim()
            };
        }

        private NetworkLocationResult MakeResult()
        {
            var config = BuildConfig();
            string password = PassBox.Password;
            return new NetworkLocationResult(config, password, !string.IsNullOrEmpty(password));
        }

        private void HandleBack()
        {
            if (_step > 0)
            {
                Log.Dbg("NetworkLocationDialog: B → previous step");
                GoToStep(_step - 1);
            }
            else
            {
                Log.Dbg("NetworkLocationDialog: B at first step → cancel");
                Close(null);
            }
        }

        private void Close(NetworkLocationResult result)
        {
            Log.Dbg("NetworkLocationDialog.Close: result={Result}", result == null ? "null" : "set");
            Overlay.Visibility = Visibility.Collapsed;
            Visibility = Visibility.Collapsed;
            _tcs?.TrySetResult(result);
            OnClosed?.Invoke();
        }

        public void HandleButton(VirtualKey key)
        {
            Log.Dbg("NetworkLocationDialog.HandleButton: key={Key} focus={Focus}",
                key, _focusIndex);
            var list = FocusList();
            var focused = _focusIndex >= 0 && _focusIndex < list.Count ? list[_focusIndex] : null;

            switch (key)
            {
                case VirtualKey.GamepadA:
                case VirtualKey.Enter:
                    if (focused == ProtocolCombo)
                    {
                        // Smart A on the combo: open (or close) the dropdown
                        // flyout for selection — never advance the step.
                        if (_protocolOpen)
                        {
                            Log.Dbg("NetworkLocationDialog: A closes protocol dropdown");
                            CloseProtocolDropdown();
                        }
                        else
                        {
                            Log.Dbg("NetworkLocationDialog: A opens protocol dropdown");
                            _protocolOpen = true;
                            ProtocolCombo.IsDropDownOpen = true;
                        }
                    }
                    else if (focused != null)
                    {
                        // Field already focused (keyboard up): A stays in input
                        // mode. Select all so typing replaces the value.
                        Log.Dbg("NetworkLocationDialog: A on field → input");
                        if (focused is TextBox box) box.SelectAll();
                    }
                    else
                    {
                        // Keyboard closed: A advances the step (or saves on the
                        // last one).
                        Log.Dbg("NetworkLocationDialog: A → next step");
                        NextStep();
                    }
                    break;
                case VirtualKey.GamepadMenu:
                    // Start: next field; after the last field the keyboard
                    // closes and A/B take over the steps.
                    Log.Dbg("NetworkLocationDialog: Start → next component");
                    MoveFocusNext();
                    break;
                case VirtualKey.GamepadB:
                case VirtualKey.Escape:
                    if (_protocolOpen)
                    {
                        CloseProtocolDropdown();
                    }
                    else if (focused != null)
                    {
                        // Keyboard up: B closes it first; a second B backs up.
                        Log.Dbg("NetworkLocationDialog: B closes keyboard");
                        _focusIndex = -1;
                        UnfocusAll();
                    }
                    else
                    {
                        HandleBack();
                    }
                    break;
                case VirtualKey.GamepadX:
                    // X runs Test, available on the last step only (the Test
                    // button itself stays mouse/touch-only).
                    if (_step == StepCount - 1)
                    {
                        Log.Dbg("NetworkLocationDialog: X → test connection");
                        _ = RunTestAsync();
                    }
                    break;
                // GamepadY/View and the shoulder buttons are consumed here
                // (no-op) so they never leak to the page behind the modal.
            }
        }

        private void CloseProtocolDropdown()
        {
            _protocolOpen = false;
            ProtocolCombo.IsDropDownOpen = false;
            ApplyFocus();
        }

        public void HandleDPad(VirtualKey key)
        {
            Log.Dbg("NetworkLocationDialog.HandleDPad: key={Key}", key);

            // While the protocol dropdown is open, D-pad moves the selection.
            if (_protocolOpen)
            {
                int idx = ProtocolCombo.SelectedIndex;
                if (key == VirtualKey.GamepadDPadUp || key == VirtualKey.Up)
                    idx = Math.Max(0, idx - 1);
                else if (key == VirtualKey.GamepadDPadDown || key == VirtualKey.Down)
                    idx = Math.Min(ProtocolCombo.Items.Count - 1, idx + 1);
                else
                    return;
                ProtocolCombo.SelectedIndex = idx;
                return;
            }

            // Keyboard closed: D-pad is inert — steps run on A/B only.
            if (_focusIndex < 0) return;

            var list = FocusList();
            if (key == VirtualKey.GamepadDPadUp || key == VirtualKey.Up)
                _focusIndex = (_focusIndex + list.Count - 1) % list.Count;
            else if (key == VirtualKey.GamepadDPadDown || key == VirtualKey.Down)
                _focusIndex = (_focusIndex + 1) % list.Count;
            else
                return;
            ApplyFocus();
        }

        private void ApplyFocus()
        {
            var list = FocusList();
            if (_focusIndex < 0 || _focusIndex >= list.Count) return;
            var control = list[_focusIndex];
            control.Focus(FocusState.Programmatic);
            if (control is TextBox box)
                box.SelectAll();
        }
    }

    public sealed class NetworkLocationResult
    {
        public NetworkServerConfig Config { get; }
        public string Password { get; }
        public bool PasswordEdited { get; }

        public NetworkLocationResult(NetworkServerConfig config, string password, bool passwordEdited)
        {
            Config = config;
            Password = password;
            PasswordEdited = passwordEdited;
        }
    }
}
