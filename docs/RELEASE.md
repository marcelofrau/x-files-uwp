# Release Process

## Overview

Releases are automated via GitHub Actions (`.github/workflows/ci.yml`). When you push a
tag matching `v*`, the workflow builds, packages, and creates a GitHub Release with the
zip attached. The release notes come from `RELEASE-NOTES.md`.

## Version Scheme

Format: `major.minor.patch.build` (4 parts)

- `major.minor.patch` — semantic versioning, set manually in `version.txt`
- `build` — auto-incrementing build number from `build_counter.txt` + PreBuildEvent

Example: `1.1.0.820` → major=1, minor=1, patch=0, build=820

The tag MUST include all 4 parts: `v1.1.0.820`

## How CI/CD Works

### Workflow: `.github/workflows/ci.yml`

**Triggers:**
- Push to `main` branch → build only (no release)
- Push tag `v*` → build + package + create GitHub Release
- Pull request → build only
- Manual dispatch

**Build job:**
1. Checkout code
2. Create signing certificate (or reuse existing)
3. **Sync version from tag**: if tag is `v1.1.0.820`, sets `build_counter.txt` to 819
   (PreBuildEvent increments to 820, updates `version.txt` to `1.1.0.820`)
4. Restore NuGet packages
5. Build Release|x64 with MSBuild
6. Package with `scripts\package.ps1 -SkipBuild` (creates MSIX + zip)
7. Upload zip as artifact

**Release job** (only on tag push):
1. Download artifact
2. Create GitHub Release using `RELEASE-NOTES.md` as body
3. Attach zip file (`xfiles_{version}_{platform}.zip`)

### Key Files

| File | Purpose |
|------|---------|
| `version.txt` | Current version (4-part), updated by PreBuildEvent |
| `build_counter.txt` | Build number, incremented by PreBuildEvent on each build |
| `RELEASE-NOTES.md` | Release notes body for GitHub Release |
| `scripts/package.ps1` | Creates MSIX + distributable zip |
| `scripts/build.ps1` | Builds Release|x64 with signing cert |
| `.github/workflows/ci.yml` | GitHub Actions workflow |

## Making a New Release

### Step-by-step

```bash
# 1. Make sure all changes are committed
git status
git diff

# 2. Update RELEASE-NOTES.md with new features/fixes
#    - Write in English
#    - Use emojis for visual appeal
#    - Group by category (New Features, Bug Fixes, etc.)
#    - Mention what changed since the LAST release

# 3. Commit release notes
git add RELEASE-NOTES.md
git commit -m "docs: release notes for vX.Y.Z.B"

# 4. Push commit
git push

# 5. Create and push tag (MUST be 4-part version)
git tag vX.Y.Z.B
git push origin vX.Y.Z.B
```

The workflow takes ~2 minutes. Check progress at:
`https://github.com/marcelofrau/x-files-uwp/actions`

### What Happens Automatically

1. Workflow reads `build_counter.txt`, sets it to `target - 1`
2. PreBuildEvent increments counter, writes `version.txt` with full 4-part version
3. MSBuild builds Release|x64
4. `package.ps1` creates MSIX + zip in `AppPackages/`
5. `softprops/action-gh-release` creates GitHub Release with:
   - Title: `X-Files vX.Y.Z.B`
   - Body: contents of `RELEASE-NOTES.md`
   - Asset: `xfiles_{version}_x64.zip`

## Release Notes Guidelines

### Structure

```markdown
> 🎉 **One-line hook** — catchy summary of the biggest feature.

---

## 🆕 What's new since vX.Y.Z

### 🎮 Feature Category *(NEW)*
- Feature description
- Another feature

### 🐛 Bug Fixes
- Fix description

---

## 📦 Installation
1. 📥 Download the zip file below
2. 📖 Follow installation instructions in README

---

> 🕹️ Made with ❤️ for the Xbox homebrew community
```

### Tips

- **Lead with the best feature** — the one-liner at the top should make users excited
- **Use emojis** — they break up text and make the release visually appealing
- **Group by category** — New Features, Improvements, Bug Fixes, etc.
- **Be specific** — "35+ ROM formats supported" is better than "ROM support"
- **Migrate old notes** — each release should describe changes SINCE THE LAST RELEASE
  (the "All Features" section from v1.0.0 is kept in that release only)

### Emoji Convention

| Category | Emoji |
|----------|-------|
| New features | 🆕 or ✨ |
| Improvements | ⚡ or 🎨 |
| Bug fixes | 🩹 or 🐛 |
| Performance | 🚀 |
| UI/UX | 🎨 |
| Files/Operations | 📋 |
| Media | 🎵 🎬 |
| Archives | 📦 |
| Settings | ⚙️ |
| Logging | 📊 |

## Local Build & Package (Without CI)

If you need to build and package locally:

```powershell
# Full build + package
.\scripts\package.ps1 -Configuration Release -Platform x64

# Output: AppPackages/xfiles_{version}_x64.zip

# Or build only (no package)
.\scripts\build.ps1 -Configuration Release -Platform x64
```

The zip contains:
- `XFiles_{version}_x64.msix` — the app package
- `Dependencies/x64/*.appx` — runtime dependencies
- `xfiles.cer` — signing certificate

## Troubleshooting

### "Version mismatch" in release
Make sure the tag version matches `version.txt` after build. The workflow syncs them
automatically via `build_counter.txt`.

### Release notes not showing
The release job reads `RELEASE-NOTES.md` from the repo root. Make sure the file exists
and is committed before pushing the tag.

### Build fails on CI
Check the Actions log. Common issues:
- NuGet restore fails → check `packages.config` or NuGet.config
- MSBuild fails → check for compilation errors locally first
- Package fails → verify `version.txt` and `build_counter.txt` are in sync
