> 🐛 **Move destination picker restored.** v1.3.1 fixes a regression that removed the "one folder up" option when moving files — you can pick any destination folder again.

---

## 🐛 Bug Fixes

### 📦 Move / File Operations
- **Move picker shows parent folders again** — when moving a file to another directory, the folder browser now lists the `..` (up one level) entry as it did in v1.2.0, so you can navigate to any destination folder
- **Root cause** — a deterministic-sort added in v1.3.0 was wiping the `..` parent entry from directory scans; the ordering logic is now extracted and regression-tested so it can't silently eat the parent entry again

---

## 📦 Installation

1. 📥 Download the zip file below
2. 📖 Follow the installation instructions in the [README](https://github.com/marcelofrau/x-files-uwp#installation)

---

> 🕹️ Made with ❤️ for the Xbox homebrew community
