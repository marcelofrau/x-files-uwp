using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Imaging;
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
        public Action ReprobeRequested;
        public bool IsVisible => Visibility == Visibility.Visible;

        public PortalSetupDialog()
        {
            this.InitializeComponent();
        }

        public void Show(string statusLine)
        {
            Log.Info("PortalSetupDialog.Show: status=\"{Status}\"", statusLine ?? "");
            Visibility = Visibility.Visible;
            Overlay.Visibility = Visibility.Visible;
            StatusText.Text = statusLine ?? "";
            RenderQr(DocsUrl);
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
            Close();
            CredentialsRequested?.Invoke();
        }

        private void OnReprobeClicked(object sender, RoutedEventArgs e)
        {
            Log.Dbg("PortalSetupDialog: re-probe button clicked");
            Close();
            ReprobeRequested?.Invoke();
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
                    Close();
                    ReprobeRequested?.Invoke();
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
            if (key == Windows.System.VirtualKey.GamepadB || key == Windows.System.VirtualKey.Escape)
                Close();
        }

        private void Close()
        {
            Overlay.Visibility = Visibility.Collapsed;
            Visibility = Visibility.Collapsed;
            OnClosed?.Invoke();
        }
    }
}
