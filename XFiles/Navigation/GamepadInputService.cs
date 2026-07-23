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
            Log.Information("GamepadInputService creating — poll interval=33ms (~30fps), deadzone={Deadzone}", Deadzone);

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(33)
            };
            _timer.Tick += OnTick;

            RefreshGamepad();

            Gamepad.GamepadAdded += OnGamepadAdded;
            Gamepad.GamepadRemoved += OnGamepadRemoved;
            Log.Information("GamepadInputService created — GamepadAdded/Removed hooks registered");
        }

        public void Start()
        {
            Log.Information("GamepadInputService.Start()");
            _timer.Start();
        }

        public void Stop()
        {
            Log.Information("GamepadInputService.Stop()");
            _timer.Stop();
        }

        private void OnGamepadAdded(object sender, Gamepad e)
        {
            Log.Information("Gamepad.GamepadAdded event fired");
            RefreshGamepad();
        }

        private void OnGamepadRemoved(object sender, Gamepad e)
        {
            Log.Information("Gamepad.GamepadRemoved event fired");
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
                Log.Warning("GetCurrentReading failed — refreshing gamepad", ex);
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
            if (_tickCount % 300 == 0)
            {
                Log.Verbose("Tick {Tick}: buttons={Buttons}, LStick=({LX:F2},{LY:F2}), RStick=({RX:F2},{RY:F2})",
                    _tickCount, pressed, reading.LeftThumbstickX, reading.LeftThumbstickY,
                    reading.RightThumbstickX, reading.RightThumbstickY);
            }

            // D-pad — initial press fires immediately, then repeats while held
            var dpadNow = pressed & (GamepadButtons.DPadUp | GamepadButtons.DPadDown | GamepadButtons.DPadLeft | GamepadButtons.DPadRight);
            var dpadJustPressed = dpadNow & ~_dpadHeld;
            var dpadJustReleased = ~dpadNow & _dpadHeld;
            _dpadNavigatedThisTick = false;

            if (dpadNow != 0 || dpadJustPressed != 0 || dpadJustReleased != 0)
            {
                Log.Verbose("DPAD state: now={Now} justPressed={JP} justReleased={JR} held={Held} cooldown={Cd}",
                    dpadNow, dpadJustPressed, dpadJustReleased, _dpadHeld, _dpadRepeatCooldown);
            }

            if ((dpadJustPressed & GamepadButtons.DPadUp) != 0)
            {
                Log.Information("DPAD: initial press Up");
                nav.OnDPadUp(isRepeat: false);
                _dpadRepeatCooldown = DpadInitialDelay;
                _dpadNavigatedThisTick = true;
            }
            if ((dpadJustPressed & GamepadButtons.DPadDown) != 0)
            {
                Log.Information("DPAD: initial press Down");
                nav.OnDPadDown(isRepeat: false);
                _dpadRepeatCooldown = DpadInitialDelay;
                _dpadNavigatedThisTick = true;
            }
            if ((dpadJustPressed & GamepadButtons.DPadLeft) != 0)
            {
                Log.Information("DPAD: initial press Left");
                nav.OnDPadLeft();
                _dpadRepeatCooldown = DpadInitialDelay;
                _dpadNavigatedThisTick = true;
            }
            if ((dpadJustPressed & GamepadButtons.DPadRight) != 0)
            {
                Log.Information("DPAD: initial press Right");
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
                    Log.Information("DPAD: repeat Up (cooldown={Cd})", _dpadRepeatCooldown);
                    nav.OnDPadUp(isRepeat: true);
                }
                else if ((dpadNow & GamepadButtons.DPadDown) != 0)
                {
                    Log.Information("DPAD: repeat Down (cooldown={Cd})", _dpadRepeatCooldown);
                    nav.OnDPadDown(isRepeat: true);
                }
                else if ((dpadNow & GamepadButtons.DPadLeft) != 0)
                {
                    Log.Information("DPAD: repeat Left (cooldown={Cd})", _dpadRepeatCooldown);
                    nav.OnDPadLeft();
                }
                else if ((dpadNow & GamepadButtons.DPadRight) != 0)
                {
                    Log.Information("DPAD: repeat Right (cooldown={Cd})", _dpadRepeatCooldown);
                    nav.OnDPadRight();
                }
                _dpadRepeatCooldown = DpadRepeatInterval;
                _dpadNavigatedThisTick = true;
            }

            _dpadHeld = dpadNow;

            // A, B, Y — just pressed only
            if ((justPressed & GamepadButtons.A) != 0)
            {
                Log.Information("Button: A (Confirm)");
                nav.OnConfirm();
            }
            if ((justPressed & GamepadButtons.B) != 0)
            {
                Log.Information("Button: B (Back)");
                nav.OnBack();
            }
            if ((justPressed & GamepadButtons.Y) != 0)
            {
                Log.Information("Button: Y (Context)");
                nav.OnContextMenu();
            }

            // X — refresh
            if ((justPressed & GamepadButtons.X) != 0)
            {
                Log.Information("Button: X (Refresh)");
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
                    nav.OnSettings();
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

        private double _stickCooldown;
        private double _shoulderSeekCooldown;
        private double _dpadRepeatCooldown;
        private GamepadButtons _dpadHeld;
        private bool _dpadNavigatedThisTick;
        private const double DpadInitialDelay = 300;
        private const double DpadRepeatInterval = 80;

        private void HandleLeftStick(double x, double y, INavigable nav)
        {
            if (_stickCooldown > 0)
            {
                _stickCooldown -= 16;
                return;
            }

            if (nav.IsMediaFullscreen) return;
            if (nav.IsMediaPlayerActive) return;
            if (_dpadNavigatedThisTick) return;

            if (Math.Abs(y) > Deadzone)
            {
                if (y > Deadzone)
                    nav.OnDPadUp();
                else
                    nav.OnDPadDown();
                _stickCooldown = 100;
            }
            else if (Math.Abs(x) > Deadzone)
            {
                if (x < -Deadzone)
                    nav.OnDPadLeft();
                else
                    nav.OnDPadRight();
                _stickCooldown = 100;
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
