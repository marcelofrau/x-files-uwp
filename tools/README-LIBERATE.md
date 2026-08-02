# Loopback Liberation Tools

UWP apps on Xbox cannot reach the console's own Device Portal
(`https://[::1]:11443`) due to loopback network isolation. X-Files needs that
channel to browse `LocalAppData` of other apps (see
[`docs/PORTAL-APPDATA.md`](../docs/PORTAL-APPDATA.md)).

These scripts grant the loopback exemption from your PC over SSH, using the
same credential flow as xbHomebrewVault:

1. **Portal credentials** fetch the **rotating SSH password** from
   `GET /ext/smb/developerfolder`
2. The target app's **Package Family Name (PFN)** is discovered from
   `GET /api/app/packagemanager/packages` (no need to hardcode the publisher hash)
3. `checknetisolation loopbackexempt -a -n=<PFN>` runs over SSH (`DevToolsUser`, port 22)

## Requirements

| Tool | Where |
|---|---|
| PowerShell 7 (`pwsh`) | `.ps1` (uses `-SkipCertificateCheck`) |
| `bash` | `.sh` |
| `plink` (PuTTY) | optional, `.ps1` — auto SSH password. Falls back to interactive `ssh` |
| `sshpass` | optional, `.sh` — auto SSH password. Falls back to interactive `ssh` |
| `curl` | `.sh` |
| `jq` or `python3` | optional, `.sh` — PFN auto-discovery. Falls back to manual PFN entry |

## Usage

```pwsh
# Windows
pwsh ./liberate-loopback.ps1 -Ip <XBOX-IP> -User <portal-user>
```

```bash
# Linux / macOS
./liberate-loopback.sh -ip <XBOX-IP> -user <portal-user>
```

Everything can be interactive — omit `-ip`/`-user`/`-pass` and the script asks.

### Flags (both scripts)

| Flag | Meaning |
|---|---|
| `-ip <ip>` | Xbox IP/hostname |
| `-user <u>` | Device Portal username |
| `-pass <p>` | Device Portal password (else prompted securely) |
| `-app <name>` | Installed app `Name` (default `XFiles.Xbox`) |
| `-pfn <pfn>` | Skip discovery, use this PFN directly |
| `-undo` | Remove the exemption (`-d`) instead of adding it (`-a`) |
| `-check` | Only verify current exemption state |
| `-portalPort`, `-sshPort`, `-sshUser` | Override defaults (11443 / 22 / `DevToolsUser`) |

### Examples

```pwsh
# Just apply for X-Files (all prompts interactive)
./liberate-loopback.ps1

# Apply for any installed app by name
./liberate-loopback.sh -ip <XBOX-IP> -user <portal-user> -app RetroArch

# Undo
./liberate-loopback.ps1 -pfn XFiles.Xbox_jgz7qwhvc5jpc -undo

# Verify without changing anything
./liberate-loopback.sh -check
```

## When to re-run

The exemption survives app relaunch but is **lost** on:

- Re-installing the app (new deploy)
- Console reboot

After either, re-run the script (or press **Y** on the X-Files About screen to
re-probe the portal). It prints `OK: exemption applied` when done.

## Manual fallback

No script? Run it by hand — full steps in
[`docs/PORTAL-APPDATA.md`](../docs/PORTAL-APPDATA.md) ("Manual SSH liberation").
The short version:

```pwsh
# 1. Get the rotating SSH password
curl -k -u <portal-user>:<portal-password> "https://<XBOX-IP>:11443/ext/smb/developerfolder"

# 2. SSH in and exempt
ssh DevToolsUser@<XBOX-IP>
checknetisolation loopbackexempt -a -n=XFiles.Xbox_jgz7qwhvc5jpc
checknetisolation loopbackexempt -s   # verify
```
