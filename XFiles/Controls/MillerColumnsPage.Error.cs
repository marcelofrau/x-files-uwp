using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Data.Json;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.System;
using Windows.System.Display;
using XFiles.Audio;
using XFiles.FileSystem;
using XFiles.Metadata;
using XFiles.Navigation;
using XFiles.Services;
using XFiles.Visualizers;


namespace XFiles.Controls
{
    public sealed partial class MillerColumnsPage
    {
        public void ShowError(string title, string description, string details)
        {
            ErrorTitleText.Text = title;
            ErrorDescriptionText.Text = description;
            ErrorDetailsText.Text = details;
            ErrorOverlay.Visibility = Visibility.Visible;
            ErrorOverlay.Opacity = 0;

            // Reset share state
            ErrorShareText.Text = "Share";
            ErrorUploadProgress.Visibility = Visibility.Collapsed;
            BtnErrorShare.IsEnabled = true;

            var fadeIn = new DoubleAnimation { To = 1.0, Duration = new Duration(TimeSpan.FromMilliseconds(200)) };
            Storyboard.SetTarget(fadeIn, ErrorOverlay);
            Storyboard.SetTargetProperty(fadeIn, "Opacity");
            var sb = new Storyboard();
            sb.Children.Add(fadeIn);
            sb.Begin();

            _lastErrorText = $"[{title}] {description}\n\n{details}";
            Log.Warn("Error overlay shown: {Title} — {Description}", title, description);
        }

        private void HideError()
        {
            ErrorOverlay.Visibility = Visibility.Collapsed;
            ErrorUploadProgress.Visibility = Visibility.Collapsed;
        }

        private void OnErrorCloseClick(object sender, RoutedEventArgs e)
        {
            HideError();
        }

        private void OnErrorCopyClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
                dp.SetText(_lastErrorText);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
                Log.Info("Error details copied to clipboard");
            }
            catch (Exception ex)
            {
                Log.Warn("Failed to copy error to clipboard", ex);
            }
        }

        private async void OnErrorShareClick(object sender, RoutedEventArgs e)
        {
            try
            {
                BtnErrorShare.IsEnabled = false;
                ErrorShareText.Text = "Uploading...";
                ErrorUploadProgress.Visibility = Visibility.Visible;
                Log.Info("ErrorShare: starting upload");

                // 1. Get gofile server
                ErrorUploadStatus.Text = "Getting server...";
                var serverResp = await _errorShareClient.GetStringAsync("https://api.gofile.io/servers");
                var serverJson = JsonObject.Parse(serverResp);
                string server = serverJson.GetNamedObject("data")
                    .GetNamedArray("servers").GetObjectAt(0).GetNamedString("name");
                Log.Info("ErrorShare: server={Server}", server);

                // 2. Build zip: error.txt + current session log
                ErrorUploadStatus.Text = "Compressing...";
                byte[] zipBytes;
                string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                using (var archive = SharpCompress.Archives.Zip.ZipArchive.Create())
                {
                    var errorBytes = System.Text.Encoding.UTF8.GetBytes(_lastErrorText);
                    archive.AddEntry($"error-{timestamp}.txt",
                        new MemoryStream(errorBytes), errorBytes.Length);

                    string logContent = await Task.Run(() => Log.GetAllSessionsContent());
                    if (!string.IsNullOrEmpty(logContent))
                    {
                        var logBytes = System.Text.Encoding.UTF8.GetBytes(logContent);
                        archive.AddEntry($"xfiles-logs-{timestamp}.txt",
                            new MemoryStream(logBytes), logBytes.Length);
                    }

                    using (var zipStream = new MemoryStream())
                    {
                        archive.SaveTo(zipStream,
                            new SharpCompress.Writers.WriterOptions(SharpCompress.Common.CompressionType.Deflate));
                        zipBytes = zipStream.ToArray();
                    }
                }
                Log.Info("ErrorShare: zip={KB} KB", zipBytes.Length / 1024);

                // 3. Upload
                ErrorUploadStatus.Text = $"Uploading {zipBytes.Length / 1024} KB...";
                var form = new MultipartFormDataContent();
                var fileContent = new ByteArrayContent(zipBytes);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
                form.Add(fileContent, "file", $"xfiles-error-{timestamp}.zip");

                string uploadUrl = $"https://{server}.gofile.io/contents/uploadfile";
                Log.Info("ErrorShare: uploading to {Url}", uploadUrl);
                var resp = await _errorShareClient.PostAsync(uploadUrl, form);
                string responseBody = await resp.Content.ReadAsStringAsync();
                Log.Info("ErrorShare: status={Status}", resp.StatusCode);

                // 4. Parse URL
                string downloadUrl = null;
                var respJson = JsonObject.Parse(responseBody);
                if (respJson.ContainsKey("data") && respJson["data"].ValueType == JsonValueType.Object)
                {
                    var data = respJson.GetNamedObject("data");
                    if (data.ContainsKey("downloadPage"))
                        downloadUrl = data.GetNamedString("downloadPage");
                }

                ErrorUploadProgress.Visibility = Visibility.Collapsed;

                if (!string.IsNullOrEmpty(downloadUrl))
                {
                    Log.Info("ErrorShare: success, URL={Url}", downloadUrl);
                    HideError();
                    ShareDialogControl.Show(downloadUrl, "Error Shared");
                }
                else
                {
                    Log.Warn("ErrorShare: upload returned no URL");
                    ErrorShareText.Text = "Failed";
                    BtnErrorShare.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("ErrorShare: upload failed: {Error}", ex.Message);
                ErrorUploadProgress.Visibility = Visibility.Collapsed;
                ErrorShareText.Text = "Failed";
                BtnErrorShare.IsEnabled = true;
            }
        }
    }
}
