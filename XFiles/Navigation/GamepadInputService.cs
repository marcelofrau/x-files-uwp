using System;
using System.Threading;
using Windows.Gaming.Input;
using Windows.System;
using Windows.UI.Xaml;

namespace XFiles.Navigation
{
    public sealed class GamepadInputService
    {
        private readonly DispatcherTimer _timer;
        private readonly DispatcherQueue _dispatcherQueue;
        private volatile Gamepad _gamepad;
        private GamepadReading _prevReading;
        private GamepadButtons _prevButtons;
        private System.Diagnostics.Stopwatch _sw;

        // Background poll thread: reads the gamepad at ~125Hz and enqueues an
        // immediate UI dispatch on any button edge. The Xbox DispatcherTimer fires
        // at ~50ms (Dev Mode composition clock), so without this a button press can
        // wait up to a full tick before the app notices it.
        private Thread _pollThread;
        private volatile bool _pollRunning;
        private volatile GamepadButtons _pollButtons;
        private int _dispatchPending; // 0/1 via Interlocked — coalesce to one in-flight UI dispatch
#if INPUT_LATENCY_DEBUG
        private long _lastLatLogMs;
#endif

        // Analog stick deadzone
        private const double Deadzone = 0.5;

        // Active navigable target
        public INavigable ActiveNavigable { get; set; }

        // Observable controller state
        public bool IsControllerConnected => _gamepad != null;
        public event EventHandler<bool> ControllerConnectedChanged;

        public GamepadInputService()
        {
            Log.Info("GamepadInputService creating — poll interval=33ms (~30fps), deadzone={Deadzone}", Deadzone);

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(33)
            };
            _timer.Tick += OnTick;
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            _sw = System.Diagnostics.Stopwatch.StartNew();
            _lastDpadTickMs = 0;
            _lastProbeWarnMs = 0;

            RefreshGamepad();

            Gamepad.GamepadAdded += OnGamepadAdded;
            Gamepad.GamepadRemoved += OnGamepadRemoved;
            Log.Info("GamepadInputService created — GamepadAdded/Removed hooks registered");
        }

        public void Start()
        {
            Log.Info("GamepadInputService.Start() — starting background poll thread (8ms)");
            _timer.Start();
            _pollRunning = true;
            _pollThread = new Thread(PollLoop)
            {
                IsBackground = true,
                Name = "GamepadPoll"
            };
            _pollThread.Start();
        }

        public void Stop()
        {
            Log.Info("GamepadInputService.Stop()");
            _pollRunning = false;
            _timer.Stop();
        }

        /// <summary>
        /// High-frequency reader on a background thread. Detects button-state edges
        /// and enqueues an immediate UI dispatch, bypassing the DispatcherTimer's
        /// ~50ms floor on Xbox. The timer remains the periodic processor (repeats,
        /// sticks, holds) — this only cuts the edge-detection latency.
        /// </summary>
        private void PollLoop()
        {
            while (_pollRunning)
            {
                try
                {
                    var g = _gamepad;
                    if (g != null)
                    {
                        var reading = g.GetCurrentReading();
                        if (reading.Buttons != _pollButtons)
                        {
                            _pollButtons = reading.Buttons;
                            RequestDispatch();
                        }
                    }
                }
                catch
                {
                    // Gamepad removed mid-read — next poll refreshes state.
                }
                Thread.Sleep(8);
            }
        }

        private void RequestDispatch()
        {
            if (Interlocked.Exchange(ref _dispatchPending, 1) != 0) return;
#if INPUT_LATENCY_DEBUG
            long enqueueMs = _sw.ElapsedMilliseconds;
#endif
            if (!_dispatcherQueue.TryEnqueue(() =>
                {
                    _dispatchPending = 0;
#if INPUT_LATENCY_DEBUG
                    long nowMs = _sw.ElapsedMilliseconds;
                    if (nowMs - _lastLatLogMs > 1000)
                    {
                        // Proves whether the early dispatch beats the 50ms timer on Xbox.
                        Log.Info("INPUT-LAT: edge→UI dispatch {Dt}ms", nowMs - enqueueMs);
                        _lastLatLogMs = nowMs;
                    }
#endif
                    OnTick(null, null);
                }))
            {
                _dispatchPending = 0; // queue unavailable — don't wedge
            }
        }

        private void OnGamepadAdded(object sender, Gamepad e)
        {
            Log.Info("Gamepad.GamepadAdded event fired");
            RefreshGamepad();
        }

        private void OnGamepadRemoved(object sender, Gamepad e)
        {
            Log.Info("Gamepad.GamepadRemoved event fired");
            RefreshGamepad();
        }

        private void RefreshGamepad()
        {
            var gamepads = Gamepad.Gamepads;
            var wasConnected = _gamepad != null;

            if (gamepads.Count > 0)
            {
                _gamepad = gamepads[0];
                _prevButtons = GamepadButtons.None;
                _prevReading = default;
            }
            else
            {
                _gamepad = null;
            }

            if (wasConnected != (_gamepad != null))
            {
                ControllerConnectedChanged?.Invoke(this, _gamepad != null);
            }
        }

        private int _tickCount;

        private void OnTick(object sender, object e)
        {
            if (_gamepad == null)
            {
                RefreshGamepad();
                return;
            }

            GamepadReading reading;
            try
            {
                reading = _gamepad.GetCurrentReading();
            }
            catch (Exception ex)
            {
                Log.Warn("GetCurrentReading failed — refreshing gamepad", ex);
                RefreshGamepad();
                return;
            }

            // Sync the poll thread's view so it doesn't re-enqueue the same state.
            _pollButtons = reading.Buttons;

            var nav = ActiveNavigable;
            if (nav == null)
            {
                _prevReading = reading;
                _prevButtons = reading.Buttons;
                return;
            }

            _dpadNavigatedThisTick = false;

            // Time since the previous poll tick (real wall-clock). Used for the D-pad
            // repeat cooldown and long-press thresholds so timing stays correct even
            // when UI-thread work delays the 16ms DispatcherTimer.
            long nowMs = _sw.ElapsedMilliseconds;
            long elapsedMs = nowMs - _lastDpadTickMs;
            if (elapsedMs < 1) elapsedMs = 1;

#if GAMEPAD_POLL_DEBUG
            // Tick-cadence probe: a large gap means the UI thread was busy and input
            // was starved. Rate-limited to one warning/second to avoid log flooding.
            if (_lastDpadTickMs > 0 && nowMs - _lastProbeWarnMs > 1000)
            {
                long gap = nowMs - _lastDpadTickMs;
                if (gap > 80)
                {
                    Log.Warn("POLL: tick gap {Gap}ms since tick #{Tick} — UI thread busy?", gap, _tickCount);
                    _lastProbeWarnMs = nowMs;
                }
            }
#endif

            _lastDpadTickMs = nowMs;

            var pressed = reading.Buttons;
            var justPressed = (pressed ^ _prevButtons) & pressed;
            var justReleased = (pressed ^ _prevButtons) & _prevButtons;

#if GAMEPAD_INPUT_DEBUG
            if (justPressed != 0) Log.Info("INPUT-DBG: justPressed={Buttons} at +{T}ms readTs={Ts}", justPressed, Environment.TickCount, reading.Timestamp);
            if (justReleased != 0) Log.Info("INPUT-DBG: justReleased={Buttons} at +{T}ms readTs={Ts}", justReleased, Environment.TickCount, reading.Timestamp);
            double dbgLx = reading.LeftThumbstickX, dbgLy = reading.LeftThumbstickY;
            double dbgRx = reading.RightThumbstickX, dbgRy = reading.RightThumbstickY;
            if (Math.Abs(dbgLx) > 0.3 || Math.Abs(dbgLy) > 0.3 || Math.Abs(dbgRx) > 0.3 || Math.Abs(dbgRy) > 0.3)
                Log.Info("INPUT-DBG: sticks L=({Lx:F2},{Ly:F2}) R=({Rx:F2},{Ry:F2})", dbgLx, dbgLy, dbgRx, dbgRy);
#endif

            // Log raw button state at Verbose every 300 ticks (~5s)
            _tickCount++;
#if GAMEPAD_POLL_DEBUG
            if (_tickCount % 300 == 0)
            {
                Log.Verb("Tick {Tick}: buttons={Buttons}, LStick=({LX:F2},{LY:F2}), RStick=({RX:F2},{RY:F2})",
                    _tickCount, pressed, reading.LeftThumbstickX, reading.LeftThumbstickY,
                    reading.RightThumbstickX, reading.RightThumbstickY);
            }
#endif

            // D-pad — initial press fires immediately, then repeats while held
            var dpadNow = pressed & (GamepadButtons.DPadUp | GamepadButtons.DPadDown | GamepadButtons.DPadLeft | GamepadButtons.DPadRight);
            var dpadJustPressed = dpadNow & ~_dpadHeld;
            var dpadJustReleased = ~dpadNow & _dpadHeld;
            _dpadNavigatedThisTick = false;

#if GAMEPAD_POLL_DEBUG
            // State-change only (press/release) to keep the hot path quiet
            if (dpadJustPressed != 0 || dpadJustReleased != 0)
            {
                Log.Verb("DPAD state: now={Now} justPressed={JP} justReleased={JR} held={Held} cooldown={Cd}",
                    dpadNow, dpadJustPressed, dpadJustReleased, _dpadHeld, _dpadRepeatCooldown);
            }
#endif

            if ((dpadJustPressed & GamepadButtons.DPadUp) != 0)
            {
#if GAMEPAD_INPUT_DEBUG
                Log.Info("INPUT: DPad Up initial press");
#endif
                nav.OnDPadUp(isRepeat: false);
                _dpadRepeatCooldown = DpadInitialDelay;
                _dpadNavigatedThisTick = true;
            }
            if ((dpadJustPressed & GamepadButtons.DPadDown) != 0)
            {
#if GAMEPAD_INPUT_DEBUG
                Log.Info("INPUT: DPad Down initial press");
#endif
                nav.OnDPadDown(isRepeat: false);
                _dpadRepeatCooldown = DpadInitialDelay;
                _dpadNavigatedThisTick = true;
            }
            if ((dpadJustPressed & GamepadButtons.DPadLeft) != 0)
            {
#if GAMEPAD_INPUT_DEBUG
                Log.Verb("DPAD: initial press Left");
#endif
                nav.OnDPadLeft();
                _dpadRepeatCooldown = DpadInitialDelay;
                _dpadNavigatedThisTick = true;
            }
            if ((dpadJustPressed & GamepadButtons.DPadRight) != 0)
            {
#if GAMEPAD_INPUT_DEBUG
                Log.Verb("DPAD: initial press Right");
#endif
                nav.OnDPadRight();
                _dpadRepeatCooldown = DpadInitialDelay;
                _dpadNavigatedThisTick = true;
            }

            // Repeat while held (skip if initial press already handled this tick)
            if (_dpadRepeatCooldown > 0) _dpadRepeatCooldown -= elapsedMs;
            if (_dpadRepeatCooldown <= 0 && dpadNow != 0 && !_dpadNavigatedThisTick)
            {
                if ((dpadNow & GamepadButtons.DPadUp) != 0)
                {
#if GAMEPAD_POLL_DEBUG
                    Log.Info("DPAD: repeat Up (cooldown={Cd})", _dpadRepeatCooldown);
#endif
                    nav.OnDPadUp(isRepeat: true);
                }
                else if ((dpadNow & GamepadButtons.DPadDown) != 0)
                {
#if GAMEPAD_POLL_DEBUG
                    Log.Info("DPAD: repeat Down (cooldown={Cd})", _dpadRepeatCooldown);
#endif
                    nav.OnDPadDown(isRepeat: true);
                }
                else if ((dpadNow & GamepadButtons.DPadLeft) != 0)
                {
#if GAMEPAD_POLL_DEBUG
                    Log.Info("DPAD: repeat Left (cooldown={Cd})", _dpadRepeatCooldown);
#endif
                    nav.OnDPadLeft();
                }
                else if ((dpadNow & GamepadButtons.DPadRight) != 0)
                {
#if GAMEPAD_POLL_DEBUG
                    Log.Info("DPAD: repeat Right (cooldown={Cd})", _dpadRepeatCooldown);
#endif
                    nav.OnDPadRight();
                }
                _dpadRepeatCooldown = DpadRepeatInterval;
                _dpadNavigatedThisTick = true;
            }

            _dpadHeld = dpadNow;

#if INPUT_LATENCY_DEBUG
            // Release seen by the app — a gap between the physical release and this
            // log means the gamepad reading was stale (input continues to repeat).
            if ((justReleased & (GamepadButtons.DPadUp | GamepadButtons.DPadDown | GamepadButtons.DPadLeft | GamepadButtons.DPadRight)) != 0)
            {
                Log.Info("INPUT: D-pad released at +{T}ms — reading now shows {Buttons}", Environment.TickCount, pressed);
            }
#endif

            // A, B — just pressed only
            if ((justPressed & GamepadButtons.A) != 0)
            {
#if GAMEPAD_INPUT_DEBUG
                Log.Info("INPUT: A (Confirm)");
#endif
                nav.OnConfirm();
            }
            if ((justPressed & GamepadButtons.B) != 0)
            {
#if GAMEPAD_INPUT_DEBUG
                Log.Info("INPUT: B (Back)");
#endif
                nav.OnBack();
            }

            // Y — long-press detection (500ms)
            const double YLongPressMs = 500;
            if ((justPressed & GamepadButtons.Y) != 0)
            {
#if GAMEPAD_INPUT_DEBUG
                Log.Info("INPUT: Y pressed — starting hold timer");
#endif
                _yHeld = true;
                _yHeldMs = 0;
                _yLongPressHandled = false;
            }
            if (_yHeld && (pressed & GamepadButtons.Y) != 0)
            {
                _yHeldMs += elapsedMs;
                if (_yHeldMs >= YLongPressMs && !_yLongPressHandled)
                {
#if GAMEPAD_INPUT_DEBUG
                    Log.Verb("Button: Y long-press triggered");
#endif
                    nav.OnContextMenuLongPress();
                    _yLongPressHandled = true;
                }
            }
            if ((justReleased & GamepadButtons.Y) != 0)
            {
                if (_yHeld && !_yLongPressHandled)
                {
#if GAMEPAD_INPUT_DEBUG
                    Log.Verb("Button: Y short press (Context)");
#endif
                    nav.OnContextMenu();
                }
                _yHeld = false;
                _yHeldMs = 0;
                _yLongPressHandled = false;
            }

            // X — refresh current directory
            if ((justPressed & GamepadButtons.X) != 0)
            {
#if GAMEPAD_INPUT_DEBUG
                Log.Info("INPUT-DBG: X pressed (Refresh dispatch)");
#endif
                nav.OnRefresh();
            }

            // Start — settings
            if ((justPressed & GamepadButtons.Menu) != 0)
            {
                nav.OnSettings();
            }

            // View/Select — long-press detection (500ms)
            const double ViewLongPressMs = 500;
            if ((justPressed & GamepadButtons.View) != 0)
            {
#if GAMEPAD_INPUT_DEBUG
                Log.Info("INPUT: View pressed — starting hold timer");
#endif
                _viewHeld = true;
                _viewHeldMs = 0;
                _viewLongPressHandled = false;
                _viewCtxFullscreen = nav.IsMediaFullscreen || nav.IsMediaPlayerActive;
            }
            if (_viewHeld && (pressed & GamepadButtons.View) != 0)
            {
                _viewHeldMs += elapsedMs;
                if (_viewHeldMs >= ViewLongPressMs && !_viewLongPressHandled)
                {
#if GAMEPAD_INPUT_DEBUG
                    Log.Info("Button: View long-press triggered");
#endif
                    if (_viewCtxFullscreen)
                        nav.OnSelectVisualizerMenu();
                    _viewLongPressHandled = true;
                }
            }
            if ((justReleased & GamepadButtons.View) != 0)
            {
                if (_viewHeld && !_viewLongPressHandled)
                {
#if GAMEPAD_INPUT_DEBUG
                    Log.Info("Button: View short press");
#endif
                    if (_viewCtxFullscreen)
                        nav.OnSelectVisualizer();
                    else
                        nav.OnToggleBatch();
                }
                _viewHeld = false;
                _viewHeldMs = 0;
                _viewLongPressHandled = false;
                _viewCtxFullscreen = false;
            }

            // LB, RB — one navigation step on press; continuous seek while held
            if ((justPressed & GamepadButtons.LeftShoulder) != 0)
            {
                nav.OnSeekBack();
                _shoulderSeekCooldown = 60;
            }
            if ((justPressed & GamepadButtons.RightShoulder) != 0)
            {
                nav.OnSeekForward();
                _shoulderSeekCooldown = 60;
            }
            if (_shoulderSeekCooldown > 0) _shoulderSeekCooldown -= 33;
            if (_shoulderSeekCooldown <= 0)
            {
                if ((pressed & GamepadButtons.LeftShoulder) != 0)
                {
                    nav.OnSeekRepeat(-5);
                    _shoulderSeekCooldown = 60;
                }
                else if ((pressed & GamepadButtons.RightShoulder) != 0)
                {
                    nav.OnSeekRepeat(5);
                    _shoulderSeekCooldown = 60;
                }
            }

            // LT, RT — continuous for image zoom, threshold for page nav
            nav.OnTriggerHeld((float)reading.LeftTrigger, (float)reading.RightTrigger);
            if (reading.LeftTrigger > 0.5 && _prevReading.LeftTrigger <= 0.5)
            {
                nav.OnPageUp();
            }
            if (reading.RightTrigger > 0.5 && _prevReading.RightTrigger <= 0.5)
            {
                nav.OnPageDown();
            }

            // Left thumbstick → D-pad or image pan
            nav.OnLeftStickMove((float)reading.LeftThumbstickX, (float)reading.LeftThumbstickY);
            HandleLeftStick(reading.LeftThumbstickX, reading.LeftThumbstickY, nav);

            // Right thumbstick → scroll preview or image pan
            nav.OnRightStickMove((float)reading.RightThumbstickX, (float)reading.RightThumbstickY);
            HandleRightStick(reading.RightThumbstickX, reading.RightThumbstickY, nav);

            _prevReading = reading;
            _prevButtons = pressed;
        }

        private bool _yHeld;
        private double _yHeldMs;
        private bool _yLongPressHandled;

        private bool _viewHeld;
        private double _viewHeldMs;
        private bool _viewLongPressHandled;
        private bool _viewCtxFullscreen;

        private double _stickAccumY;
        private double _stickAccumX;
        private double _shoulderSeekCooldown;
        private double _dpadRepeatCooldown;
        private GamepadButtons _dpadHeld;
        private bool _dpadNavigatedThisTick;
        private long _lastDpadTickMs;
        private long _lastProbeWarnMs;
        private const double DpadInitialDelay = 350;
        private const double DpadRepeatInterval = 90;
        private const double StickDeadzone = 0.18;
        private const double StickMinSpeed = 6.0;   // items/sec at deadzone edge
        private const double StickMaxSpeed = 28.0;   // items/sec at full deflection

        private void HandleLeftStick(double x, double y, INavigable nav)
        {
            if (nav.IsMediaFullscreen) return;
            if (nav.IsMediaPlayerActive) return;
            if (_dpadNavigatedThisTick) return;

            double magY = Math.Abs(y);
            double magX = Math.Abs(x);

            // Vertical navigation (list scrolling)
            if (magY > StickDeadzone)
            {
                double deflection = (magY - StickDeadzone) / (1.0 - StickDeadzone);
                deflection = Math.Min(1.0, deflection);
                double speed = StickMinSpeed + deflection * (StickMaxSpeed - StickMinSpeed);

                _stickAccumY += speed / 30.0; // 30 ticks/sec (33ms)
                int steps = Math.Min((int)_stickAccumY, 5);
                if (steps != 0)
                {
                    _stickAccumY -= steps;
                    if (y > 0)
                    {
                        for (int i = 0; i < steps; i++) nav.OnDPadUp();
                    }
                    else
                    {
                        for (int i = 0; i < steps; i++) nav.OnDPadDown();
                    }
                }
            }
            else
            {
                _stickAccumY = 0;
            }

            // Horizontal navigation (column drill in/out)
            if (magX > StickDeadzone && magY < StickDeadzone)
            {
                double deflection = (magX - StickDeadzone) / (1.0 - StickDeadzone);
                double speed = StickMinSpeed + Math.Min(1.0, deflection) * (StickMaxSpeed - StickMinSpeed);

                _stickAccumX += speed / 30.0;
                int steps = Math.Min((int)_stickAccumX, 5);
                if (steps != 0)
                {
                    _stickAccumX -= steps;
                    if (x < 0)
                    {
                        for (int i = 0; i < steps; i++) nav.OnDPadLeft();
                    }
                    else
                    {
                        for (int i = 0; i < steps; i++) nav.OnDPadRight();
                    }
                }
            }
            else
            {
                _stickAccumX = 0;
            }
        }

        private void HandleRightStick(double x, double y, INavigable nav)
        {
            const double ScrollDeadzone = 0.15;
            const double ScrollSpeed = 40.0;

            if (Math.Abs(y) > ScrollDeadzone)
            {
                double delta = -y * ScrollSpeed;
                nav.OnScrollVertical(delta);
            }
            if (Math.Abs(x) > ScrollDeadzone)
            {
                double delta = x * ScrollSpeed;
                nav.OnScrollHorizontal(delta);
            }
        }
    }
}
