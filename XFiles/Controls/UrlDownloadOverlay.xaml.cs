using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace XFiles.Controls
{
    /// <summary>
    /// Fullscreen WebView used as a fallback when a download URL leads to an HTML
    /// page (captcha, share page, Mega, unsupported resolver). The user clicks the
    /// page's download button with the injected gamepad cursor; the file navigation
    /// is intercepted via UnviewableContentIdentified / NewWindowRequested, downloaded
    /// to the current folder, and the overlay closes automatically.
    /// The overlay stays open (and captures input) while the download runs so B can
    /// cancel it.
    /// </summary>
    public sealed partial class UrlDownloadOverlay : UserControl
    {
        private string _destDir;
        private CancellationTokenSource _cts;
        private bool _downloading;
        private bool _webViewReady;
        private bool _cursorInjected;

        public bool IsOpen => Visibility == Visibility.Visible;

        /// <summary>Raised whenever the overlay closes (B button, cancel, or after a download).</summary>
        public Action OnClosed;

        /// <summary>Raised after a download finished and the overlay closed. Path of the saved file.</summary>
        public event Action<string> DownloadCompleted;

        public UrlDownloadOverlay()
        {
            this.InitializeComponent();
            DownloadWebView.NavigationStarting += OnNavigationStarting;
            DownloadWebView.NavigationCompleted += OnNavigationCompleted;
            DownloadWebView.NavigationFailed += OnNavigationFailed;
            DownloadWebView.UnviewableContentIdentified += OnUnviewableContent;
            DownloadWebView.NewWindowRequested += OnNewWindowRequested;
        }

        public void Show(string url, string destDir)
        {
            Log.Info("UrlDownloadOverlay.Show: {Url} → {Dest}", url, destDir);
            _destDir = destDir;
            _downloading = false;
            _webViewReady = false;
            _cursorInjected = false;
            _cts?.Cancel();
            _cts = null;

            TitleText.Text = "Download from URL";
            UrlText.Text = url;
            StatusBar.Visibility = Visibility.Collapsed;
            DownloadWebView.Visibility = Visibility.Visible;

            Visibility = Visibility.Visible;
            try
            {
                DownloadWebView.Navigate(new Uri(url));
            }
            catch (Exception ex)
            {
                Log.Warn("UrlDownloadOverlay.Show: invalid URL {Url}", ex, url);
                TitleText.Text = "Invalid URL";
            }
        }

        public void Close()
        {
            Log.Info("UrlDownloadOverlay.Close");
            _downloading = false;
            _webViewReady = false;
            _cts?.Cancel();
            _cts = null;
            try { DownloadWebView.NavigateToString("about:blank"); }
            catch (Exception ex) { Log.Warn("UrlDownloadOverlay.Close: blank nav failed", ex); }
            Visibility = Visibility.Collapsed;
            OnClosed?.Invoke();
        }

        // ── WebView events ─────────────────────────────────────

        private void OnNavigationStarting(WebView sender, WebViewNavigationStartingEventArgs args)
        {
            if (_downloading) args.Cancel = true;
        }

        private async void OnNavigationCompleted(WebView sender, WebViewNavigationCompletedEventArgs args)
        {
            Log.Info("UrlDownloadOverlay: navigation completed {Uri} success={Success}", args.Uri, args.IsSuccess);
            if (_downloading) return;

            if (!args.IsSuccess)
            {
                TitleText.Text = "Could not load the page";
                return;
            }

            await InjectCursorAsync();
            _webViewReady = true;
        }

        private void OnNavigationFailed(object sender, WebViewNavigationFailedEventArgs e)
        {
            Log.Warn("UrlDownloadOverlay: navigation failed {Uri} ({Status})", e.Uri, e.WebErrorStatus);
            if (_downloading) return;
            TitleText.Text = "Could not load the page";
        }

        private async void OnUnviewableContent(WebView sender, WebViewUnviewableContentIdentifiedEventArgs args)
        {
            Log.Info("UrlDownloadOverlay: unviewable content {Uri}", args.Uri);
            if (_downloading) return;
            await StartDownloadAsync(args.Uri.ToString());
        }

        private async void OnNewWindowRequested(WebView sender, WebViewNewWindowRequestedEventArgs args)
        {
            Log.Info("UrlDownloadOverlay: new window requested {Uri}", args.Uri);
            args.Handled = true;
            if (_downloading) return;
            // Navigate the same view: a file URL → UnviewableContentIdentified →
            // download; a page URL → shown inline.
            DownloadWebView.Navigate(args.Uri);
        }

        // ── Download capture ───────────────────────────────────

        private async Task StartDownloadAsync(string fileUrl)
        {
            if (_downloading) return;
            _downloading = true;
            _cts = new CancellationTokenSource();

            Log.Info("UrlDownloadOverlay: starting download {Url}", fileUrl);
            TitleText.Text = "Downloading";
            DownloadWebView.Visibility = Visibility.Collapsed;
            StatusBar.Visibility = Visibility.Visible;
            DownloadProgress.IsIndeterminate = true;
            DownloadProgress.Value = 0;
            StatusText.Text = "Downloading...";

            var result = await XFiles.Services.DownloadService.TryDownloadAsync(
                fileUrl, _destDir,
                (copied, total) =>
                {
                    _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => UpdateProgress(copied, total));
                },
                _cts.Token);

            if (result.Outcome == XFiles.Services.DownloadService.DownloadOutcome.Downloaded)
            {
                StatusText.Text = "Saved: " + System.IO.Path.GetFileName(result.SavedPath);
                DownloadProgress.IsIndeterminate = false;
                DownloadProgress.Value = 100;
                await Task.Delay(800);
                string savedPath = result.SavedPath;
                DownloadCompleted?.Invoke(savedPath);
                Close();
            }
            else if (result.Outcome == XFiles.Services.DownloadService.DownloadOutcome.Canceled)
            {
                Log.Info("UrlDownloadOverlay: download cancelled");
                Close();
            }
            else
            {
                _downloading = false;
                Log.Warn("UrlDownloadOverlay: download failed ({Error})", result.Error ?? "unknown");
                TitleText.Text = "Download failed";
                StatusText.Text = string.IsNullOrEmpty(result.Error)
                    ? "The link still leads to a page. Try clicking the download button, then press B."
                    : $"Download failed: {result.Error}";
                DownloadProgress.IsIndeterminate = false;
                DownloadProgress.Value = 0;
                // Restore the page so the user can retry or back out.
                DownloadWebView.Visibility = Visibility.Visible;
                StatusBar.Visibility = Visibility.Collapsed;
                _cts = null;
            }
        }

        private void UpdateProgress(long copied, long total)
        {
            if (total > 0)
            {
                DownloadProgress.IsIndeterminate = false;
                DownloadProgress.Value = Math.Max(0, Math.Min(100, (double)copied / total * 100));
                StatusText.Text = $"Downloading... {FormatBytes(copied)} / {FormatBytes(total)}";
            }
            else
            {
                DownloadProgress.IsIndeterminate = true;
                StatusText.Text = $"Downloading... {FormatBytes(copied)}";
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }

        // ── Gamepad input (called by MillerColumnsPage) ────────

        public void HandleDPad(VirtualKey key, bool isRepeat)
        {
            if (_downloading || !_webViewReady) return;
            int step = isRepeat ? 60 : 90;
            switch (key)
            {
                case VirtualKey.GamepadDPadUp:
                    _ = InvokeJs($"window.scrollBy(0,{-step});");
                    break;
                case VirtualKey.GamepadDPadDown:
                    _ = InvokeJs($"window.scrollBy(0,{step});");
                    break;
            }
        }

        public void HandleButton(VirtualKey key)
        {
            if (_downloading)
            {
                if (key == VirtualKey.GamepadB)
                {
                    Log.Info("UrlDownloadOverlay: B → cancel download");
                    _cts?.Cancel();
                }
                return;
            }

            switch (key)
            {
                case VirtualKey.GamepadB:
                    Close();
                    break;
                case VirtualKey.GamepadA:
                    if (_webViewReady)
                    {
                        _ = InvokeJs("(function(){ try { return window.xfClick ? window.xfClick() : 'no-cursor'; } catch(e) { return 'err'; } })()");
                    }
                    break;
                case VirtualKey.GamepadX:
                    if (DownloadWebView.CanGoBack) DownloadWebView.GoBack();
                    break;
                case VirtualKey.GamepadY:
                    if (DownloadWebView.CanGoForward) DownloadWebView.GoForward();
                    break;
            }
        }

        public void HandleLeftStick(float x, float y)
        {
            if (_downloading || !_webViewReady || !_cursorInjected) return;
            if (Math.Abs(x) < 0.15f && Math.Abs(y) < 0.15f) return;
            float speed = 12f;
            _ = InvokeJs($"window.xfMove({x * speed:F1},{ -y * speed:F1});");
        }

        public void HandleRightStick(float x, float y)
        {
            if (_downloading || !_webViewReady) return;
            if (Math.Abs(x) < 0.15f && Math.Abs(y) < 0.15f) return;
            float dx = x * 50f;
            float dy = -y * 50f;
            _ = InvokeJs($"window.scrollBy({dx:F1},{dy:F1});");
        }

        // ── JS helpers ─────────────────────────────────────────

        private async Task InjectCursorAsync()
        {
            try
            {
                string js = @"(function(){
try {
  var old = document.getElementById('xf_cursor'); if (old) old.remove();
  var c = document.createElement('div');
  c.id = 'xf_cursor';
  c.style.cssText = 'position:fixed;left:0;top:0;width:28px;height:28px;margin:-14px 0 0 -14px;border-radius:50%;border:2px solid #93C43C;background:rgba(147,196,60,0.15);pointer-events:none;z-index:2147483647;';
  document.body.appendChild(c);
  window.xfCurX = Math.floor(window.innerWidth / 2);
  window.xfCurY = Math.floor(window.innerHeight / 2);
  c.style.left = window.xfCurX + 'px';
  c.style.top = window.xfCurY + 'px';
  window.xfMove = function(dx, dy) {
    window.xfCurX += dx; window.xfCurY += dy;
    var el = document.getElementById('xf_cursor');
    if (el) { el.style.left = window.xfCurX + 'px'; el.style.top = window.xfCurY + 'px'; }
  };
  window.xfClick = function() {
    var el = document.elementFromPoint(window.xfCurX, window.xfCurY);
    if (el) { el.click(); return el.tagName; }
    return 'none';
  };
  return 'ok';
} catch (e) { return 'err:' + e.message; }
})()";
                string result = await DownloadWebView.InvokeScriptAsync("eval", new[] { js });
                _cursorInjected = result == "ok";
                Log.Dbg("UrlDownloadOverlay: cursor injected = {Ok}", _cursorInjected);
            }
            catch (Exception ex)
            {
                Log.Warn("UrlDownloadOverlay: cursor injection failed", ex);
                _cursorInjected = false;
            }
        }

        private async Task InvokeJs(string script)
        {
            try
            {
                await DownloadWebView.InvokeScriptAsync("eval", new[] { script });
            }
            catch (Exception ex)
            {
                Log.Warn("UrlDownloadOverlay.InvokeJs failed — script: {Script}", ex, script);
            }
        }
    }
}
