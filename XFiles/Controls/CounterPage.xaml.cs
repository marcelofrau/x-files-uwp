using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;
using XFiles.Navigation;

namespace XFiles.Controls
{
    /// <summary>
    /// Mock page for Phase 2: validates the gamepad input pipeline.
    /// Keyboard/mouse added for desktop debugging — gamepad remains primary target.
    /// </summary>
    public sealed partial class CounterPage : Page, INavigable
    {
        private int _counter;
        private GamepadInputService _input;

        public CounterPage()
        {
            this.InitializeComponent();
            this.KeyDown += OnKeyDown;
            this.PointerPressed += OnPointerPressed;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _input = e.Parameter as GamepadInputService;
            if (_input != null)
            {
                _input.ActiveNavigable = this;
            }
            this.Focus(FocusState.Programmatic);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            if (_input != null && _input.ActiveNavigable == this)
            {
                _input.ActiveNavigable = null;
            }
        }

        private void OnKeyDown(object sender, KeyRoutedEventArgs e)
        {
            switch (e.Key)
            {
                case VirtualKey.Up:        OnDPadUp(); break;
                case VirtualKey.Down:      OnDPadDown(); break;
                case VirtualKey.Left:      OnDPadLeft(); break;
                case VirtualKey.Right:     OnDPadRight(); break;
                case VirtualKey.Enter:
                case VirtualKey.Space:     OnConfirm(); break;
                case VirtualKey.Escape:
                case VirtualKey.Back:      OnBack(); break;
                case VirtualKey.Y:         OnContextMenu(); break;
                case VirtualKey.PageUp:    OnPageUp(); break;
                case VirtualKey.PageDown:  OnPageDown(); break;
            }
        }

        private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var props = e.GetCurrentPoint(this).Properties;
            if (props.IsLeftButtonPressed)       OnConfirm();
            else if (props.IsRightButtonPressed)  OnBack();
            else if (props.IsMiddleButtonPressed) OnContextMenu();
        }

        private void UpdateDisplay(string action = null)
        {
            CounterText.Text = _counter.ToString();
            if (action != null)
            {
                LastInputText.Text = action;
            }
        }

        // INavigable implementation

        public void OnDPadUp()
        {
            _counter++;
            UpdateDisplay("DPad Up");
        }

        public void OnDPadDown()
        {
            _counter--;
            UpdateDisplay("DPad Down");
        }

        public void OnDPadLeft()
        {
            _counter -= 10;
            UpdateDisplay("DPad Left (-10)");
        }

        public void OnDPadRight()
        {
            _counter += 10;
            UpdateDisplay("DPad Right (+10)");
        }

        public void OnConfirm()
        {
            _counter = 0;
            UpdateDisplay("A — Reset");
        }

        public void OnBack()
        {
            _counter *= -1;
            UpdateDisplay("B — Negate");
        }

        public void OnContextMenu()
        {
            _counter += 100;
            UpdateDisplay("Y — +100");
        }

        public void OnPageUp()
        {
            _counter += 50;
            UpdateDisplay("LB — Page Up (+50)");
        }

        public void OnPageDown()
        {
            _counter -= 50;
            UpdateDisplay("RB — Page Down (-50)");
        }

        public void OnScrollHorizontal(double delta) { }

        public void OnScrollVertical(double delta) { }
    }
}
