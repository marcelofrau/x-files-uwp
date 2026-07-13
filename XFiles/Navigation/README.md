# Navigation/

Pure navigation logic, no XAML/UI dependency:

- `INavigable.cs` — semantic contract (OnDPad/OnConfirm/OnBack/OnContextMenu/OnPageUp/
  OnPageDown). See docs/GAMEPAD.md. Introduced in Phase 2.
- `ColumnNavigator.cs` — implements INavigable, owns the 3-column state (Parent/Current/
  Preview), drill-in/out logic. See docs/ARCHITECTURE.md. Introduced in Phase 4.
- `GamepadInputService.cs` — polls Windows.Gaming.Input.Gamepad, edge-detection,
  dpad-repeat-while-held, translates raw input into INavigable calls. Introduced in
  Phase 2.

Nothing implemented yet — see docs/ROADMAP.md.
