using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Imaging;
using XFiles.Services;
using ZXing;
using ZXing.Common;

namespace XFiles.Controls
{
    public sealed partial class PortalSetupDialog : UserControl
    {
        public const string DocsUrl =
            "https://github.com/marcelofrau/x-files-uwp/blob/main/docs/PORTAL-APPDATA.md";

        public Action OnClosed;
        public Action CredentialsRequested;
        public Action ResetCredentialsRequested;
        public event Action Connected;
        public bool IsVisible => Visibility == Visibility.Visible;

        private readonly DispatcherTimer _successTimer;

        public PortalSetupDialog()
        {
            this.InitializeComponent();
            _successTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            _successTimer.Tick += (s, e) => { _successTimer.Stop(); Close(); };
        }

        public void Show(string statusLine, string autoProbeMessage = null)
        {
            Log.Info("PortalSetupDialog.Show: status=\"{Status}\"", statusLine ?? "");
            _successTimer.Stop();
            Visibility = Visibility.Visible;
            Overlay.Visibility = Visibility.Visible;
            ReprobeResultText.Text = "";
            ReprobeProgress.Visibility = Visibility.Collapsed;
            SetButtonsEnabled(true);
            UpdateCredentialsButton();
            RenderQr(DocsUrl);

            // Auto-retry: same legal check the Retry button performs, started
            // automatically so the modal opens already probing (feedback in place).
            if (!DevicePortalService.IsPortalConnected)
                StartReprobe(autoProbeMessage ?? "Probing portal…");
        }

        private void RenderQr(string url)
        {
            try
            {
                var writer = new BarcodeWriterGeneric
                {
                    Format = BarcodeFormat.QR_CODE,
                    Options = new EncodingOptions
                    {
                        Width = 250,
                        Height = 250,
                        Margin = 1
                    }
                };
                var matrix = writer.Encode(url);
                if (matrix == null)
                {
                    Log.Warn("PortalSetupDialog: QR encode returned null");
                    return;
                }
                int w = matrix.Width;
                int h = matrix.Height;
                var wb = new WriteableBitmap(w, h);
                var stream = wb.PixelBuffer.AsStream();
                var buffer = new byte[w * h * 4];
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        bool isBlack = matrix[x, y];
                        int offset = (y * w + x) * 4;
                        byte color = isBlack ? (byte)0 : (byte)255;
                        buffer[offset] = color;
                        buffer[offset + 1] = color;
                        buffer[offset + 2] = color;
                        buffer[offset + 3] = 255;
                    }
                }
                stream.Write(buffer, 0, buffer.Length);
                wb.Invalidate();
                QrImage.Source = wb;
                Log.Info("PortalSetupDialog: QR set ({W}x{H})", w, h);
            }
            catch (Exception ex)
            {
                Log.Warn("PortalSetupDialog: QR generation failed: {Error}", ex.Message);
            }
        }

        private void OnCredentialsClicked(object sender, RoutedEventArgs e)
        {
            Log.Dbg("PortalSetupDialog: credentials button clicked");
            if (DevicePortalService.HasCredentials)
            {
                Log.Dbg("PortalSetupDialog: creds present → reset flow");
                ResetCredentialsRequested?.Invoke();
                return;
            }
            StartCredentials();
        }

        private void OnReprobeClicked(object sender, RoutedEventArgs e)
        {
            Log.Dbg("PortalSetupDialog: re-probe button clicked");
            StartReprobe();
        }

        private void OnCloseClicked(object sender, RoutedEventArgs e)
        {
            Log.Dbg("PortalSetupDialog: close button clicked");
            Close();
        }

        private void OnOverlayTapped(object sender, TappedRoutedEventArgs e)
        {
            Log.Dbg("PortalSetupDialog: overlay tapped → close");
            Close();
        }

        private void OnOverlayKeyDown(object sender, KeyRoutedEventArgs e)
        {
            switch (e.Key)
            {
                case Windows.System.VirtualKey.GamepadB:
                case Windows.System.VirtualKey.Escape:
                    e.Handled = true;
                    Log.Dbg("PortalSetupDialog: key {Key} → close", e.Key);
                    Close();
                    break;
                case Windows.System.VirtualKey.GamepadY:
                    e.Handled = true;
                    Log.Dbg("PortalSetupDialog: Y → re-probe");
                    StartReprobe();
                    break;
                default:
                    e.Handled = true;
                    break;
            }
        }

        public void HandleDPad(Windows.System.VirtualKey key)
        {
            // Buttons are natively focusable; gamepad A/B handled at overlay level.
        }

        public void HandleButton(Windows.System.VirtualKey key)
        {
            Log.Verb("PortalSetupDialog.HandleButton: key={Key}", key);
            switch (key)
            {
                case Windows.System.VirtualKey.GamepadA:
                case Windows.System.VirtualKey.Enter:
                    if (DevicePortalService.HasCredentials)
                        ResetCredentialsRequested?.Invoke();
                    else
                        StartCredentials();
                    break;
                case Windows.System.VirtualKey.GamepadY:
                    StartReprobe();
                    break;
                case Windows.System.VirtualKey.GamepadB:
                case Windows.System.VirtualKey.Escape:
                    Close();
                    break;
            }
        }

        private void StartCredentials()
        {
            Close();
            CredentialsRequested?.Invoke();
        }

        /// <summary>
        /// Starts a forced portal probe with on-screen feedback. The modal must be
        /// visible first (call Show() or open via the router). Safe to call from the
        /// credentials flow — the connecting state is shown right where the user acts.
        /// </summary>
        public void StartReprobe(string message = "Probing portal…")
        {
            if (ReprobeProgress.Visibility == Visibility.Visible)
            {
                Log.Dbg("PortalSetupDialog: probe already running, ignoring re-probe");
                return;
            }
            Log.Info("PortalSetupDialog: re-probing Device Portal");
            ReprobeProgress.Visibility = Visibility.Visible;
            ReprobeResultText.Text = message;
            ReprobeResultText.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 208, 176, 96));
            SetButtonsEnabled(false);
            DevicePortalService.ProbeCompleted += OnReprobeCompleted;
            DevicePortalService.ProbeAsync(force: true);
        }

        private void OnReprobeCompleted()
        {
            DevicePortalService.ProbeCompleted -= OnReprobeCompleted;
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                ReprobeProgress.Visibility = Visibility.Collapsed;
                SetButtonsEnabled(true);
                UpdateCredentialsButton();
                bool ok = DevicePortalService.IsPortalConnected;
                if (ok)
                {
                    ReprobeResultText.Text = "Portal connected: " + DevicePortalService.BaseUrl;
                    ReprobeResultText.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 148, 196, 60));
                    Log.Info("PortalSetupDialog: probe succeeded — closing");
                    Connected?.Invoke();
                    _successTimer.Start();
                }
                else
                {
                    ReprobeResultText.Text = "Probe failed: " + DevicePortalService.ProbeStatus;
                    ReprobeResultText.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 224, 102, 102));
                    Log.Warn("PortalSetupDialog: probe failed — staying open for retry");
                }
            });
        }

        private void UpdateCredentialsButton()
        {
            CredentialsBtnText.Text = DevicePortalService.HasCredentials
                ? "Reset credentials"
                : "Enter credentials";
        }

        private void SetButtonsEnabled(bool enabled)
        {
            CredentialsBtn.IsEnabled = enabled;
            ReprobeBtn.IsEnabled = enabled;
            CloseBtn.IsEnabled = enabled;
        }

        private void Close()
        {
            if (Visibility == Visibility.Collapsed)
                return;
            _successTimer.Stop();
            ReprobeProgress.Visibility = Visibility.Collapsed;
            DevicePortalService.ProbeCompleted -= OnReprobeCompleted;
            Overlay.Visibility = Visibility.Collapsed;
            Visibility = Visibility.Collapsed;
            OnClosed?.Invoke();
        }
    }
}
