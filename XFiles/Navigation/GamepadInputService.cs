using System;
using Windows.Gaming.Input;
using Windows.UI.Xaml;

namespace XFiles.Navigation
{
    public sealed class GamepadInputService
    {
        private readonly DispatcherTimer _timer;
        private Gamepad _gamepad;
        private GamepadReading _prevReading;
        private GamepadButtons _prevButtons;

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

            RefreshGamepad();

            Gamepad.GamepadAdded += OnGamepadAdded;
            Gamepad.GamepadRemoved += OnGamepadRemoved;
            Log.Info("GamepadInputService created — GamepadAdded/Removed hooks registered");
        }

        public void Start()
        {
            Log.Info("GamepadInputService.Start()");
            _timer.Start();
        }

        public void Stop()
        {
            Log.Info("GamepadInputService.Stop()");
            _timer.Stop();
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

            var nav = ActiveNavigable;
            if (nav == null)
            {
                _prevReading = reading;
                _prevButtons = reading.Buttons;
                return;
            }

            _dpadNavigatedThisTick = false;

            var pressed = reading.Buttons;
            var justPressed = (pressed ^ _prevButtons) & pressed;
            var justReleased = (pressed ^ _prevButtons) & _prevButtons;

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
            if (dpadNow != 0 || dpadJustPressed != 0 || dpadJustReleased != 0)
            {
                Log.Verb("DPAD state: now={Now} justPressed={JP} justReleased={JR} held={Held} cooldown={Cd}",
                    dpadNow, dpadJustPressed, dpadJustReleased, _dpadHeld, _dpadRepeatCooldown);
            }
#endif

            if ((dpadJustPressed & GamepadButtons.DPadUp) != 0)
            {
                Log.Verb("DPAD: initial press Up");
                nav.OnDPadUp(isRepeat: false);
                _dpadRepeatCooldown = DpadInitialDelay;
                _dpadNavigatedThisTick = true;
            }
            if ((dpadJustPressed & GamepadButtons.DPadDown) != 0)
            {
                Log.Verb("DPAD: initial press Down");
                nav.OnDPadDown(isRepeat: false);
                _dpadRepeatCooldown = DpadInitialDelay;
                _dpadNavigatedThisTick = true;
            }
            if ((dpadJustPressed & GamepadButtons.DPadLeft) != 0)
            {
                Log.Verb("DPAD: initial press Left");
                nav.OnDPadLeft();
                _dpadRepeatCooldown = DpadInitialDelay;
                _dpadNavigatedThisTick = true;
            }
            if ((dpadJustPressed & GamepadButtons.DPadRight) != 0)
            {
                Log.Verb("DPAD: initial press Right");
                nav.OnDPadRight();
                _dpadRepeatCooldown = DpadInitialDelay;
                _dpadNavigatedThisTick = true;
            }

            // Repeat while held (skip if initial press already handled this tick)
            if (_dpadRepeatCooldown > 0) _dpadRepeatCooldown -= 33;
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

            // A, B — just pressed only
            if ((justPressed & GamepadButtons.A) != 0)
            {
                Log.Verb("Button: A (Confirm)");
                nav.OnConfirm();
            }
            if ((justPressed & GamepadButtons.B) != 0)
            {
                Log.Verb("Button: B (Back)");
                nav.OnBack();
            }

            // Y — long-press detection (500ms = ~15 ticks)
            const int YLongPressTicks = 15;
            if ((justPressed & GamepadButtons.Y) != 0)
            {
                Log.Verb("Button: Y pressed — starting hold timer");
                _yHeld = true;
                _yHeldTicks = 0;
                _yLongPressHandled = false;
            }
            if (_yHeld && (pressed & GamepadButtons.Y) != 0)
            {
                _yHeldTicks++;
                if (_yHeldTicks >= YLongPressTicks && !_yLongPressHandled)
                {
                    Log.Verb("Button: Y long-press triggered");
                    nav.OnContextMenuLongPress();
                    _yLongPressHandled = true;
                }
            }
            if ((justReleased & GamepadButtons.Y) != 0)
            {
                if (_yHeld && !_yLongPressHandled)
                {
                    Log.Verb("Button: Y short press (Context)");
                    nav.OnContextMenu();
                }
                _yHeld = false;
                _yHeldTicks = 0;
                _yLongPressHandled = false;
            }

            // X — refresh current directory
            if ((justPressed & GamepadButtons.X) != 0)
            {
                Log.Verb("Button: X (Refresh)");
                nav.OnRefresh();
            }

            // Start/Select — settings
            if ((justPressed & GamepadButtons.Menu) != 0)
            {
                nav.OnSettings();
            }
            if ((justPressed & GamepadButtons.View) != 0)
            {
                if (nav.IsMediaFullscreen || nav.IsMediaPlayerActive)
                    nav.OnSelectVisualizer();
                else
                    nav.OnToggleBatch();
            }

            // LB, RB — continuous seek while held
            if ((justPressed & GamepadButtons.LeftShoulder) != 0)
            {
                nav.OnSeekBack();
                _shoulderSeekCooldown = 0;
            }
            if ((justPressed & GamepadButtons.RightShoulder) != 0)
            {
                nav.OnSeekForward();
                _shoulderSeekCooldown = 0;
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
        private int _yHeldTicks;
        private bool _yLongPressHandled;

        private double _stickAccumY;
        private double _stickAccumX;
        private double _shoulderSeekCooldown;
        private double _dpadRepeatCooldown;
        private GamepadButtons _dpadHeld;
        private bool _dpadNavigatedThisTick;
        private const double DpadInitialDelay = 300;
        private const double DpadRepeatInterval = 80;
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

                _stickAccumY += speed / 60.0; // 60 ticks/sec (16ms)
                int steps = (int)_stickAccumY;
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

                _stickAccumX += speed / 60.0;
                int steps = (int)_stickAccumX;
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
