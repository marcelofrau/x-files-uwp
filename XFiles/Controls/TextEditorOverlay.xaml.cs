using System;
using System.Threading.Tasks;
using Windows.System;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using XFiles.FileSystem;
using static XFiles.FileSystem.TextEditorService;

namespace XFiles.Controls
{
    public sealed partial class TextEditorOverlay : UserControl
    {
        private string _language;

        private string _filePath;
        private string _fileName;
        private FileTier _fileTier;
        private LineEndingStyle _lineEnding;
        private string _detectedEncodingName;
        private bool _isReadOnly;
        private bool _highlightEnabled;

        // HTML template parts (loaded once)
        private static string _highlightJs;
        private static string _highlightCss;
        private static string _fontBase64;
        private static string _editorJs;

        // Toast timer
        private DispatcherTimer _toastTimer;

        // Dirty state
        private DispatcherTimer _dirtyPollTimer;
        private bool _lastDirtyState;
        private bool _webViewReady;
        private DateTime _dirtySuppressUntil = DateTime.MinValue;

        // Unsaved dialog state
        private bool _isUnsavedDialogOpen;
        private TaskCompletionSource<UnsavedDialogResult> _unsavedTcs;

        // Event-driven keyboard input
        private bool _keyHandlerRegistered;

        // Pending JS config (applied after NavigationCompleted)
        private int _pendingMaxUndo;
        private bool _pendingHighlightEnabled;
        private string _pendingLanguage;

        // Virtual keyboard state
        private bool _isKeyboardVisible;
        private string _lastKeyboardText;

        private enum UnsavedDialogResult { Save, Discard, Cancel }

        public bool IsOpen => Visibility == Visibility.Visible;

        public Action OnClosed;

        public TextEditorOverlay()
        {
            this.InitializeComponent();
            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            _toastTimer.Tick += OnToastTimerTick;
            _dirtyPollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _dirtyPollTimer.Tick += OnDirtyPollTick;

            // Virtual keyboard bridge
            KeyboardBridge.TextChanged += OnKeyboardBridgeTextChanged;
            KeyboardBridge.KeyDown += OnKeyboardBridgeKeyDown;
            _lastKeyboardText = "";
        }

        /// <summary>
        /// Open a text file in the editor.
        /// </summary>
        public async void Show(string filePath)
        {
            Log.Info("TextEditorOverlay.Show: {Path}", filePath);

            _filePath = filePath;
            _fileName = System.IO.Path.GetFileName(filePath);

            // Load file
            var result = await TextEditorService.LoadAsync(filePath);
            if (result == null)
            {
                Log.Warn("TextEditorOverlay.Show: failed to load {Path}", filePath);
                return;
            }

            _fileTier = result.Tier;
            _lineEnding = result.LineEnding;
            _detectedEncodingName = result.EncodingName;
            _isReadOnly = result.IsBinary || _fileTier == FileTier.ReadOnly;
            _highlightEnabled = _fileTier == FileTier.FullEdit;

            // Load HTML assets
            await EnsureAssetsLoadedAsync();

            // Build and load HTML
            string lang = TextEditorService.GetHighlightLang(System.IO.Path.GetExtension(filePath));
            _language = lang;
            string html = BuildEditorHtml(result.Text, lang, _highlightEnabled);
            EditorWebView.NavigationCompleted += OnEditorWebViewNavigated;
            EditorWebView.NavigateToString(html);

            // Show notification bar for readonly
            if (_fileTier == FileTier.ReadOnly)
            {
                string reason = result.IsBinary
                    ? "This file appears to be binary and cannot be edited."
                    : $"File too large to edit ({FormatFileSize(result.FileSize)}) — read-only mode.";
                NotificationText.Text = reason;
                NotificationBar.Visibility = Visibility.Visible;
            }
            else
            {
                NotificationBar.Visibility = Visibility.Collapsed;
            }

            // Store JS config (applied after NavigationCompleted)
            _pendingMaxUndo = 0;
            _pendingHighlightEnabled = _highlightEnabled;
            _pendingLanguage = lang;

            // Show overlay
            Visibility = Visibility.Visible;
            Log.Dbg("TextEditorOverlay: visibility=Visible, registering handlers");

            // Show HTML mouse cursor (LStick controls it on Xbox)
            Log.Dbg("TextEditorOverlay: HTML mouse cursor enabled for LStick");

            // Register keyboard handler
            if (!_keyHandlerRegistered)
            {
                Window.Current.CoreWindow.KeyDown += OnEditorKeyDown;
                _keyHandlerRegistered = true;
                Log.Dbg("TextEditorOverlay: CoreWindow.KeyDown registered");
            }
            else
            {
                Log.Warn("TextEditorOverlay: KeyDown already registered!");
            }

            // Populate sidebar
            FileNameText.Text = _fileName;
            UpdateSidebarStatus(false);
            EncodingText.Text = _detectedEncodingName ?? "";
            string le = _lineEnding == LineEndingStyle.CRLF ? "CRLF" :
                        _lineEnding == LineEndingStyle.LF ? "LF" :
                        _lineEnding == LineEndingStyle.CR ? "CR" : "";
            LineEndingText.Text = le;

            // Set file icon
            SetFileIcon(filePath);

            // Start dirty poll
            _lastDirtyState = false;
            _dirtyPollTimer.Start();

            // Update footer
            UpdateFooter();

            Log.Info("TextEditorOverlay: opened {File} — tier={Tier}, encoding={Encoding}, lang={Lang}",
                _fileName, _fileTier, _detectedEncodingName, lang);
        }

        /// <summary>
        /// Close the editor. Returns true if safe to close, false if user cancelled.
        /// </summary>
        public async Task<bool> ConfirmClose()
        {
            bool dirty = await InvokeJsBool("editor.isDirty()");
            // Sync cached state with JS truth so badge is correct
            if (dirty != _lastDirtyState)
            {
                _lastDirtyState = dirty;
                UpdateSidebarStatus(dirty);
            }
            Log.Dbg("TextEditorOverlay: ConfirmClose — isDirty={Dirty}", dirty);
            if (!dirty) return true;

            Log.Dbg("TextEditorOverlay: ConfirmClose — showing unsaved dialog");
            var result = await ShowUnsavedDialog();
            Log.Dbg("TextEditorOverlay: ConfirmClose — dialog result={Result}", result);
            switch (result)
            {
                case UnsavedDialogResult.Save:
                    Log.Info("TextEditorOverlay: ConfirmClose — saving then closing");
                    await HandleSaveAsync();
                    return true;
                case UnsavedDialogResult.Discard:
                    Log.Info("TextEditorOverlay: ConfirmClose — discarding changes");
                    await InvokeJs("editor.setDirty(false)");
                    return true;
                default:
                    Log.Dbg("TextEditorOverlay: ConfirmClose — cancelled, staying open");
                    return false;
            }
        }

        public void Close()
        {
            Log.Info("TextEditorOverlay.Close: begin");
            HideVirtualKeyboard();
            HideUnsavedDialog();
            _dirtyPollTimer.Stop();

            // Unregister keyboard handler
            if (_keyHandlerRegistered)
            {
                Window.Current.CoreWindow.KeyDown -= OnEditorKeyDown;
                _keyHandlerRegistered = false;
                Log.Dbg("TextEditorOverlay.Close: CoreWindow.KeyDown unregistered");
            }

            // Hide HTML mouse cursor
            try
            {
                _ = InvokeJs("editor.hideMouse()");
                Log.Dbg("TextEditorOverlay.Close: HTML mouse hidden");
            }
            catch (Exception ex)
            {
                Log.Warn("TextEditorOverlay.Close: mouse hide failed", ex);
            }

            Visibility = Visibility.Collapsed;
            EditorWebView.NavigateToString("about:blank");
            OnClosed?.Invoke();
        }

        private async System.Threading.Tasks.Task AttemptCloseAsync()
        {
            Log.Dbg("TextEditorOverlay: AttemptCloseAsync called — dirty={Dirty}, unsavedOpen={Unsaved}", _lastDirtyState, _isUnsavedDialogOpen);
            bool safe = await ConfirmClose();
            Log.Dbg("TextEditorOverlay: AttemptCloseAsync — ConfirmClose returned safe={Safe}", safe);
            if (safe) Close();
        }

        // ── Input routing (called by MillerColumnsPage) ────────

        public async void HandleDPadUp()    { if (!_isReadOnly) { await InvokeJs("editor.moveCursorUp(1)"); PullJsLogs("DPadUp"); } }
        public async void HandleDPadDown()  { if (!_isReadOnly) { await InvokeJs("editor.moveCursorDown(1)"); PullJsLogs("DPadDown"); } }
        public async void HandleDPadLeft()  { if (!_isReadOnly) { await InvokeJs("editor.moveCursorLeft(1)"); PullJsLogs("DPadLeft"); } }
        public async void HandleDPadRight() { if (!_isReadOnly) { await InvokeJs("editor.moveCursorRight(1)"); PullJsLogs("DPadRight"); } }

        // ── Keyboard input (event-driven) ────────────────────

        private void OnEditorKeyDown(CoreWindow sender, KeyEventArgs e)
        {
            if (!IsOpen || _isUnsavedDialogOpen) return;

            var key = e.VirtualKey;
            Log.Verb("EditorKeyDown: key={Key} readOnly={RO}", key, _isReadOnly);

            switch (key)
            {
                case VirtualKey.Up:
                    if (!_isReadOnly) { InvokeJs("editor.moveCursorUp(1)"); PullJsLogs("Up"); }
                    e.Handled = true;
                    break;
                case VirtualKey.Down:
                    if (!_isReadOnly) { InvokeJs("editor.moveCursorDown(1)"); PullJsLogs("Down"); }
                    e.Handled = true;
                    break;
                case VirtualKey.Left:
                    if (!_isReadOnly) { InvokeJs("editor.moveCursorLeft(1)"); PullJsLogs("Left"); }
                    e.Handled = true;
                    break;
                case VirtualKey.Right:
                    if (!_isReadOnly) { InvokeJs("editor.moveCursorRight(1)"); PullJsLogs("Right"); }
                    e.Handled = true;
                    break;
                case VirtualKey.Home:
                    if (!_isReadOnly) { InvokeJs("editor.moveToLineStart()"); PullJsLogs("Home"); }
                    e.Handled = true;
                    break;
                case VirtualKey.End:
                    if (!_isReadOnly) { InvokeJs("editor.moveToLineEnd()"); PullJsLogs("End"); }
                    e.Handled = true;
                    break;
                case VirtualKey.PageUp:
                    if (!_isReadOnly) { InvokeJs("editor.jumpPageUp()"); PullJsLogs("PgUp"); }
                    e.Handled = true;
                    break;
                case VirtualKey.PageDown:
                    if (!_isReadOnly) { InvokeJs("editor.jumpPageDown()"); PullJsLogs("PgDn"); }
                    e.Handled = true;
                    break;
                case VirtualKey.GamepadMenu:
                    Log.Verb("EditorKeyDown: Start → save");
                    HandleSave();
                    e.Handled = true;
                    break;
                default:
                    Log.Verb("EditorKeyDown: unhandled key={Key}", key);
                    break;
            }
        }

        private async void PullJsLogs(string direction)
        {
            try
            {
                string logs = await InvokeJsStr("editor.getLogs()");
                if (!string.IsNullOrEmpty(logs))
                {
                    foreach (var line in logs.Split('\n'))
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                        {
#if EDITOR_JS_DEBUG
                            Log.Verb("JS[{Dir}]: {Line}", direction, line);
#endif
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("PullJsLogs failed", ex);
            }
        }

        public void HandleButton(VirtualKey key)
        {
            Log.Verb("TextEditorOverlay.HandleButton: key={Key} unsavedOpen={Unsaved} keyboard={Keyboard} readOnly={RO}",
                key, _isUnsavedDialogOpen, _isKeyboardVisible, _isReadOnly);

            if (_isUnsavedDialogOpen) { HandleUnsavedDialogButton(key); return; }

            switch (key)
            {
                case VirtualKey.GamepadB:
                    if (_isKeyboardVisible) { Log.Verb("HandleButton: B → hide keyboard"); HideVirtualKeyboard(); }
                    else { Log.Verb("HandleButton: B → AttemptCloseAsync"); _ = AttemptCloseAsync(); }
                    break;
                case VirtualKey.GamepadX:
                    Log.Verb("HandleButton: X → backspace");
                    if (!_isReadOnly) { InvokeJs("editor.backspace()"); PullJsLogs("backspace"); }
                    break;
                case VirtualKey.GamepadY:
                    Log.Verb("HandleButton: Y → newline");
                    if (!_isReadOnly) { InvokeJs("editor.insertNewline()"); PullJsLogs("newline"); }
                    break;
                case VirtualKey.GamepadA:
                    Log.Verb("HandleButton: A → show virtual keyboard");
                    if (!_isReadOnly) { ShowVirtualKeyboard(); }
                    break;
                case VirtualKey.GamepadMenu:
                    Log.Verb("HandleButton: Start → save");
                    HandleSave();
                    break;
                default:
                    Log.Verb("HandleButton: unhandled key {Key}", key);
                    break;
            }
        }

        public void HandleStick(float x, float y)
        {
            if (Math.Abs(y) < 0.15f && Math.Abs(x) < 0.15f) return;
            InvokeJs($"editor.scrollViewport({x * 35:F1}, {-y * 35:F1})");
        }

        public void HandleLeftStick(float x, float y)
        {
            if (Math.Abs(y) < 0.15f && Math.Abs(x) < 0.15f) return;
            // Move HTML mouse cursor inside WebView
            // Y is inverted: gamepad positive Y = up, screen positive Y = down
            try
            {
                float speed = 18f;
                float dx = x * speed;
                float dy = -y * speed;
                _ = InvokeJs($"editor.moveMouse({dx:F1},{dy:F1})");
            }
            catch (Exception ex)
            {
                Log.Warn("HandleLeftStick: moveMouse failed", ex);
            }
        }

        // ── Pointer → caret sync ────────────────────────────────

        private void OnCoreWindowPointerMoved(Windows.UI.Core.CoreWindow sender, Windows.UI.Core.PointerEventArgs args)
        {
            if (!IsOpen || _isReadOnly) return;
            try
            {
                var pt = args.CurrentPoint.Position;
                var webViewToContent = EditorWebView.TransformToVisual(Window.Current.Content);
                var webViewOrigin = webViewToContent.TransformPoint(new Windows.Foundation.Point(0, 0));
                float localX = (float)(pt.X - webViewOrigin.X);
                float localY = (float)(pt.Y - webViewOrigin.Y);
#if POINTER_DEBUG
                Log.Verb("PointerMoved: raw=({RawX:F1},{RawY:F1}) webOrigin=({Wx:F1},{Wy:F1}) local=({Lx:F1},{Ly:F1})",
                    pt.X, pt.Y, webViewOrigin.X, webViewOrigin.Y, localX, localY);
#endif
                InvokeJs($"editor.setTextCursorAtPoint({localX:F1},{localY:F1})");
            }
            catch (Exception ex)
            {
                Log.Warn("OnCoreWindowPointerMoved failed", ex);
            }
        }

        // ── Virtual keyboard ──────────────────────────────────

        public void ShowVirtualKeyboard()
        {
            if (_isReadOnly || _isKeyboardVisible) return;

            Log.Dbg("TextEditorOverlay: showing virtual keyboard");
            _isKeyboardVisible = true;
            _lastKeyboardText = "";
            KeyboardBridge.Text = "";

            bool shown = false;
            try
            {
                shown = InputPane.GetForCurrentView().TryShow();
                Log.Dbg("TextEditorOverlay: InputPane.TryShow result={Result}", shown);
            }
            catch (Exception ex)
            {
                Log.Warn("TextEditorOverlay: InputPane failed", ex);
            }

            if (shown)
            {
                KeyboardBridge.Focus(FocusState.Programmatic);
            }
            else
            {
                Log.Warn("TextEditorOverlay: virtual keyboard not available on this device");
                _isKeyboardVisible = false;
            }
        }

        public void HideVirtualKeyboard()
        {
            if (!_isKeyboardVisible) return;
            _isKeyboardVisible = false;

            try
            {
                InputPane.GetForCurrentView().TryHide();
            }
            catch (Exception ex)
            {
                Log.Warn("TextEditorOverlay: HideVirtualKeyboard failed", ex);
            }

            KeyboardBridge.Text = "";

            Log.Dbg("TextEditorOverlay: virtual keyboard hidden");
        }

        private void OnKeyboardBridgeTextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isKeyboardVisible || _isReadOnly) return;

            string currentText = KeyboardBridge.Text ?? "";
            if (currentText.Length > _lastKeyboardText.Length)
            {
                string newChars = currentText.Substring(_lastKeyboardText.Length);
                string escaped = newChars.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "\\r");
                _ = InvokeJs($"editor.insertText('{escaped}')");
                Log.Dbg("TextEditorOverlay: keyboard input forwarded: {Chars}", newChars);
            }
            else if (currentText.Length < _lastKeyboardText.Length)
            {
                int deleted = _lastKeyboardText.Length - currentText.Length;
                for (int i = 0; i < deleted; i++)
                    _ = InvokeJs("editor.backspace()");
            }

            _lastKeyboardText = currentText;
        }

        private void OnKeyboardBridgeKeyDown(object sender, KeyRoutedEventArgs e)
        {
            // Suppress gamepad button sounds by marking events as handled
            if (e.Key == VirtualKey.GamepadA || e.Key == VirtualKey.GamepadB ||
                e.Key == VirtualKey.GamepadX || e.Key == VirtualKey.GamepadY ||
                e.Key == VirtualKey.GamepadLeftShoulder || e.Key == VirtualKey.GamepadRightShoulder ||
                e.Key == VirtualKey.GamepadLeftTrigger || e.Key == VirtualKey.GamepadRightTrigger)
            {
                e.Handled = true;
            }
        }

        // ── Save ───────────────────────────────────────────────

        private async void HandleSave()
        {
            await HandleSaveAsync();
        }

        private async Task HandleSaveAsync()
        {
            if (_isReadOnly) return;

            bool dirty = await InvokeJsBool("editor.isDirty()");
            if (!dirty)
            {
                Log.Verb("TextEditorOverlay: no changes to save");
                return;
            }

            string content = await InvokeJsStr("editor.getText()");
            bool ok = await TextEditorService.SaveAsync(_filePath, content, _lineEnding);

            if (ok)
            {
                await InvokeJs("editor.setDirty(false)");
                _lastDirtyState = false;
                _dirtySuppressUntil = DateTime.Now.AddSeconds(2);
                Log.Info("TextEditorOverlay: saved {File}", _fileName);
                // Green badge + "Saved" status
                StatusText.Text = "Saved";
                StatusText.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x00, 0x00, 0x00));
                StatusBadge.Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x93, 0xC4, 0x3C));
                StatusBadge.Visibility = Visibility.Visible;
                ShowToast("Saved");
            }
            else
            {
                Log.Warn("TextEditorOverlay: save failed for {File}", _fileName);
                ShowToast("Save failed");
            }
        }

        // ── Toast ───────────────────────────────────────────────

        private async void OnDirtyPollTick(object sender, object e)
        {
            if (Visibility != Visibility.Visible || !_webViewReady) { return; }
            if (DateTime.Now < _dirtySuppressUntil) { return; }
            try
            {
                bool dirty = await InvokeJsBool("editor.isDirty()");
                if (dirty != _lastDirtyState)
                {
                    Log.Dbg("TextEditorOverlay: dirty state changed → {Dirty}", dirty);
                    _lastDirtyState = dirty;
                    UpdateSidebarStatus(dirty);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("TextEditorOverlay: dirtyPoll failed", ex);
            }
        }

        private void UpdateSidebarStatus(bool dirty)
        {
            if (dirty)
            {
                StatusText.Text = "Modified";
                StatusText.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x00, 0x00, 0x00));
                StatusBadge.Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xF1, 0xC4, 0x0F));
                StatusBadge.Visibility = Visibility.Visible;
            }
            else
            {
                StatusText.Text = "";
                StatusBadge.Visibility = Visibility.Collapsed;
            }
        }

        private void SetFileIcon(string filePath)
        {
            string ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
            string iconFile = "ctx-generic-120.png";
            if (FileActionSheet.TextExts.Contains(ext)) iconFile = "ctx-text-120.png";
            else if (System.IO.Path.GetFileName(filePath).EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                     || ext == ".7z" || ext == ".rar" || ext == ".tar" || ext == ".gz")
                iconFile = "ctx-archive-120.png";
            FileIconImage.Source = new Windows.UI.Xaml.Media.Imaging.BitmapImage(
                new System.Uri($"ms-appx:///Assets/Views/FileActionSheet/{iconFile}"));
        }

        private void ShowToast(string message)
        {
            SaveToastText.Text = message;
            SaveToast.Visibility = Visibility.Visible;
            _toastTimer.Stop();
            _toastTimer.Start();
        }

        private void OnToastTimerTick(object sender, object e)
        {
            _toastTimer.Stop();
            SaveToast.Visibility = Visibility.Collapsed;
        }

        // ── Footer ─────────────────────────────────────────────

        private void UpdateFooter()
        {
            // Single footer — always same state (no input mode toggle)
        }

        // ── Unsaved changes dialog ─────────────────────────────

        private Task<UnsavedDialogResult> ShowUnsavedDialog()
        {
            Log.Dbg("TextEditorOverlay: ShowUnsavedDialog — showing dialog");
            _unsavedTcs = new TaskCompletionSource<UnsavedDialogResult>();
            _isUnsavedDialogOpen = true;
            UnsavedOverlay.Visibility = Visibility.Visible;
            return _unsavedTcs.Task;
        }

        private void HideUnsavedDialog()
        {
            _isUnsavedDialogOpen = false;
            UnsavedOverlay.Visibility = Visibility.Collapsed;
        }

        private void OnUnsavedSaveClicked(object sender, RoutedEventArgs e) => HandleUnsavedDialogButton(VirtualKey.GamepadA);
        private void OnUnsavedDiscardClicked(object sender, RoutedEventArgs e) => HandleUnsavedDialogButton(VirtualKey.GamepadX);
        private void OnUnsavedCancelClicked(object sender, RoutedEventArgs e) => HandleUnsavedDialogButton(VirtualKey.GamepadB);
        private void OnUnsavedOverlayTapped(object sender, TappedRoutedEventArgs e) => HandleUnsavedDialogButton(VirtualKey.GamepadB);

        private void HandleUnsavedDialogButton(VirtualKey key)
        {
            Log.Dbg("TextEditorOverlay: HandleUnsavedDialogButton key={Key} tcsComplete={Complete}",
                key, _unsavedTcs?.Task?.IsCompleted ?? true);

            if (_unsavedTcs == null || _unsavedTcs.Task.IsCompleted) return;

            UnsavedDialogResult result;
            switch (key)
            {
                case VirtualKey.GamepadA: result = UnsavedDialogResult.Save; break;
                case VirtualKey.GamepadX: result = UnsavedDialogResult.Discard; break;
                case VirtualKey.GamepadB: result = UnsavedDialogResult.Cancel; break;
                default: return;
            }
            HideUnsavedDialog();
            _unsavedTcs.TrySetResult(result);
        }

        // ── JS helpers ─────────────────────────────────────────

        private async void OnEditorWebViewNavigated(WebView sender, WebViewNavigationCompletedEventArgs args)
        {
            EditorWebView.NavigationCompleted -= OnEditorWebViewNavigated;
            // AllowFocusOnInteraction=False should prevent WebView from stealing focus,
            // but on Xbox the WebView can still trap focus. The visibility toggle trick
            // releases any residual focus (known workaround from UWP community).
            EditorWebView.Visibility = Visibility.Collapsed;
            EditorWebView.Visibility = Visibility.Visible;

            // Show HTML mouse cursor
            await InvokeJs("editor.showMouse()");
            Log.Dbg("TextEditorOverlay: HTML mouse cursor shown after WebView init");

            // Give JS a moment to init, then configure and ensure cursor is positioned
            await Task.Delay(100);
            await InvokeJs($"editor.setMaxUndo({_pendingMaxUndo})");
            await InvokeJs($"editor.setHighlightEnabled({BoolToLower(_pendingHighlightEnabled)})");
            if (!string.IsNullOrEmpty(_pendingLanguage))
                await InvokeJs($"editor.setLanguage('{_pendingLanguage}')");
            await InvokeJs("editor.updateBlockCursor()");
            _webViewReady = true;
            Log.Dbg("TextEditorOverlay: WebView initialized, focus released via visibility toggle");
        }

        private async Task InvokeJs(string script)
        {
            try
            {
                await EditorWebView.InvokeScriptAsync("eval", new[] { script });
            }
            catch (Exception ex)
            {
                Log.Warn("TextEditorOverlay.InvokeJs failed — script: {Script}", ex, script);
            }
        }

        private async Task<string> InvokeJsStr(string script)
        {
            try
            {
                return await EditorWebView.InvokeScriptAsync("eval", new[] { script });
            }
            catch (Exception ex)
            {
                Log.Warn("TextEditorOverlay.InvokeJsStr failed", ex);
                return "";
            }
        }

        private async Task<bool> InvokeJsBool(string script)
        {
            // EdgeHTML InvokeScriptAsync returns empty string for JS booleans.
            // Wrap to force explicit string return.
            string result = await InvokeJsStr($"(function() {{ return ({script}) ? 'true' : 'false'; }})()");
            return result == "true";
        }

        // ── HTML building ──────────────────────────────────────

        private static string BuildEditorHtml(string text, string lang, bool highlightEnabled)
        {
            string escapedText = System.Net.WebUtility.HtmlEncode(text);
            string langClass = !string.IsNullOrEmpty(lang) ? ("language-" + lang) : "";
            string highlightScript = highlightEnabled
                ? $"hljs.highlightBlock(document.querySelector('code'));"
                : "";

            return $@"<!DOCTYPE html>
<html>
<head>
<meta charset=""utf-8"">
<style>
  * {{ margin:0; padding:0; box-sizing:border-box; }}
  body {{ background:#0F1318; overflow:hidden; height:100vh; }}
  #editor {{
    display:flex; height:100vh; overflow:auto;
    font-family:'Inconsolata','Consolas','Courier New',monospace;
    font-size:13px; color:#D4D4D4; line-height:1.5;
    white-space:pre;
  }}
  #line-numbers {{
    width:50px; min-width:50px;
    text-align:right; padding:8px 8px 8px 0;
    color:#5A5C60; user-select:none; pointer-events:none;
    font-family:'Inconsolata','Consolas','Courier New',monospace;
    font-size:13px; line-height:1.5;
  }}
  #line-numbers div {{ height:1.5em; }}
  #code {{
    flex:1; padding:8px 12px; outline:none;
    font-family:'Inconsolata','Consolas','Courier New',monospace;
    font-size:13px; line-height:1.5;
    tab-size:4; -moz-tab-size:4;
    overflow:visible;
    white-space:pre;
  }}
  ::selection {{ background:#264F78; }}
  ::-webkit-scrollbar {{ width:8px; height:8px; }}
  ::-webkit-scrollbar-track {{ background:#1A1D23; }}
  ::-webkit-scrollbar-thumb {{ background:#3A3D43; border-radius:4px; }}
  ::-webkit-scrollbar-thumb:hover {{ background:#5A5D63; }}
</style>
<style>{_highlightCss}</style>
</head>
<body>
<div id=""editor"" contenteditable=""true"" spellcheck=""false"">
  <div id=""line-numbers""></div>
  <pre><code id=""code"" class=""{langClass}"">{escapedText}</code></pre>
</div>
<script>{_highlightJs}</script>
<script>{_editorJs}</script>
<script>{highlightScript}</script>
</body>
</html>";
        }

        private static async Task EnsureAssetsLoadedAsync()
        {
            if (_highlightJs != null && _highlightCss != null && _fontBase64 != null && _editorJs != null)
                return;

            try
            {
                var jsFile = await StorageFile.GetFileFromApplicationUriAsync(
                    new Uri("ms-appx:///Assets/highlight.min.js"));
                _highlightJs = await FileIO.ReadTextAsync(jsFile);

                var cssFile = await StorageFile.GetFileFromApplicationUriAsync(
                    new Uri("ms-appx:///Assets/highlight-aco.css"));
                _highlightCss = await FileIO.ReadTextAsync(cssFile);

                var fontFile = await StorageFile.GetFileFromApplicationUriAsync(
                    new Uri("ms-appx:///Assets/Inconsolata-Regular.ttf"));
                var fontBytes = await Task.Run(() => System.IO.File.ReadAllBytes(fontFile.Path));
                _fontBase64 = Convert.ToBase64String(fontBytes);

                var editorFile = await StorageFile.GetFileFromApplicationUriAsync(
                    new Uri("ms-appx:///Assets/editor.js"));
                _editorJs = await FileIO.ReadTextAsync(editorFile);

                Log.Dbg("TextEditorOverlay: assets loaded — JS={JsLen}, CSS={CssLen}, Font={FontLen}, Editor={EditorLen}",
                    _highlightJs.Length, _highlightCss.Length, _fontBase64.Length, _editorJs.Length);
            }
            catch (Exception ex)
            {
                Log.Err("TextEditorOverlay: failed to load assets", ex);
                _highlightJs = "";
                _highlightCss = "";
                _fontBase64 = "";
                _editorJs = "";
            }
        }

        private static string BoolToLower(bool value) => value ? "true" : "false";
    }
}
