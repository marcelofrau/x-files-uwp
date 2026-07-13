# Theming/

- `RetroTheme.xaml` — ResourceDictionary with custom Brushes/Typography/Styles (no default
  Fluent Design chrome anywhere). Scaffold version exists now with basic brush placeholders;
  full ControlTemplate set arrives in Phase 8. See docs/UI-THEMING.md.
- `AppTheme.cs` — loads a user-editable JSON theme file from LocalFolder and applies it to
  the ResourceDictionary at runtime. Introduced in Phase 8.

See docs/ROADMAP.md and docs/DECISIONS.md (ADR-002) for rationale.
