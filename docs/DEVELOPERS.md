---
layout: default
title: Developer Docs
---

> 🔧 Technical documentation for contributors, testers, and tinkerers.
> Not a user guide — for end-user tutorials see the [wiki](https://github.com/marcelofrau/x-files-uwp/wiki).

## Specification & Design

<div class="card-grid">
  <a class="card" href="SPEC.html">
    <h3>📋 Functional Spec</h3>
    <p>MVP scope, done criteria, and feature definitions.</p>
  </a>
  <a class="card" href="ARCHITECTURE.html">
    <h3>🏗️ Architecture</h3>
    <p>Layered design, data flow, Miller column model, Mermaid diagrams.</p>
  </a>
  <a class="card" href="DECISIONS.html">
    <h3>🧠 ADRs</h3>
    <p>Why XAML, why SharpCompress, why SQLite — recorded decisions.</p>
  </a>
  <a class="card" href="ROADMAP.html">
    <h3>🗺️ Roadmap</h3>
    <p>Implementation status and the remaining backlog.</p>
  </a>
</div>

## Core Systems

<div class="card-grid">
  <a class="card" href="FILEBROWSER.html">
    <h3>📁 File Browser</h3>
    <p>FileEntry model, P/Invoke scanning, sorting.</p>
  </a>
  <a class="card" href="ARCHIVES.html">
    <h3>🗜️ Archives</h3>
    <p>zip/7z/rar virtual folders via SharpCompress.</p>
  </a>
  <a class="card" href="AUDIO-VISUALIZATION.html">
    <h3>📊 VU Meter</h3>
    <p>AudioGraph pipeline and spectrum analyzer.</p>
  </a>
  <a class="card" href="AUDIO-VISUALIZERS.html">
    <h3>🌈 Visualizers</h3>
    <p>31 Win2D visualizers and the registry.</p>
  </a>
  <a class="card" href="ROM-FORMATS.html">
    <h3>🕹️ ROM Formats</h3>
    <p>ROM header parsing, systems, extensions, cache.</p>
  </a>
  <a class="card" href="FILETYPE-ICONS.html">
    <h3>🖼️ File-Type Icons</h3>
    <p>Papirus icon mapping and SVG-to-PNG workflow.</p>
  </a>
</div>

## Platforms & Releases

<div class="card-grid">
  <a class="card" href="DEPLOY-XBOX.html">
    <h3>🖥️ Xbox Deploy</h3>
    <p>Developer Mode, Device Portal, sideload.</p>
  </a>
  <a class="card" href="RELEASE.html">
    <h3>🚀 Release Process</h3>
    <p>CI/CD workflow and versioning scheme.</p>
  </a>
  <a class="card" href="PORTAL-APPDATA.html">
    <h3>🗃️ Portal AppData</h3>
    <p>Browse other apps' files on your Xbox.</p>
  </a>
  <a class="card" href="FILE-SHARES.html">
    <h3>🔗 File Shares</h3>
    <p>SMB/UNC feasibility notes and network unknowns.</p>
  </a>
</div>

## Engineering Practices

<div class="card-grid">
  <a class="card" href="UI-THEMING.html">
    <h3>🎨 UI Theming</h3>
    <p>ControlTemplate conventions and BladeTheme.</p>
  </a>
  <a class="card" href="LOGGING.html">
    <h3>📜 Logging</h3>
    <p>Log levels, debug flags, conventions.</p>
  </a>
  <a class="card" href="PHASE2-TESTS.html">
    <h3>🧪 Manual Tests</h3>
    <p>Gamepad input test procedures.</p>
  </a>
  <a class="card" href="ASSETS-GUIDE.html">
    <h3>🛠️ Assets Guide</h3>
    <p>Asset naming and icon workflow.</p>
  </a>
  <a class="card" href="ATTRIBUTIONS.html">
    <h3>🙏 Attributions</h3>
    <p>Third-party assets and fonts.</p>
  </a>
  <a class="card" href="SETTINGS-EXPANSION.html">
    <h3>⚙️ Settings Plan</h3>
    <p>Planned settings expansion.</p>
  </a>
</div>

## Feature Docs

<div class="card-grid">
  <a class="card" href="bgm/README.html">
    <h3>🎼 Background Music</h3>
    <p>BGM spec, architecture, implementation checklist.</p>
  </a>
  <a class="card" href="network-files/README.html">
    <h3>🌐 Network Files</h3>
    <p>SMB network access — plan, spec, ADRs, checklist.</p>
  </a>
  <a class="card" href="text-editor/SPEC.html">
    <h3>✏️ Text Editor</h3>
    <p>Editor requirements, encoding, edge cases.</p>
  </a>
  <a class="card" href="tech-debts/README.html">
    <h3>🧾 Tech Debts</h3>
    <p>Debt audit and remediation plan.</p>
  </a>
  <a class="card" href="portal-appdata/PLAN.html">
    <h3>🗃️ Portal AppData Plan</h3>
    <p>Implementation plan and checklist.</p>
  </a>
</div>
