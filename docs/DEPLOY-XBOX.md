# Xbox Deploy — Reference Guide

Based on the same flow used/documented in the sibling project `dosbox-pure-uwp` (see
`README.md` and `AGENTS.md` in that repo for specific details already validated).

## 1. Prerequisites

- Xbox console with **Developer Mode** enabled (via Microsoft Store "Dev Home" app,
  requires a registered developer account).
- Windows machine with Visual Studio 2022 ("Universal Windows Platform development"
  workload) — **this project does not build on Linux/WSL**, only the structure/docs are
  created here; compilation and real deploy must be done on a Windows machine.
- Xbox and development machine on the same local network.

## 2. Enable Developer Mode on Console

1. Install the "Dev Home" app (Microsoft Store) on Xbox in normal Retail mode.
2. Follow the registration flow (requires Microsoft developer account — free or paid
   depending on timing/region).
3. Console restarts in Developer Mode; "Dev Home" app shows the **IP address** and a
   **pairing code** for Device Portal.

## 3. Device Portal

1. In browser on Windows machine: `https://<XBOX-IP>:11443`.
2. Authenticate with the pairing code shown in "Dev Home".
3. **Apps** menu → allows installing `.appx`/`.msix` (or `.appxbundle`) packages directly
   via the web interface, or via Visual Studio (more practical during development).

## 4. Deploy via Visual Studio (Recommended During Development)

1. Open `XFiles.sln` in Visual Studio (Windows).
2. Select `x64` platform (or `ARM`/`ARM64` depending on target console — Xbox uses a
   specific internal architecture; consult the current Xbox Developer Mode documentation
   for the correct value at build time, as this changes between console generations).
3. In the deploy device dropdown, choose **Remote Machine**, enter Xbox IP
   (Developer Mode exposes a separate debugging port from the Device Portal port).
4. F5 (Debug) or Ctrl+F5 (Run without debug) — Visual Studio packages, copies and installs
   automatically.

## 5. Deploy via Device Portal (For "Release" Builds, Without Visual Studio)

1. Generate package via **Project → Publish → Create App Packages** in Visual Studio,
   selecting "Sideloading" (not "Microsoft Store").
2. In Xbox Device Portal → Apps → **Add** → select the generated `.appxbundle`/`.msix` +
   certificate file (`.cer`) if needed.
3. Install and run from the Device Portal app list or the Xbox dashboard itself
   (Developer Mode exposes a separate "Dev Mode Home" menu with sideloaded apps).

## 6. Required Capabilities in Manifest

```xml
<Capabilities>
  <rescap:Capability Name="broadFileSystemAccess" />
  <rescap:Capability Name="runFullTrust" />
</Capabilities>
```

Without these two, `FindFirstFileExFromAppW`/`GetLogicalDrives` (see `FILEBROWSER.md`)
fail silently or return access denied for any path outside the app sandbox.
Requires the `rescap` namespace declared in `Package.appxmanifest`
(`xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"`)
and the corresponding declaration in `<Dependencies>`/`TargetDeviceFamily`.

## 7. `TargetDeviceFamily`

```xml
<Dependencies>
  <TargetDeviceFamily Name="Windows.Xbox" MinVersion="10.0.0.0" MaxVersionTested="10.0.0.0" />
</Dependencies>
```

Adjust `MinVersion`/`MaxVersionTested` to the real values of the SDK installed at build
time (Visual Studio auto-fills these when changing `TargetDeviceFamily` in the project).

## 8. Common Troubleshooting Checklist

- [ ] App doesn't appear in Device Portal → verify the package signing certificate is
      trusted on the device (self-signed must be manually installed via Device Portal →
      Certificates before first deploy).
- [ ] `Access Denied` when listing drives → confirm both capabilities above and that
      Developer Mode is actually active (not just "Retail with sideload", which has
      different restrictions).
- [ ] Gamepad not detected → confirm the app is in the foreground (Xbox only delivers
      gamepad input to the focused app) and that `Gamepad.GamepadAdded` was subscribed
      before initial enumeration (common race condition: controller already connected before
      app start doesn't fire `GamepadAdded` — must also iterate `Gamepad.Gamepads` at
      startup).
