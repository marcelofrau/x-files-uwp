using System;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;

namespace XFiles.Controls
{
    public sealed partial class ImageFullScreenOverlay : UserControl
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

        private readonly DispatcherTimer _renderTimer;

        public bool IsOpen => Visibility == Visibility.Visible;

        public ImageFullScreenOverlay()
        {
            this.InitializeComponent();
            _renderTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _renderTimer.Tick += OnRenderTick;
        }

        public void Show(ImageSource source)
        {
            FullImage.Source = source;
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
            Visibility = Visibility.Visible;
            _renderTimer.Start();
            Log.Information("ImageFullScreenOverlay: opened");
        }

        public void Close()
        {
            _renderTimer.Stop();
            Visibility = Visibility.Collapsed;
            FullImage.Source = null;
            Log.Information("ImageFullScreenOverlay: closed");
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
                _zoomLevel = Math.Max(MinZoom, Math.Min(MaxZoom, _zoomLevel + zoomDelta));
                if (_zoomLevel <= 1.0)
                {
                    _targetPanX = 0;
                    _targetPanY = 0;
                }
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

        private void OnRenderTick(object sender, object e)
        {
            bool changed = false;

            // Smooth zoom interpolation
            double zoomDiff = _zoomLevel - _displayZoom;
            if (Math.Abs(zoomDiff) > 0.0005)
            {
                _displayZoom += zoomDiff * 0.2;
                ImageScale.ScaleX = _displayZoom;
                ImageScale.ScaleY = _displayZoom;
                changed = true;
            }
            else if (Math.Abs(zoomDiff) > 0)
            {
                _displayZoom = _zoomLevel;
                ImageScale.ScaleX = _zoomLevel;
                ImageScale.ScaleY = _zoomLevel;
                changed = true;
            }

            // Smooth pan interpolation
            double dx = _targetPanX - _displayPanX;
            double dy = _targetPanY - _displayPanY;
            if (Math.Abs(dx) > 0.1 || Math.Abs(dy) > 0.1)
            {
                _displayPanX += dx * LerpFactor;
                _displayPanY += dy * LerpFactor;
                ImageTranslate.X = _displayPanX;
                ImageTranslate.Y = _displayPanY;
                changed = true;
            }
            else if (Math.Abs(dx) > 0 || Math.Abs(dy) > 0)
            {
                _displayPanX = _targetPanX;
                _displayPanY = _targetPanY;
                ImageTranslate.X = _targetPanX;
                ImageTranslate.Y = _targetPanY;
                changed = true;
            }

            // Snap zoom text
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
