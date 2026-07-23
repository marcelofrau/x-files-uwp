using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using XFiles.FileSystem;

namespace XFiles.Controls
{
    public sealed partial class PdfFullScreenOverlay : UserControl
    {
        private double _zoomLevel = 1.0;
        private double _displayZoom = 1.0;
        private const double ZoomSpeed = 0.025;
        private const double MinZoom = 0.25;
        private const double MaxZoom = 5.0;
        private double _targetPanX;
        private double _targetPanY;
        private double _displayPanX;
        private double _displayPanY;
        private const double DPadPanSpeed = 200.0;
        private const double LerpFactor = 0.35;

        private string _filePath;
        private int _currentPage;
        private int _pageCount;
        private uint _baseRenderWidth;

        private int _currentTier = 1;
        private readonly Dictionary<int, WriteableBitmap> _tierCache
            = new Dictionary<int, WriteableBitmap>();
        private bool _upgradePending;
        public Action OnClosed;

        private static class Tiers
        {
            public const int Base = 1;
            public const int Mid = 2;
            public const int High = 4;

            public static int FromZoom(double zoom)
            {
                if (zoom >= 3.0) return High;
                if (zoom >= 1.5) return Mid;
                return Base;
            }
        }

        private readonly DispatcherTimer _renderTimer;

        public bool IsOpen => Visibility == Visibility.Visible;

        public PdfFullScreenOverlay()
        {
            this.InitializeComponent();
            _renderTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _renderTimer.Tick += OnRenderTick;
        }

        public async Task ShowAsync(string filePath, int pageCount, int startPage = 0)
        {
            _filePath = filePath;
            _pageCount = pageCount;
            _currentPage = Math.Max(0, Math.Min(startPage, pageCount - 1));
            _currentTier = 1;
            _tierCache.Clear();
            _upgradePending = false;

            ResetZoomPan();
            Visibility = Visibility.Visible;
            _renderTimer.Start();

            PageText.Text = $"{_currentPage + 1} / {_pageCount}";
            await LoadBasePageAsync(_currentPage);

            Log.Information("PdfFullScreenOverlay: opened '{Path}', page {Page}/{Total}",
                filePath, _currentPage + 1, _pageCount);
        }

        public void Close()
        {
            _renderTimer.Stop();
            Visibility = Visibility.Collapsed;
            FullImage.Source = null;
            _tierCache.Clear();
            _filePath = null;
            Log.Information("PdfFullScreenOverlay: closed");
            OnClosed?.Invoke();
        }

        public async void HandleBumper(bool isLeft)
        {
            if (!IsOpen) return;

            if (isLeft && _currentPage > 0)
            {
                _currentPage--;
                _tierCache.Clear();
                _currentTier = 1;
                _upgradePending = false;
                ResetZoomPan();
                PageText.Text = $"{_currentPage + 1} / {_pageCount}";
                await LoadBasePageAsync(_currentPage);
            }
            else if (!isLeft && _currentPage < _pageCount - 1)
            {
                _currentPage++;
                _tierCache.Clear();
                _currentTier = 1;
                _upgradePending = false;
                ResetZoomPan();
                PageText.Text = $"{_currentPage + 1} / {_pageCount}";
                await LoadBasePageAsync(_currentPage);
            }
        }

        public void HandleButton(VirtualKey key)
        {
            switch (key)
            {
                case VirtualKey.GamepadB:
                case VirtualKey.Escape:
                    Close();
                    break;
            }
        }

        public void HandleTriggers(float left, float right)
        {
            if (!IsOpen) return;

            double zoomDelta = (right - left) * ZoomSpeed;
            if (Math.Abs(zoomDelta) > 0.001)
            {
                double prevZoom = _zoomLevel;
                _zoomLevel = Math.Max(MinZoom, Math.Min(MaxZoom, _zoomLevel + zoomDelta));
                if (_zoomLevel <= 1.0)
                {
                    _targetPanX = 0;
                    _targetPanY = 0;
                }

                CheckTierTransition(prevZoom, _zoomLevel);
            }
        }

        public void HandleDPad(VirtualKey key)
        {
            if (_zoomLevel <= 1.0) return;
            double panStep = DPadPanSpeed / _zoomLevel;
            switch (key)
            {
                case VirtualKey.GamepadDPadUp:
                    _targetPanY -= panStep;
                    break;
                case VirtualKey.GamepadDPadDown:
                    _targetPanY += panStep;
                    break;
                case VirtualKey.GamepadDPadLeft:
                    _targetPanX -= panStep;
                    break;
                case VirtualKey.GamepadDPadRight:
                    _targetPanX += panStep;
                    break;
            }
        }

        public void HandleRightStick(float x, float y)
        {
            if (_zoomLevel <= 1.0) return;
            double mag = Math.Sqrt(x * x + y * y);
            if (mag < 0.15) return;
            double speed = 20.0 / _zoomLevel;
            _targetPanX -= x * speed;
            _targetPanY += y * speed;
        }

        private async Task LoadBasePageAsync(int pageIndex)
        {
            LoadingRing.IsActive = true;
            FullImage.Source = null;

            if (_baseRenderWidth == 0)
                _baseRenderWidth = 1920;

            var result = await PdfPreviewService.LoadPageAsync(
                _filePath, pageIndex, _baseRenderWidth);

            LoadingRing.IsActive = false;

            if (result.Bitmap != null)
            {
                FullImage.Source = result.Bitmap;
                _tierCache[Tiers.Base] = result.Bitmap;
                _currentTier = Tiers.Base;
            }
            else if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                Log.Warning("PdfFullScreenOverlay: load error: {Error}", result.ErrorMessage);
            }
        }

        private void CheckTierTransition(double prevZoom, double newZoom)
        {
            int prevTier = Tiers.FromZoom(prevZoom);
            int newTier = Tiers.FromZoom(newZoom);

            if (newTier != prevTier && !_upgradePending)
            {
                StartTierUpgrade(newTier);
            }
        }

        private async void StartTierUpgrade(int targetTier)
        {
            if (_upgradePending) return;
            _upgradePending = true;

            WriteableBitmap highResBitmap;
            if (_tierCache.TryGetValue(targetTier, out highResBitmap))
            {
                FullImage.Source = highResBitmap;
                _currentTier = targetTier;
                _upgradePending = false;
                Log.Information("PdfFullScreenOverlay: tier {From}→{To} (cached), zoom {Zoom:F2}",
                    _currentTier, targetTier, _zoomLevel);
                return;
            }

            uint renderWidth = (uint)(_baseRenderWidth * targetTier);
            var result = await PdfPreviewService.LoadPageAsync(
                _filePath, _currentPage, renderWidth);

            if (result.Bitmap != null)
            {
                _tierCache[targetTier] = result.Bitmap;
                FullImage.Source = result.Bitmap;
                _currentTier = targetTier;
                Log.Information("PdfFullScreenOverlay: tier {From}→{To} (rendered), zoom {Zoom:F2}",
                    _currentTier, targetTier, _zoomLevel);
            }

            _upgradePending = false;
        }

        private void ResetZoomPan()
        {
            _zoomLevel = 1.0;
            _displayZoom = 1.0;
            _targetPanX = 0;
            _targetPanY = 0;
            _displayPanX = 0;
            _displayPanY = 0;
            ImageScale.ScaleX = 1;
            ImageScale.ScaleY = 1;
            ImageTranslate.X = 0;
            ImageTranslate.Y = 0;
            ZoomText.Visibility = Visibility.Collapsed;
        }

        private void OnRenderTick(object sender, object e)
        {
            double zoomDiff = _zoomLevel - _displayZoom;
            if (Math.Abs(zoomDiff) > 0.0005)
            {
                _displayZoom += zoomDiff * 0.2;
                ImageScale.ScaleX = _displayZoom;
                ImageScale.ScaleY = _displayZoom;
            }
            else if (Math.Abs(zoomDiff) > 0)
            {
                _displayZoom = _zoomLevel;
                ImageScale.ScaleX = _zoomLevel;
                ImageScale.ScaleY = _zoomLevel;
            }

            double dx = _targetPanX - _displayPanX;
            double dy = _targetPanY - _displayPanY;
            if (Math.Abs(dx) > 0.1 || Math.Abs(dy) > 0.1)
            {
                _displayPanX += dx * LerpFactor;
                _displayPanY += dy * LerpFactor;
                ImageTranslate.X = _displayPanX;
                ImageTranslate.Y = _displayPanY;
            }
            else if (Math.Abs(dx) > 0 || Math.Abs(dy) > 0)
            {
                _displayPanX = _targetPanX;
                _displayPanY = _targetPanY;
                ImageTranslate.X = _targetPanX;
                ImageTranslate.Y = _targetPanY;
            }

            if (_displayZoom != 1.0)
            {
                if (ZoomText.Visibility != Visibility.Visible)
                    ZoomText.Visibility = Visibility.Visible;
                ZoomText.Text = $"{(int)(_displayZoom * 100)}%";
            }
            else
            {
                if (ZoomText.Visibility != Visibility.Collapsed)
                    ZoomText.Visibility = Visibility.Collapsed;
            }
        }

        private void OnOverlayTapped(object sender, TappedRoutedEventArgs e)
        {
            Close();
        }
    }
}
