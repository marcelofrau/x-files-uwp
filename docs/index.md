---
title: Home
---

<div class="hero">
  <img class="hero-img" src="images/social-preview.jpg" alt="X-Files on Xbox">
  <h1>🕹️ X-Files</h1>
  <p>
    Gamepad-first file browser for Xbox — a native Miller-column explorer inspired by yazi,
    built with C#/XAML for the Xbox Series S/X. Browse, preview, and play your files without
    ever leaving the couch.
  </p>
  <div class="pills">
    <span class="pill">🎮 Gamepad-first</span>
    <span class="pill">🎵 Native chiptune player</span>
    <span class="pill">🎼 Background music</span>
    <span class="pill">📊 31 visualizers</span>
    <span class="pill">📱 QR file sharing</span>
    <span class="pill">📄 PDF viewer</span>
    <span class="pill">✏️ Text editor</span>
  </div>
</div>

## Explore the docs

<div class="card-grid">
  <a class="card" href="SPEC.html">
    <h3>📋 Spec</h3>
    <p>Functional specification, MVP scope, and done criteria.</p>
  </a>
  <a class="card" href="ARCHITECTURE.html">
    <h3>🏗️ Architecture</h3>
    <p>Layered design, data flow, and the Miller column model.</p>
  </a>
  <a class="card" href="FILEBROWSER.html">
    <h3>📁 File Browser</h3>
    <p>FileEntry model, P/Invoke directory scanning, and sorting.</p>
  </a>
  <a class="card" href="ARCHIVES.html">
    <h3>🗜️ Archives</h3>
    <p>zip/7z/rar via SharpCompress, archives as virtual folders.</p>
  </a>
  <a class="card" href="GAMEPAD.html">
    <h3>🎮 Gamepad</h3>
    <p>Button mapping, the INavigable contract, and input routing.</p>
  </a>
  <a class="card" href="AUDIO-VISUALIZERS.html">
    <h3>🌈 Visualizers</h3>
    <p>31 Win2D audio visualizers and their registry.</p>
  </a>
  <a class="card" href="ROM-FORMATS.html">
    <h3>🕹️ ROM Formats</h3>
    <p>ROM header parsing, systems, extensions, and cache.</p>
  </a>
  <a class="card" href="FILE-SHARING-QR.html">
    <h3>📱 QR Sharing</h3>
    <p>Share files to your phone via gofile.io QR codes.</p>
  </a>
  <a class="card" href="DEPLOY-XBOX.html">
    <h3>🖥️ Deploy to Xbox</h3>
    <p>Developer Mode, Device Portal, and sideload steps.</p>
  </a>
  <a class="card" href="RELEASE.html">
    <h3>🚀 Release</h3>
    <p>Release process, CI/CD workflow, and versioning.</p>
  </a>
  <a class="card" href="DECISIONS.html">
    <h3>🧠 Decisions</h3>
    <p>ADRs — why XAML, why SharpCompress, why SQLite.</p>
  </a>
  <a class="card" href="bgm/README.html">
    <h3>🎼 Background Music</h3>
    <p>BGM spec, architecture, and implementation checklist.</p>
  </a>
</div>

## Screenshots

<div class="shot-grid">
  <a href="screenshots/xfiles-11-music-player-full.jpg"><img src="screenshots/xfiles-11-music-player-full.jpg" alt="Music player"></a>
  <a href="screenshots/xfiles-45-video-player.jpg"><img src="screenshots/xfiles-45-video-player.jpg" alt="Video player"></a>
  <a href="screenshots/xfiles-12-viz1.jpg"><img src="screenshots/xfiles-12-viz1.jpg" alt="Visualizer"></a>
  <a href="screenshots/xfiles-25-fileops.jpg"><img src="screenshots/xfiles-25-fileops.jpg" alt="File operations"></a>
  <a href="screenshots/xfiles-34-roms.jpg"><img src="screenshots/xfiles-34-roms.jpg" alt="ROM preview"></a>
  <a href="screenshots/xfiles-36-editor.jpg"><img src="screenshots/xfiles-36-editor.jpg" alt="Text editor"></a>
</div>

## Get started

- 🖥️ [Deploy to your Xbox](DEPLOY-XBOX.html) — Developer Mode, Device Portal, sideload.
- 🎮 [Gamepad controls](GAMEPAD.html) — every button mapped.
- 📖 [User guide](https://github.com/marcelofrau/x-files-uwp/wiki) — end-user tutorials on the wiki.
- ⬇️ [Download releases](https://github.com/marcelofrau/x-files-uwp/releases) — grab the latest package.
