# Assets/

```
Assets/
├── Icons/        # App icon (app.ico) + view-agnostic icons
├── Fonts/        # Custom .ttf (post-MVP, see UI-THEMING.md)
└── Views/        # Per-view/page icons & images
    └── ...       # One folder per view that needs assets
```

- Full guide: `docs/ASSETS-GUIDE.md`
- Skill: `.opencode/skills/assets-icons/SKILL.md`
- Personal icon set: `F:\workspace\icons8-personal-set`
- Naming: `{viewname}-{descriptor}-{size}.png` (lowercase, hifens)
- XAML ref: `ms-appx:///Assets/Views/{ViewName}/{filename}`
- Must register in `XFiles.csproj` as `<Content>` for deployment
