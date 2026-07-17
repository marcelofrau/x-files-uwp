using System;
using System.Collections.Generic;
using System.Numerics;
using Windows.Gaming.Input;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace XFiles.Navigation
{
    public sealed class GamepadInputService
    {
        private readonly DispatcherTimer _timer;
        private Gamepad _gamepad;
        private GamepadReading _prevReading;
        private GamepadButtons _prevButtons;

        // Dpad repeat state
        private bool _dpadHeld;
        private GamepadButtons _dpadHeldButton;
        private int _dpadHoldTimeMs;
        private const int DpadInitialDelayMs = 400;
        private const int DpadRepeatMs = 100;
        private bool _dpadRepeatFired;

        // Analog stick deadzone
        private const double Deadzone = 0.5;

        // Active navigable target
        public INavigable ActiveNavigable { get; set; }

        // Observable controller state
        public bool IsControllerConnected => _gamepad != null;
        public event EventHandler<bool> ControllerConnectedChanged;

        public GamepadInputService()
        {
            Log.Information("GamepadInputService creating — poll interval=16ms (~60fps), deadzone={Deadzone}, dpad initialDelay={Init}ms, repeat={Repeat}ms",
                Deadzone, DpadInitialDelayMs, DpadRepeatMs);

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

            // D-pad just pressed → fire immediately
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

            // D-pad repeat while held
            HandleDpadRepeat(pressed, justPressed, justReleased, nav);

            // Left thumbstick → map to D-pad when beyond deadzone
            HandleLeftStick(reading.LeftThumbstickX, reading.LeftThumbstickY, nav);

            _prevReading = reading;
            _prevButtons = pressed;
        }

        private void HandleDpadRepeat(GamepadButtons pressed, GamepadButtons justPressed,
            GamepadButtons justReleased, INavigable nav)
        {
            var dpadMask = GamepadButtons.DPadUp | GamepadButtons.DPadDown |
                           GamepadButtons.DPadLeft | GamepadButtons.DPadRight;
            var currentDpad = pressed & dpadMask;

            if (currentDpad == GamepadButtons.None)
            {
                if (_dpadHeld)
                    Log.Verbose("DpadRepeat: released after {Ms}ms", _dpadHoldTimeMs);
                _dpadHeld = false;
                _dpadHoldTimeMs = 0;
                _dpadRepeatFired = false;
                return;
            }

            if (!_dpadHeld || currentDpad != _dpadHeldButton)
            {
                Log.Verbose("DpadRepeat: started holding {Button}", currentDpad);
                _dpadHeld = true;
                _dpadHeldButton = currentDpad;
                _dpadHoldTimeMs = 0;
                _dpadRepeatFired = false;
                return;
            }

            _dpadHoldTimeMs += 16;

            if (!_dpadRepeatFired && _dpadHoldTimeMs >= DpadInitialDelayMs)
            {
                _dpadRepeatFired = true;
                _dpadHoldTimeMs = 0;
                Log.Verbose("DpadRepeat: initial delay elapsed, firing {Button}", currentDpad);
                FireDpadEvent(currentDpad, nav);
            }
            else if (_dpadRepeatFired && _dpadHoldTimeMs >= DpadRepeatMs)
            {
                _dpadHoldTimeMs = 0;
                Log.Verbose("DpadRepeat: repeat firing {Button}", currentDpad);
                FireDpadEvent(currentDpad, nav);
            }
        }

        private void FireDpadEvent(GamepadButtons button, INavigable nav)
        {
            switch (button)
            {
                case GamepadButtons.DPadUp: nav.OnDPadUp(); break;
                case GamepadButtons.DPadDown: nav.OnDPadDown(); break;
                case GamepadButtons.DPadLeft: nav.OnDPadLeft(); break;
                case GamepadButtons.DPadRight: nav.OnDPadRight(); break;
            }
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
                if (y < -Deadzone)
                {
                    Log.Verbose("Input: LeftStick Up (y={Y:F2})", y);
                    nav.OnDPadUp();
                }
                else
                {
                    Log.Verbose("Input: LeftStick Down (y={Y:F2})", y);
                    nav.OnDPadDown();
                }
                _stickCooldown = 200;
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
                _stickCooldown = 200;
            }
        }
    }
}
