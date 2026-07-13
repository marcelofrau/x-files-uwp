# X-Files

Gamepad-first file browser for Xbox (UWP), inspired by yazi's Miller-column UX
(Parent | Current | Preview, live preview) — no code/core shared with yazi, just UX
inspiration, reimplemented natively in C#/XAML.

**Status:** scaffold phase. No feature logic implemented yet.

## Start here

- [`docs/SPEC.md`](docs/SPEC.md) — what this app does, MVP scope
- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — layers, data flow, column model
- [`docs/ROADMAP.md`](docs/ROADMAP.md) — phased implementation plan with done criteria
- [`docs/DECISIONS.md`](docs/DECISIONS.md) — why XAML, why SharpCompress, why no yazi core
- [`AGENTS.md`](AGENTS.md) — guide for agents/contributors working on this repo

## Requirements

- Visual Studio 2022 with the "Universal Windows Platform development" workload
  (Windows only — this project cannot be built on Linux/macOS)
- Xbox console with Developer Mode enabled, for real-device testing
  (see [`docs/DEPLOY-XBOX.md`](docs/DEPLOY-XBOX.md))

## Sibling project

`../dosbox-pure-uwp` — some infra patterns (gamepad input abstraction, P/Invoke directory
scanning, manifest capabilities) were used as reference/inspiration. No shared code
(different language/stack).

## License

[GPL-3.0](LICENSE) — free software; you can redistribute it and/or modify it under the
terms of the GNU General Public License as published by the Free Software Foundation,
either version 3 of the License, or (at your option) any later version.
