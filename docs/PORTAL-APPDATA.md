# Portal AppData Access (Loopback Exemption)

X-Files can browse the console filesystem through its own UWP capabilities, but
**other apps' `LocalAppData`** (RetroArch saves/configs, DuckStation memory
cards, dosbox confs, etc.) is off-limits to a sandboxed UWP app. The one
elevated channel available on Xbox Developer Mode is the **Device Portal REST
API running on the console itself** — which the app cannot reach by default
because of loopback network isolation.

This document explains the mechanism, the manual SSH liberation, the helper
scripts, and the feature that becomes possible once the exemption is applied.

---

## 1. Why

Goal: browse `LocalAppData` of installed apps straight from X-Files on the
Xbox, e.g.:

- RetroArch → `C:\Users\DevKits\AppData\Local\Packages\RetroArch...\LocalState\` (saves, cores, configs)
- DuckStation → memory cards, BIOS, screenshots
- dosbox / PSX2 / PPSSPP → configs, memory cards, isos

The portal exposes these via:

- `GET /api/app/packagemanager/packages` → list apps + **Package Family Name** (PFN)
- `GET /api/filesystem/apps/file?knownfolderid=LocalAppData&packagefullname=<PFN>&path=<dir>` → browse files

## 2. How the loopback isolation works

Xbox (like Windows) isolates UWP app containers from local loopback: an app
cannot open a TCP connection to the console's own `::1` / `127.0.0.1`. X-Files
probes `https://[::1]:11443` (the portal) and gets a connection timeout unless
an exemption exists.

The exemption is granted by **`checknetisolation`**, which requires elevation
(outside the app container) — so it cannot be done from inside X-Files:

- A self-exempt P/Invoke (`NetworkIsolationSetAppContainerConfig`) returns
  `ACCESS_DENIED` on Xbox — the RPC is hardened, dev mode does **not** bypass it.
- SSH from inside the sandbox is blocked (all remote listeners are dropped).

Therefore the exemption is applied externally — via SSH from a PC, or from
xbHomebrewVault.

```text
┌──────────┐   loopback blocked   ┌────────────────────────────┐
│ X-Files  │ ────────────────✗──▶ │ Device Portal [::1]:11443  │
│ (UWP)    │                      │  /api/filesystem/apps/file │
└────┬─────┘                      └────────────────────────────┘
     │  checknetisolution -a (elevated, via SSH)
     ▼
  exemption granted → portal reachable → LocalAppData browsable
```

## 3. Reset behavior (confirmed by testing)

- The exemption **survives app relaunch** (verified: clean relaunch keeps the
  portal reachable).
- The exemption is **lost on re-install** (any new deploy re-registers the
  package) and on **console reboot**.

Practical consequence: re-apply after every new build install and after
rebooting the console. Not after every app launch.

## 4. Manual SSH liberation (step-by-step)

### 4.1 Get the console IP

- Xbox Dev Home → shows the IP (e.g. `10.0.0.98`)
- Or Xbox Settings → Network → Advanced → IP
- Or from any device on the LAN: `https://<XBOX-IP>:11443`

### 4.2 Portal credentials

Set on the console in Dev Home → **Device Portal**. Two values:

| Field | Example |
|---|---|
| username | `keita` |
| password | `boris12345` |

They are also the HTTP Basic auth for every portal REST call.

### 4.3 Get the rotating SSH password

The SSH password is **different from the portal password and rotates**. Two
ways to get it:

**Via REST (same as xbHomebrewVault does):**

```pwsh
curl -k -u keita:boris12345 "https://10.0.0.98:11443/ext/smb/developerfolder"
# → {"Password":"<rotating-ssh-password>", ...}
```

**Via Dev Home on the console:** the SSH/SFTP credential is shown in Dev Home.

| SSH value | Value |
|---|---|
| host | console IP |
| port | `22` |
| username | `DevToolsUser` |
| password | rotating value from above |

### 4.4 Discover the Package Family Name (PFN)

The "hash" in `XFiles.Xbox_jgz7qwhvc5jpc` (`jgz7qwhvc5jpc`) is the
**PublisherId**, derived from the signing certificate. You do **not** need to
hardcode it — the portal returns the full PFN:

```pwsh
curl -k -u keita:boris12345 "https://10.0.0.98:11443/api/app/packagemanager/packages"
# find the object where "Name" == "XFiles.Xbox", read "PackageFamilyName"
```

The default for X-Files is `XFiles.Xbox_jgz7qwhvc5jpc`.

### 4.5 Exempt

```pwsh
ssh DevToolsUser@10.0.0.98
checknetisolation loopbackexempt -a -n=XFiles.Xbox_jgz7qwhvc5jpc
# → OK.
exit
```

### 4.6 Verify

```pwsh
ssh DevToolsUser@10.0.0.98
checknetisolation loopbackexempt -s
# → look for: Name: XFiles.Xbox_jgz7qwhvc5jpc
exit
```

Inside X-Files: open **About** and run the probe (**Y**). It should show the
portal as **CONNECTED**.

### 4.7 Revert

```pwsh
ssh DevToolsUser@10.0.0.98
checknetisolation loopbackexempt -d -n=XFiles.Xbox_jgz7qwhvc5jpc
exit
```

## 5. Helper scripts

Packaged with the release zip under `tools/`:

| File | Purpose |
|---|---|
| `tools/liberate-loopback.ps1` | Windows (PowerShell 7) |
| `tools/liberate-loopback.sh` | Linux / macOS (bash) |

Both automate 4.3 → 4.6: portal creds → fetch rotating SSH password → discover
PFN from the packages endpoint → run `checknetisolation -a` → verify. See
[`tools/README-LIBERATE.md`](../tools/README-LIBERATE.md).

```pwsh
pwsh ./tools/liberate-loopback.ps1 -Ip 10.0.0.98 -User keita
```

## 6. Feature unlocked once exempted (implemented)

Once exempted, X-Files shows a **"User Folders"** entry at the root of the
Miller columns. Drill in to browse, preview, edit, and manage other apps'
`LocalAppData` / `DevelopmentFiles`:

1. **List apps** — `GET /api/app/packagemanager/packages` → installed packages as folders.
2. **Browse files** — `GET /api/filesystem/apps/file?knownfolderid=LocalAppData&packagefullname=<PFN>&path=<dir>`.
3. **Preview / edit / play** — small files (≤ 25 MB) auto-download into an
   internal cache (`portal-cache`, 2 GB LRU, cleared each launch); larger files
   download on open with a progress dialog. Text files open in the editor; save
   writes back to the portal.
4. **Manage** — Download (copy to disk), Upload file, New Folder, Rename, Delete.

### 6.1 Credentials (persisted)

- Credentials are entered once via the credentials dialog and stored in the
  app's SQLite settings (`PortalUser` / `PortalPass`) — no build-time `.env`
  needed on the console.
- Wrong creds → HTTP 401 → the dialog reappears.
- Re-probe anytime: **About + Y**.

### 6.2 If the drill-in shows the setup screen

The setup dialog explains the three exemption routes (XB Homebrew Vault wizard,
the packaged scripts, or manual SSH below) and shows a **QR code** pointing to
this page. Not connected yet? Apply the exemption, then **Re-probe**.

### 6.3 curl cheat-sheet (write ops, from any device)

Base URL `https://<XBOX-IP>:11443`, Basic auth `-u <portal-user>:<portal-pass>`.
Writes need a CSRF token: first `GET /api/os/info` (any page works) → cookie
`CSRF-Token=<token>`; then send header `-H "X-CSRF-Token: <token>"`. Omit
`packagefullname` to list packages; use `%5C` for `\`.

```bash
# list known folders
curl -k -u user:pass "https://<IP>:11443/api/filesystem/apps/knownfolders"

# list installed packages (omit packagefullname)
curl -k -u user:pass "https://<IP>:11443/api/app/packagemanager/packages"

# list files: path is backslash-quirk — root "\" = %5C, one level "\\Settings"
curl -k -u user:pass "https://<IP>:11443/api/filesystem/apps/files?knownfolderid=LocalAppData&packagefullname=<PFN>&path=%5C"

# download a file — filename is a SEPARATE param; path = parent folder only
curl -k -u user:pass -o settings.dat \
  "https://<IP>:11443/api/filesystem/apps/file?filename=settings.dat&packagefullname=<PFN>&path=%5C"

# create folder
curl -k -u user:pass -X POST -H "X-CSRF-Token: <token>" \
  "https://<IP>:11443/api/filesystem/apps/folder?newfoldername=NewDir&packagefullname=<PFN>&path=%5C"

# rename (path = parent)
curl -k -u user:pass -X POST -H "X-CSRF-Token: <token>" \
  "https://<IP>:11443/api/filesystem/apps/rename?filename=old.txt&newfilename=new.txt&packagefullname=<PFN>&path=%5C"

# delete
curl -k -u user:pass -X DELETE -H "X-CSRF-Token: <token>" \
  "https://<IP>:11443/api/filesystem/apps/file?filename=old.txt&packagefullname=<PFN>&path=%5C"
```

**Upload gotcha:** the portal only accepts the browser-style multipart format.
`curl -F "file=@x.txt"` matches it. If the server replies `500 ... WdpTempWebFolder\UPDxxxx.tmp`,
check the multipart first, then reboot dev mode / free package quota.

> Implementation details, decisions, and the manual test script live in
> [`docs/portal-appdata/PLAN.md`](portal-appdata/PLAN.md) (D4 = SQLite creds,
> D5 = cache, D6 = 25 MB auto-download, D8 = QR → this page).

Related tooling (already in the repo):

- `XFiles/Services/DevicePortalService.cs` — probe (network + filesystem +
  packages), `ProbeAsync(force)`, `ProbeStatus`, `ProbeCompleted`, plus the
  persistent portal client (read/write APIs, CSRF, 401 handling).
- About + **Y** re-probe on `MillerColumnsPage.Navigation.cs`.
- The **XB Homebrew Vault** project (`xb-homebrew-vault`) implements the same
  REST client and a "Loopback Exempt" wizard in its Tools view.
