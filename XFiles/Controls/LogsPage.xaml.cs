using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using SharpCompress.Archives;
using SharpCompress.Common;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace XFiles.Controls
{
    public sealed partial class LogsPage : UserControl
    {
        public Action<string> OnShareRequested;
        public Action OnClosed;

        public bool IsVisible => Visibility == Visibility.Visible;

        public LogsPage()
        {
            this.InitializeComponent();
        }

        public void Show()
        {
            Log.Info("LogsPage.Show: setting visibility, reading logs");
            Visibility = Visibility.Visible;
            Overlay.Visibility = Visibility.Visible;
            LoadingText.Visibility = Visibility.Visible;
            LogScroll.Visibility = Visibility.Collapsed;
            StatusText.Text = "";
            StatusBadge.Visibility = Visibility.Collapsed;
            FooterStatusText.Text = "";

            LoadLogs();
            Log.Info("LogsPage.Show: done, IsVisible={V}", IsVisible);
        }

        private const int MaxLogChars = 500 * 1024; // 500KB display limit

        private async void LoadLogs()
        {
            try
            {
                Log.Info("LogsPage.LoadLogs: start");
                string rawContent = await Task.Run(() => Log.GetAllLogContent());
                Log.Info("LogsPage.LoadLogs: read {Len} chars", rawContent?.Length ?? 0);

                string content;
                bool truncated = false;
                if (rawContent != null && rawContent.Length > MaxLogChars)
                {
                    content = rawContent.Substring(rawContent.Length - MaxLogChars);
                    truncated = true;
                    Log.Info("LogsPage.LoadLogs: truncated to last {Max} chars", MaxLogChars);
                }
                else
                {
                    content = rawContent ?? "";
                }

                Log.Info("LogsPage.LoadLogs: setting LogText.Text ({Len} chars)", content.Length);
                LogText.Text = content;
                await Task.Yield();
                Log.Info("LogsPage.LoadLogs: LogText.Text set, yielding before scroll");

                string logPath = Log.GetCurrentLogPath();
                LogInfoText.Text = logPath != null
                    ? Path.GetFileName(logPath)
                    : "No log files found";
                LogLevelText.Text = $"Level: {Log.GetCurrentLevel()}";

                if (truncated)
                {
                    FooterStatusText.Text = $"Showing last {MaxLogChars / 1024}KB of {rawContent.Length / 1024}KB total";
                }

                LoadingText.Visibility = Visibility.Collapsed;
                LogScroll.Visibility = Visibility.Visible;
                LogScroll.ChangeView(null, LogScroll.ExtentHeight, null);
                Log.Info("LogsPage.LoadLogs: done, scroll position set");
            }
            catch (Exception ex)
            {
                LogText.Text = $"Error loading logs: {ex.Message}";
                LoadingText.Visibility = Visibility.Collapsed;
                LogScroll.Visibility = Visibility.Visible;
                Log.Err("LogsPage: failed to load logs", ex);
            }
        }

        public void HandleDPad(VirtualKey key)
        {
            if (!IsVisible) return;
            switch (key)
            {
                case VirtualKey.Up:
                    LogScroll.ChangeView(null, LogScroll.VerticalOffset - 40, null);
                    break;
                case VirtualKey.Down:
                    LogScroll.ChangeView(null, LogScroll.VerticalOffset + 40, null);
                    break;
                case VirtualKey.Left:
                    LogScroll.ChangeView(LogScroll.HorizontalOffset - 80, null, null);
                    break;
                case VirtualKey.Right:
                    LogScroll.ChangeView(LogScroll.HorizontalOffset + 80, null, null);
                    break;
                case VirtualKey.GamepadY:
                    ShareLog();
                    break;
                case VirtualKey.GamepadB:
                case VirtualKey.Escape:
                    Close();
                    break;
            }
        }

        public void HandleLeftStick(float x, float y)
        {
            if (!IsVisible) return;
            float deadzone = 0.18f;
            if (Math.Abs(x) < deadzone) x = 0;
            if (Math.Abs(y) < deadzone) y = 0;
            if (x == 0 && y == 0) return;

            double speed = 18.0;
            double dx = x * speed;
            double dy = y * speed;
            LogScroll.ChangeView(
                LogScroll.HorizontalOffset + dx,
                LogScroll.VerticalOffset - dy,
                null);
        }

        public void HandleRightStick(float x, float y)
        {
            if (!IsVisible) return;
            float deadzone = 0.18f;
            if (Math.Abs(x) < deadzone) x = 0;
            if (Math.Abs(y) < deadzone) y = 0;
            if (x == 0 && y == 0) return;

            double speed = 35.0;
            double dx = x * speed;
            double dy = y * speed;
            LogScroll.ChangeView(
                LogScroll.HorizontalOffset + dx,
                LogScroll.VerticalOffset - dy,
                null);
        }

        private async void ShareLog()
        {
            FooterStatusText.Text = "";
            StatusBadge.Visibility = Visibility.Collapsed;
            Log.Info("LogsPage.ShareLog: starting");

            try
            {
                ShowUploadProgress("Preparing logs...");
                Log.Info("LogsPage.ShareLog: reading all sessions");
                string logContent = await Task.Run(() => Log.GetAllSessionsContent());
                Log.Info("LogsPage.ShareLog: got {Len} chars, uploading", logContent?.Length ?? 0);

                string responseBody = await UploadLogFile(logContent);
                Log.Info("LogsPage.ShareLog: raw response: {Body}", responseBody ?? "(null)");

                string url = ExtractUrlFromResponse(responseBody);
                Log.Info("LogsPage.ShareLog: extracted URL: {Url}", url ?? "(null)");

                HideUploadProgress();

                if (!string.IsNullOrEmpty(url))
                {
                    Log.Info("LogsPage: upload successful, URL: {Url}", url);
                    StatusBadge.Background = new Windows.UI.Xaml.Media.SolidColorBrush(
                        Windows.UI.Color.FromArgb(255, 147, 196, 60));
                    StatusText.Text = "Shared";
                    StatusBadge.Visibility = Visibility.Visible;
                    FooterStatusText.Text = url;
                    OnShareRequested?.Invoke(url);
                }
                else
                {
                    Log.Warn("LogsPage: upload returned empty URL");
                    StatusBadge.Background = new Windows.UI.Xaml.Media.SolidColorBrush(
                        Windows.UI.Color.FromArgb(255, 200, 80, 80));
                    StatusText.Text = "Failed";
                    StatusBadge.Visibility = Visibility.Visible;
                    FooterStatusText.Text = "Upload failed";
                }
            }
            catch (Exception ex)
            {
                HideUploadProgress();
                Log.Warn("LogsPage: upload failed: {Error}", ex.Message);
                StatusBadge.Background = new Windows.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 200, 80, 80));
                StatusText.Text = "Failed";
                StatusBadge.Visibility = Visibility.Visible;
                FooterStatusText.Text = "Upload failed";
            }
        }

        private void ShowUploadProgress(string status)
        {
            UploadStatusText.Text = status;
            UploadProgressBar.IsIndeterminate = true;
            UploadProgressPanel.Visibility = Visibility.Visible;
        }

        private void HideUploadProgress()
        {
            UploadProgressPanel.Visibility = Visibility.Collapsed;
        }

        private async Task<string> UploadLogFile(string content)
        {
            Log.Info("LogsPage.UploadLogFile: content {Len} chars, uploading to gofile", content?.Length ?? 0);
            try
            {
                using (var client = new HttpClient())
                {
                    ShowUploadProgress("Getting server...");
                    Log.Info("LogsPage.UploadLogFile: step 1 - get server");
                    var serverResp = await client.GetStringAsync("https://api.gofile.io/servers");
                    Log.Info("LogsPage.UploadLogFile: server response: {Resp}", serverResp);

                    var serverJson = Windows.Data.Json.JsonObject.Parse(serverResp);
                    var data = serverJson.GetNamedObject("data");
                    var servers = data.GetNamedArray("servers");
                    string server = servers.GetObjectAt(0).GetNamedString("name");
                    Log.Info("LogsPage.UploadLogFile: using server {Server}", server);

                    ShowUploadProgress("Compressing...");
                    byte[] zipBytes;
                    using (var archive = SharpCompress.Archives.Zip.ZipArchive.Create())
                    {
                        var plainBytes = System.Text.Encoding.UTF8.GetBytes(content);
                        archive.AddEntry($"xfiles-logs-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
                            new MemoryStream(plainBytes), plainBytes.Length);

                        using (var zipStream = new MemoryStream())
                        {
                            archive.SaveTo(zipStream, new SharpCompress.Writers.WriterOptions(CompressionType.Deflate));
                            zipBytes = zipStream.ToArray();
                        }
                    }
                    Log.Info("LogsPage.UploadLogFile: {Len} chars → {Zip}B zip",
                        content.Length, zipBytes.Length);

                    ShowUploadProgress($"Uploading {zipBytes.Length / 1024} KB...");
                    var form = new MultipartFormDataContent();
                    var fileContent = new ByteArrayContent(zipBytes);
                    fileContent.Headers.Add("Content-Type", "application/zip");
                    form.Add(fileContent, "file", $"xfiles-logs-{DateTime.Now:yyyyMMdd-HHmmss}.zip");

                    string uploadUrl = $"https://{server}.gofile.io/contents/uploadfile";
                    Log.Info("LogsPage.UploadLogFile: step 2 - upload {KB} KB to {Url}", zipBytes.Length / 1024, uploadUrl);
                    var resp = await client.PostAsync(uploadUrl, form);
                    string responseBody = await resp.Content.ReadAsStringAsync();
                    Log.Info("LogsPage.UploadLogFile: status={Status}, body={Body}", resp.StatusCode, responseBody);
                    return responseBody?.Trim();
                }
            }
            catch (Exception ex)
            {
                Log.Warn("LogsPage.UploadLogFile: failed: {Error}", ex.Message);
                if (ex.InnerException != null)
                    Log.Warn("LogsPage.UploadLogFile: inner: {Inner}", ex.InnerException.Message);
                if (ex.InnerException?.InnerException != null)
                    Log.Warn("LogsPage.UploadLogFile: inner2: {Inner2}", ex.InnerException.InnerException.Message);
                return null;
            }
        }

        private static string ExtractUrlFromResponse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                var obj = Windows.Data.Json.JsonObject.Parse(json);

                // gofile: { "data": { "downloadPage": "https://..." } }
                if (obj.ContainsKey("data") && obj["data"].ValueType == Windows.Data.Json.JsonValueType.Object)
                {
                    var data = obj.GetNamedObject("data");
                    if (data.ContainsKey("downloadPage"))
                        return data.GetNamedString("downloadPage");
                }

                // litterbox: { "url": "..." } or { "link": "..." }
                if (obj.ContainsKey("url") && obj["url"].ValueType != Windows.Data.Json.JsonValueType.Null)
                    return obj["url"].GetString();
                if (obj.ContainsKey("link") && obj["link"].ValueType != Windows.Data.Json.JsonValueType.Null)
                    return obj["link"].GetString();
            }
            catch { }
            if (json.StartsWith("http"))
                return json.Trim();
            return null;
        }

        public void Close()
        {
            Visibility = Visibility.Collapsed;
            Overlay.Visibility = Visibility.Collapsed;
            OnClosed?.Invoke();
        }
    }
}
