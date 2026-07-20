using System;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Navigation;
using XFiles.Controls;
using XFiles.Navigation;
#if XRAY_ENABLED
using XrayLib;
#endif

namespace XFiles
{
    sealed partial class App : Application
    {
        public static GamepadInputService GamepadInput { get; private set; }
#if XRAY_ENABLED
        private static Xray _xray;
        private DispatcherTimer _xrayTimer;
#endif

        public App()
        {
            this.InitializeComponent();
            this.RequiresPointerMode = ApplicationRequiresPointerMode.WhenRequested;
            this.Suspending += OnSuspending;
            this.Resuming += OnResuming;

            this.UnhandledException += OnAppUnhandledException;
            TaskScheduler.UnobservedTaskException += OnTaskSchedulerUnobservedException;
        }

        private void OnAppUnhandledException(object sender, Windows.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            var ex = e.Exception;
            var title = ex?.GetType().Name ?? "Unknown Error";
            var description = ex?.Message ?? "An unexpected error occurred.";
            var details = ex?.ToString() ?? "(no stack trace)";

            Log.Error("Unhandled exception: {Message}", ex, ex?.Message);

            ShowErrorOverlay(title, description, details);
        }

        private void OnTaskSchedulerUnobservedException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            var ex = e.Exception?.InnerException ?? e.Exception;
            var title = ex?.GetType().Name ?? "Task Error";
            var description = ex?.Message ?? "An unobserved task exception occurred.";
            var details = e.Exception?.ToString() ?? "(no stack trace)";

            Log.Error("Unobserved task exception: {Message}", ex, ex?.Message);

            ShowErrorOverlay(title, description, details);
        }

        private void ShowErrorOverlay(string title, string description, string details)
        {
            try
            {
                var rootGrid = Window.Current.Content as Grid;
                var frame = rootGrid?.Children[0] as Frame;
                if (frame?.Content is Controls.MillerColumnsPage millerPage)
                {
                    millerPage.ShowError(title, description, details);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Failed to show error overlay", ex);
            }
        }

        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            Log.Init();
            Log.Information("App.OnLaunched — PrelaunchActivated={Prelaunch}, PreviousState={State}",
                e.PrelaunchActivated, e.PreviousExecutionState);

#if XRAY_ENABLED
            _xray = Xray.Start("x-files", cfg =>
            {
                cfg.AppId = "com.xfiles.uwp";
                cfg.Version = "0.1.0";
                cfg.Logger = Log.Logger;
            });
            Log.Information("Xray agent started on port {Port}", _xray.BoundPort);

            _xrayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _xrayTimer.Tick += (s, ev) => _xray?.Update();
            _xrayTimer.Start();
#endif

            Frame rootFrame = Window.Current.Content as Frame;

            if (rootFrame == null)
            {
                Log.Debug("Creating root Frame");
                rootFrame = new Frame();
                rootFrame.NavigationFailed += OnNavigationFailed;
                rootFrame.Navigated += OnRootFrameNavigated;

                if (e.PreviousExecutionState == ApplicationExecutionState.Terminated)
                {
                    Log.Warning("Restoring from Terminated state (TODO: persist/restore navigation)");
                }

                var rootGrid = new Grid();
                rootGrid.Children.Add(rootFrame);
                // rootGrid.Children.Add(new DebugOverlay(Log.Screen)); // disabled for now
                Window.Current.Content = rootGrid;
                Window.Current.CoreWindow.PointerCursor = null;
            }

            if (e.PrelaunchActivated == false)
            {
                if (rootFrame.Content == null)
                {
                    Log.Information("Starting GamepadInputService");
                    GamepadInput = new GamepadInputService();
                    GamepadInput.ControllerConnectedChanged += (s, connected) =>
                    {
                        Log.Information("Controller {Status}", connected ? "connected" : "disconnected");
                    };
                    GamepadInput.Start();

                    Log.Information("Navigating to MillerColumnsPage");
                    rootFrame.Navigate(typeof(Controls.MillerColumnsPage));
                }
                Window.Current.Activate();
                Window.Current.CoreWindow.PointerCursor = null;

                // Custom splash overlay
                ShowSplashOverlay(Window.Current.Content as Grid);

                // Play Mac boot chime
                PlayBootChime();

                // Remove Xbox safe zone (overscan margin) — app fills entire screen
                var view = ApplicationView.GetForCurrentView();
                view.SetDesiredBoundsMode(ApplicationViewBoundsMode.UseCoreWindow);

                // Prevent system B button from closing the app
                Windows.UI.Core.SystemNavigationManager.GetForCurrentView().BackRequested += (s, args) =>
                {
                    args.Handled = true;
                };

                Log.Information("Window activated");
            }
            else
            {
                Log.Information("Prelaunch — skipping UI");
            }
        }

        private void OnRootFrameNavigated(object sender, NavigationEventArgs e)
        {
            Log.Information("Frame navigated to {Page}", e.SourcePageType?.Name ?? "null");
        }

        private async void PlayBootChime()
        {
            try
            {
                var file = await Windows.Storage.StorageFile.GetFileFromApplicationUriAsync(
                    new Uri("ms-appx:///Assets/mac-startup.mp3"));
                var stream = await file.OpenReadAsync();
                var source = Windows.Media.Core.MediaSource.CreateFromStream(stream, stream.ContentType);
                var player = new Windows.Media.Playback.MediaPlayer();
                player.Volume = 0.4;
                player.Source = source;
                player.Play();
                Log.Information("Boot chime playing");
            }
            catch (Exception ex)
            {
                Log.Warning("Failed to play boot chime: {Error}", ex.Message);
            }
        }

        private void ShowSplashOverlay(Grid rootGrid)
        {
            var overlay = new Grid
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x1A, 0x1A, 0x1A)),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            var image = new Image
            {
                Source = new Windows.UI.Xaml.Media.Imaging.BitmapImage(
                    new Uri("ms-appx:///Assets/SplashScreen.scale-200.png")),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Stretch = Stretch.Uniform,
                Width = 620,
                Height = 300
            };

            var scaleTransform = new ScaleTransform { ScaleX = 1.0, ScaleY = 1.0 };
            image.RenderTransform = scaleTransform;
            image.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);

            overlay.Children.Add(image);
            rootGrid.Children.Add(overlay);

            // Animate: hold 0.8s, then scale up + fade out over 1s
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();

                var storyboard = new Storyboard();

                // Image scale X
                var scaleXAnim = new DoubleAnimation
                {
                    From = 1.0,
                    To = 1.8,
                    Duration = new Duration(TimeSpan.FromMilliseconds(1000)),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                Storyboard.SetTarget(scaleXAnim, image);
                Storyboard.SetTargetProperty(scaleXAnim, "(UIElement.RenderTransform).(ScaleTransform.ScaleX)");
                storyboard.Children.Add(scaleXAnim);

                // Image scale Y
                var scaleYAnim = new DoubleAnimation
                {
                    From = 1.0,
                    To = 1.8,
                    Duration = new Duration(TimeSpan.FromMilliseconds(1000)),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                Storyboard.SetTarget(scaleYAnim, image);
                Storyboard.SetTargetProperty(scaleYAnim, "(UIElement.RenderTransform).(ScaleTransform.ScaleY)");
                storyboard.Children.Add(scaleYAnim);

                // Image opacity
                var imageOpacity = new DoubleAnimation
                {
                    From = 1.0,
                    To = 0.0,
                    Duration = new Duration(TimeSpan.FromMilliseconds(1000)),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                };
                Storyboard.SetTarget(imageOpacity, image);
                Storyboard.SetTargetProperty(imageOpacity, "Opacity");
                storyboard.Children.Add(imageOpacity);

                // Background opacity
                var bgOpacity = new DoubleAnimation
                {
                    From = 1.0,
                    To = 0.0,
                    Duration = new Duration(TimeSpan.FromMilliseconds(1000)),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                };
                Storyboard.SetTarget(bgOpacity, overlay);
                Storyboard.SetTargetProperty(bgOpacity, "Opacity");
                storyboard.Children.Add(bgOpacity);

                storyboard.Completed += (sender, args) =>
                {
                    rootGrid.Children.Remove(overlay);
                    Log.Information("Splash overlay removed");
                };

                storyboard.Begin();
            };
            timer.Start();

            Log.Information("Splash overlay shown");
        }

        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            Log.Error("Navigation FAILED to {Page}: {Error}", e.Exception, e.SourcePageType?.Name);
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName, e.Exception);
        }

        void OnSuspending(object sender, SuspendingEventArgs e)
        {
            Log.Information("App suspending");
            var deferral = e.SuspendingOperation.GetDeferral();
            try
            {
                GamepadInput?.Stop();
                Log.Debug("GamepadInputService stopped");

                var rootGrid = Windows.UI.Xaml.Window.Current.Content as Windows.UI.Xaml.Controls.Grid;
                var frame = rootGrid?.Children[0] as Windows.UI.Xaml.Controls.Frame;
                if (frame?.Content is Controls.MillerColumnsPage millerPage)
                {
                    millerPage.StopAllTimers();
                    Log.Debug("MillerColumnsPage timers stopped");
                }
            }
            catch (Exception ex)
            {
                Log.Error("Error during suspend", ex);
            }
            deferral.Complete();
        }

        void OnResuming(object sender, object e)
        {
            Log.Information("App resuming");
            try
            {
#if XRAY_ENABLED
                _xray = Xray.Start("x-files", cfg =>
                {
                    cfg.AppId = "com.xfiles.uwp";
                    cfg.Version = "0.1.0";
                });
                _xrayTimer?.Start();
#endif
                GamepadInput?.Start();
                Window.Current.CoreWindow.PointerCursor = null;
                Log.Debug("GamepadInputService restarted");
            }
            catch (Exception ex)
            {
                Log.Error("Error during resume", ex);
            }
        }
    }
}
