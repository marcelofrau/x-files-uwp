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
using XFiles.FileSystem;
using XFiles.Navigation;

namespace XFiles
{
    sealed partial class App : Application
    {
        public static GamepadInputService GamepadInput { get; private set; }
        private Windows.Media.Playback.MediaPlayer _bootChimePlayer;

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

            Log.Err("Unhandled exception: {Message}", ex, ex?.Message);

            ShowErrorOverlay(title, description, details);
        }

        private void OnTaskSchedulerUnobservedException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            var ex = e.Exception?.InnerException ?? e.Exception;
            var title = ex?.GetType().Name ?? "Task Error";
            var description = ex?.Message ?? "An unobserved task exception occurred.";
            var details = e.Exception?.ToString() ?? "(no stack trace)";

            Log.Err("Unobserved task exception: {Message}", ex, ex?.Message);

            ShowErrorOverlay(title, description, details);
        }

        private void ShowErrorOverlay(string title, string description, string details)
        {
            try
            {
                var window = Window.Current;
                if (window == null) return;
                var rootGrid = window.Content as Grid;
                var frame = rootGrid?.Children[0] as Frame;
                if (frame?.Content is Controls.MillerColumnsPage millerPage)
                {
                    millerPage.ShowError(title, description, details);
                }
            }
            catch (Exception ex)
            {
                Log.Err("Failed to show error overlay", ex);
            }
        }

        protected override async void OnLaunched(LaunchActivatedEventArgs e)
        {
            Log.Init();
            Log.Info("App.OnLaunched — PrelaunchActivated={Prelaunch}, PreviousState={State}",
                e.PrelaunchActivated, e.PreviousExecutionState);

            // Load persisted log level from SQLite
            try
            {
                string level = await Settings.XFilesSettings.GetLogLevelAsync();
                Log.SetLogLevel(level);
                Controls.MillerColumnsPage.UpdateFtpTraceFilter();
                Log.Info("App: log level loaded from settings: {Level}", level);
            }
            catch (Exception ex)
            {
                Log.Warn("App: failed to load log level, using default Info", ex);
            }

            // Settings migration — runs once per schema bump
            try
            {
                int savedVersion = await Settings.XFilesSettings.GetSettingsVersionAsync();
                int currentVersion = Settings.XFilesSettings.GetCurrentSettingsVersion();
                if (savedVersion < currentVersion)
                {
                    Log.Info("App: migrating settings from v{Old} to v{New}", savedVersion, currentVersion);

                    if (savedVersion < 1)
                    {
                        // v1: force log level to Info, compress old uncompressed logs
                        await Settings.XFilesSettings.SetLogLevelAsync("Info");
                        Log.SetLogLevel("Info");
                        Controls.MillerColumnsPage.UpdateFtpTraceFilter();
                        Log.CompressExistingLogs();
                    }

                    await Settings.XFilesSettings.SetSettingsVersionAsync(currentVersion);
                    Log.Info("App: settings migration complete");
                }
            }
            catch (Exception ex)
            {
                Log.Warn("App: settings migration failed", ex);
            }

            // Seed the sync drive-hide setting cache (read by the scanner).
            try
            {
                await Settings.XFilesSettings.GetHideEmptyDrivesAsync();
            }
            catch (Exception ex)
            {
                Log.Warn("App: failed to load HideEmptyDrives setting", ex);
            }

            // Warm the drive-accessibility probe cache on a background thread so
            // the first root scan can hide inaccessible drives without blocking
            // the UI thread (~210ms per denied drive on Xbox).
            FileSystem.DirectoryScanner.WarmDriveProbesAsync();

            // Portal setup: load persisted Device Portal credentials, arm the portal
            // client, clear the session cache, and probe reachability (fire-and-forget).
            try
            {
                string user = await Settings.XFilesSettings.GetPortalUserAsync();
                string pass = await Settings.XFilesSettings.GetPortalPassAsync();
                if (!string.IsNullOrEmpty(user) && !string.IsNullOrEmpty(pass))
                {
                    Services.DevicePortalService.SetCredentials(user, pass);
                    Log.Info("App: portal credentials loaded from settings");
                }
                else
                {
                    Log.Dbg("App: no portal credentials stored");
                }

                await Services.PortalCache.ClearAsync();
                Services.DevicePortalService.ProbeAsync();
            }
            catch (Exception ex)
            {
                Log.Warn("App: portal startup failed: {Message}", ex.Message);
            }

            Frame rootFrame = Window.Current.Content as Frame;

            if (rootFrame == null)
            {
                Log.Dbg("Creating root Frame");
                rootFrame = new Frame();
                rootFrame.NavigationFailed += OnNavigationFailed;
                rootFrame.Navigated += OnRootFrameNavigated;

                if (e.PreviousExecutionState == ApplicationExecutionState.Terminated)
                {
                    Log.Warn("Restoring from Terminated state (TODO: persist/restore navigation)");
                }

                var rootGrid = new Grid();
                rootGrid.Children.Add(rootFrame);
                Window.Current.Content = rootGrid;
                Window.Current.CoreWindow.PointerCursor = null;
            }

            if (e.PrelaunchActivated == false)
            {
                if (rootFrame.Content == null)
                {
                    Log.Info("Starting GamepadInputService");
                    GamepadInput = new GamepadInputService();
                    GamepadInput.ControllerConnectedChanged += (s, connected) =>
                    {
                        Log.Info("Controller {Status}", connected ? "connected" : "disconnected");
                    };
                    GamepadInput.Start();

                    Log.Info("Navigating to MillerColumnsPage");
                    rootFrame.Navigate(typeof(Controls.MillerColumnsPage));
                }
                Window.Current.Activate();
                Window.Current.CoreWindow.PointerCursor = null;

                // Custom splash overlay
                ShowSplashOverlay(Window.Current.Content as Grid);

                // Play Mac boot chime
                Task chimeDone = PlayBootChime();

                // Background music: read settings and start looping playback (if the
                // feature is enabled) without delaying first paint. Fired after the
                // UI is up (post-navigation) so the first-run native render never
                // overlaps the XAML construction. The BGM waits for the chime to
                // finish playing before it fades in.
                try
                {
                    _ = Audio.BackgroundMusicService.Instance.InitializeAsync(chimeDone);
                }
                catch (Exception ex)
                {
                    Log.Warn("App: background music startup failed: {Message}", ex.Message);
                }

                // Remove Xbox safe zone (overscan margin) — app fills entire screen
                var view = ApplicationView.GetForCurrentView();
                view.SetDesiredBoundsMode(ApplicationViewBoundsMode.UseCoreWindow);

                // Prevent system B button from closing the app
                Windows.UI.Core.SystemNavigationManager.GetForCurrentView().BackRequested += (s, args) =>
                {
                    args.Handled = true;
                };

                Log.Info("Window activated");
            }
            else
            {
                Log.Info("Prelaunch — skipping UI");
            }
        }

        private void OnRootFrameNavigated(object sender, NavigationEventArgs e)
        {
            Log.Info("Frame navigated to {Page}", e.SourcePageType?.Name ?? "null");
        }

        private Task PlayBootChime()
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            PlayBootChimeAsync(tcs);
            return tcs.Task;
        }

        private async void PlayBootChimeAsync(TaskCompletionSource<bool> tcs)
        {
            try
            {
                var file = await Windows.Storage.StorageFile.GetFileFromApplicationUriAsync(
                    new Uri("ms-appx:///Assets/mac-startup.mp3"));
                var stream = await file.OpenReadAsync();
                var source = Windows.Media.Core.MediaSource.CreateFromStream(stream, stream.ContentType);
                _bootChimePlayer = new Windows.Media.Playback.MediaPlayer();
                _bootChimePlayer.Volume = 0.4;
                _bootChimePlayer.Source = source;
                _bootChimePlayer.Play();
                Log.Info("Boot chime playing");

                // MediaEnded is unreliable on UWP — complete based on the chime's
                // real duration (+ buffer for MF start latency) instead.
                double seconds = 3.0;
                try
                {
                    var props = await file.Properties.GetMusicPropertiesAsync();
                    if (props.Duration.TotalMilliseconds > 0)
                        seconds = props.Duration.TotalMilliseconds / 1000.0;
                }
                catch (Exception ex) { Log.Warn("Boot chime: duration read failed", ex); }
                await Task.Delay((int)(seconds * 1000.0) + 400);
                _bootChimePlayer = null;
                tcs.TrySetResult(true);
            }
            catch (Exception ex)
            {
                Log.Warn("Failed to play boot chime", ex);
                tcs.TrySetResult(true);
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
                    Log.Info("Splash overlay removed");
                };

                storyboard.Begin();
            };
            timer.Start();

            Log.Info("Splash overlay shown");
        }

        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            Log.Err("Navigation FAILED to {Page}: {Error}", e.Exception, e.SourcePageType?.Name);
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName, e.Exception);
        }

        void OnSuspending(object sender, SuspendingEventArgs e)
        {
            Log.Info("App suspending");
            var deferral = e.SuspendingOperation.GetDeferral();
            try
            {
                GamepadInput?.Stop();
                Log.Dbg("GamepadInputService stopped");

                var rootGrid = Windows.UI.Xaml.Window.Current.Content as Windows.UI.Xaml.Controls.Grid;
                var frame = rootGrid?.Children[0] as Windows.UI.Xaml.Controls.Frame;
                if (frame?.Content is Controls.MillerColumnsPage millerPage)
                {
                    millerPage.StopAllTimers();
                    Log.Dbg("MillerColumnsPage timers stopped");
                }
            }
            catch (Exception ex)
            {
                Log.Err("Error during suspend", ex);
            }
            deferral.Complete();
        }

        void OnResuming(object sender, object e)
        {
            Log.Info("App resuming");
            try
            {
                GamepadInput?.Start();
                Window.Current.CoreWindow.PointerCursor = null;
                Log.Dbg("GamepadInputService restarted");
            }
            catch (Exception ex)
            {
                Log.Err("Error during resume", ex);
            }
        }
    }
}
