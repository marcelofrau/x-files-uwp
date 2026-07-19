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

            // Log raw button state at Verbose every 60 ticks (~1s)
            _tickCount++;
            if (_tickCount % 60 == 0)
            {
                Log.Verbose("Tick {Tick}: buttons={Buttons}, LStick=({LX:F2},{LY:F2}), RStick=({RX:F2},{RY:F2})",
                    _tickCount, pressed, reading.LeftThumbstickX, reading.LeftThumbstickY,
                    reading.RightThumbstickX, reading.RightThumbstickY);
            }

            // D-pad — just pressed only
            if ((justPressed & GamepadButtons.DPadUp) != 0)
            {
                Log.Verbose("Input: DPadUp (justPressed)");
                nav.OnDPadUp();
            }
            if ((justPressed & GamepadButtons.DPadDown) != 0)
            {
                Log.Verbose("Input: DPadDown (justPressed)");
                nav.OnDPadDown();
            }
            if ((justPressed & GamepadButtons.DPadLeft) != 0)
            {
                Log.Verbose("Input: DPadLeft (justPressed)");
                nav.OnDPadLeft();
            }
            if ((justPressed & GamepadButtons.DPadRight) != 0)
            {
                Log.Verbose("Input: DPadRight (justPressed)");
                nav.OnDPadRight();
            }

            // A, B, Y — just pressed only
            if ((justPressed & GamepadButtons.A) != 0)
            {
                Log.Verbose("Input: A (justPressed)");
                nav.OnConfirm();
            }
            if ((justPressed & GamepadButtons.B) != 0)
            {
                Log.Verbose("Input: B (justPressed)");
                nav.OnBack();
            }
            if ((justPressed & GamepadButtons.Y) != 0)
            {
                Log.Verbose("Input: Y (justPressed)");
                nav.OnContextMenu();
            }

            // X — refresh
            if ((justPressed & GamepadButtons.X) != 0)
            {
                Log.Verbose("Input: X (justPressed) — refresh");
                nav.OnRefresh();
            }

            // Start/Select — settings
            if ((justPressed & GamepadButtons.Menu) != 0)
            {
                Log.Verbose("Input: Menu/Start (justPressed) — settings");
                nav.OnSettings();
            }
            if ((justPressed & GamepadButtons.View) != 0)
            {
                Log.Verbose("Input: View/Select (justPressed) — settings");
                nav.OnSettings();
            }

            // LB, RB — just pressed only
            if ((justPressed & GamepadButtons.LeftShoulder) != 0)
            {
                Log.Verbose("Input: LB (justPressed)");
                nav.OnPageUp();
            }
            if ((justPressed & GamepadButtons.RightShoulder) != 0)
            {
                Log.Verbose("Input: RB (justPressed)");
                nav.OnPageDown();
            }

            // LT, RT — analog triggers, detect press via threshold crossing
            if (reading.LeftTrigger > 0.5 && _prevReading.LeftTrigger <= 0.5)
            {
                Log.Verbose("Input: LT (trigger)");
                nav.OnPageUp();
            }
            if (reading.RightTrigger > 0.5 && _prevReading.RightTrigger <= 0.5)
            {
                Log.Verbose("Input: RT (trigger)");
                nav.OnPageDown();
            }

            // Left thumbstick → map to D-pad when beyond deadzone
            HandleLeftStick(reading.LeftThumbstickX, reading.LeftThumbstickY, nav);

            // Right thumbstick → scroll preview content
            HandleRightStick(reading.RightThumbstickX, reading.RightThumbstickY, nav);

            _prevReading = reading;
            _prevButtons = pressed;
        }

        private double _stickCooldown;

        private void HandleLeftStick(double x, double y, INavigable nav)
        {
            if (_stickCooldown > 0)
            {
                _stickCooldown -= 16;
                return;
            }

            if (Math.Abs(y) > Deadzone)
            {
                if (y > Deadzone)
                {
                    Log.Verbose("Input: LeftStick Up (y={Y:F2})", y);
                    nav.OnDPadUp();
                }
                else
                {
                    Log.Verbose("Input: LeftStick Down (y={Y:F2})", y);
                    nav.OnDPadDown();
                }
                _stickCooldown = 100;
            }
            else if (Math.Abs(x) > Deadzone)
            {
                if (x < -Deadzone)
                {
                    Log.Verbose("Input: LeftStick Left (x={X:F2})", x);
                    nav.OnDPadLeft();
                }
                else
                {
                    Log.Verbose("Input: LeftStick Right (x={X:F2})", x);
                    nav.OnDPadRight();
                }
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
                Log.Verbose("Input: RightStick Vertical (y={Y:F2}, delta={Delta:F1})", y, delta);
                nav.OnScrollVertical(delta);
            }
            if (Math.Abs(x) > ScrollDeadzone)
            {
                double delta = x * ScrollSpeed;
                Log.Verbose("Input: RightStick Horizontal (x={X:F2}, delta={Delta:F1})", x, delta);
                nav.OnScrollHorizontal(delta);
            }
        }
    }
}
