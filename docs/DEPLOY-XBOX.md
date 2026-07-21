# Xbox Deploy

## Prerequisites

- **Xbox One / Xbox Series X|S** with Developer Mode enabled
- That's it. No Windows PC, no Visual Studio, no special tools.

## 1. Enable Developer Mode

1. Install the **"Dev Home"** app from Microsoft Store on your Xbox (in Retail mode).
2. Follow the registration flow (free Microsoft developer account).
3. Console restarts in Developer Mode. Dev Home shows your **IP address** and **pairing code**.

## 2. Get the Package

The `.appx` or `.appxbundle` package is provided in releases. Download it to a USB drive
or to a shared folder accessible from the Xbox browser.

## 3. Deploy via Device Portal

1. Open Xbox Dev Home → note the IP address and pairing code.
2. On any device (phone, tablet, PC) connected to the same network:
   Open browser → `https://<XBOX-IP>:11443`
3. Enter the pairing code when prompted.
4. Go to **Apps** → **Add** → select the `.appxbundle` file.
5. Install. The app appears in your Dev Mode app list.

## 4. Deploy via xbHomebrewVault (Alternative)

If you have [xbHomebrewVault](https://github.com/vektorvamp xbHomebrewVault) or similar
homebrew installer:

1. Copy the `.appx` package to a USB drive.
2. On Xbox, open xbHomebrewVault → Install from USB.
3. Select the package → Install.

## 5. Required Capabilities

Already included in the manifest — no action needed:

```xml
<Capabilities>
  <rescap:Capability Name="broadFileSystemAccess" />
  <rescap:Capability Name="runFullTrust" />
</Capabilities>
```

These allow browsing external USB drives. Without them, the app can only see its own
sandbox folder.

## Troubleshooting

- **App doesn't appear in Device Portal** → Install the signing certificate first:
  Device Portal → Certificates → upload the `.cer` file.
- **Access Denied on drives** → Confirm Developer Mode is active (not Retail with sideload).
- **Gamepad not responding** → Make sure the app is in the foreground. Connect the
  controller before launching the app, or reconnect after launch.
