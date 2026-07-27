using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Imaging;
using ZXing;
using ZXing.Common;

namespace XFiles.Controls
{
    public sealed partial class ShareDialog : UserControl
    {
        public Action OnClosed;
        public bool IsVisible => Visibility == Visibility.Visible;

        public ShareDialog()
        {
            this.InitializeComponent();
        }

        public void Show(string url, string title = "Shared")
        {
            Log.Info("ShareDialog.Show: url={Url} title={Title}", url ?? "(null)", title);
            Visibility = Visibility.Visible;
            Overlay.Visibility = Visibility.Visible;
            TitleText.Text = title;
            UrlText.Text = url;

            try
            {
                Log.Info("ShareDialog.Show: generating QR code");
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
                if (matrix != null)
                {
                    Log.Info("ShareDialog.Show: QR matrix {W}x{H}", matrix.Width, matrix.Height);
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
                            buffer[offset] = color;     // B
                            buffer[offset + 1] = color; // G
                            buffer[offset + 2] = color; // R
                            buffer[offset + 3] = 255;   // A
                        }
                    }
                    stream.Write(buffer, 0, buffer.Length);
                    wb.Invalidate();
                    QrImage.Source = wb;
                    Log.Info("ShareDialog.Show: QR code set on image");
                }
                else
                {
                    Log.Warn("ShareDialog.Show: QR encode returned null");
                    HintText.Text = "QR code could not be generated — use the link above";
                }
            }
            catch (Exception ex)
            {
                Log.Warn("ShareDialog.Show: QR code generation failed: {Error}", ex.Message);
                HintText.Text = "QR code could not be generated — use the link above";
            }

            CopyToClipboard(url);
        }

        private void CopyToClipboard(string text)
        {
            try
            {
                var data = new Windows.ApplicationModel.DataTransfer.DataPackage();
                data.SetText(text);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
                HintText.Text = "Link copied to clipboard — scan QR code or paste link";
            }
            catch (Exception ex)
            {
                Log.Warn("ShareDialog: clipboard copy failed: {Error}", ex.Message);
            }
        }

        public void HandleDPad(VirtualKey key)
        {
            if (!IsVisible) return;
            Log.Verb("ShareDialog.HandleDPad: key={Key}", key);
            if (key == VirtualKey.GamepadB || key == VirtualKey.Escape)
                Close();
        }

        private void OnOverlayTapped(object sender, TappedRoutedEventArgs e)
        {
            Close();
        }

        public void Close()
        {
            Overlay.Visibility = Visibility.Collapsed;
            Visibility = Visibility.Collapsed;
            OnClosed?.Invoke();
        }
    }
}
