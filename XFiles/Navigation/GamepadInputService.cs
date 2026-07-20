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
            Log.Information("GamepadInputService creating — poll interval=16ms (~60fps), deadzone={Deadzone}", Deadzone);

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16)
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

            // D-pad — just pressed only
            if ((justPressed & GamepadButtons.DPadUp) != 0)
            {
                nav.OnDPadUp();
            }
            if ((justPressed & GamepadButtons.DPadDown) != 0)
            {
                nav.OnDPadDown();
            }
            if ((justPressed & GamepadButtons.DPadLeft) != 0)
            {
                nav.OnDPadLeft();
            }
            if ((justPressed & GamepadButtons.DPadRight) != 0)
            {
                nav.OnDPadRight();
            }

            // A, B, Y — just pressed only
            if ((justPressed & GamepadButtons.A) != 0)
            {
                nav.OnConfirm();
            }
            if ((justPressed & GamepadButtons.B) != 0)
            {
                nav.OnBack();
            }
            if ((justPressed & GamepadButtons.Y) != 0)
            {
                nav.OnContextMenu();
            }

            // X — refresh
            if ((justPressed & GamepadButtons.X) != 0)
            {
                nav.OnRefresh();
            }

            // Start/Select — settings
            if ((justPressed & GamepadButtons.Menu) != 0)
            {
                nav.OnSettings();
            }
            if ((justPressed & GamepadButtons.View) != 0)
            {
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
            if (_shoulderSeekCooldown > 0) _shoulderSeekCooldown -= 16;
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

        private void HandleLeftStick(double x, double y, INavigable nav)
        {
            if (_stickCooldown > 0)
            {
                _stickCooldown -= 16;
                return;
            }

            if (nav.IsMediaFullscreen) return;

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
