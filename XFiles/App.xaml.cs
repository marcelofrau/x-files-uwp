using System;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
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
            this.Suspending += OnSuspending;
            this.Resuming += OnResuming;
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
                rootGrid.Children.Add(new DebugOverlay(Log.Screen));
                Window.Current.Content = rootGrid;
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
#if XRAY_ENABLED
                _xrayTimer?.Stop();
                _xray?.Dispose();
#endif
                GamepadInput?.Stop();
                Log.Debug("GamepadInputService stopped");
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
                Log.Debug("GamepadInputService restarted");
            }
            catch (Exception ex)
            {
                Log.Error("Error during resume", ex);
            }
        }
    }
}
