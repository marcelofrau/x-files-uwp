---
layout: default
title: Show Other Apps' Files (User Files)
---

# Show Other Apps' Files (User Files)

X-Files can open the files of your **other apps** — RetroArch saves, DuckStation
memory cards, dosbox configs, screenshots. This is called **User Files**.

It needs a **one-time unlock** that takes about **2 minutes**. Here's how to do
it, step by step. No experience needed.

---

## 1. What you need

| Thing | Where to find it |
|---|---|
| Your **console IP** (numbers like `10.0.0.98`) | Xbox Dev Home app, on the main screen |
| **Portal username** and **password** | Xbox Dev Home → **Device Portal** (turn it on if it's off) |
| A computer on the same network | any Windows / Mac / Linux PC |

> 💡 **Don't know what to type?** Write down these 3 things before starting:
> the IP, the username, and the password. You'll need them below.

---

## 2. Do the unlock (one of these ways)

### Way 1 — Easiest: XB Homebrew Vault (PC)

1. Download **XB Homebrew Vault** from
   [github.com/marcelofrau/xb-homebrew-vault](https://github.com/marcelofrau/xb-homebrew-vault).
2. Open it and **connect to your Xbox** using the IP + portal credentials.
3. Go to **Tools** → **"X-Files Enablement"** and press it.
4. Wait for the green confirmation.

Done — skip to [step 3](#3-tell-x-files-your-credentials).

### Way 2 — The script (comes with every release)

1. Download the latest X-Files release from
   [the Releases page](https://github.com/marcelofrau/x-files-uwp/releases).
2. Unzip it. Open the **`tools`** folder.
3. **On Windows:** right-click → *Open in Terminal*, then type:

   ```
   pwsh ./liberate-loopback.ps1 -Ip <YOUR-IP> -User <portal-user>
   ```

   (replace `<YOUR-IP>` and `<portal-user>` with your values)

4. Type your portal password when asked.
5. Wait for **`OK: exemption applied`**.

### Way 3 — Manual (phone also works)

You can do the same from a phone using the **Termux** app:

1. Get the **SSH password**. It's not the portal password — it's a special
   rotating password that **Dev Home shows on the console** (look for the SSH /
   SFTP credential on the main Dev Home screen, or in the Device Portal file
   explorer).
2. In Termux, run:

   ```
   ssh DevToolsUser@<YOUR-IP>
   ```

   (the username is always `DevToolsUser`, and you type the SSH password above,
   not the portal one)

3. Once connected, run:

   ```
   checknetisolation loopbackexempt -a -n=XFiles.Xbox_jgz7qwhvc5jpc
   exit
   ```

---

## 3. Tell X-Files your credentials

1. Open X-Files on your Xbox.
2. Open the **Settings** → **Portal Credentials**.
3. Enter the same IP, username, and password from step 1.
4. Save.

---

## 4. Open User Files

1. In X-Files, go to the **file list**.
2. Open the **"User Folders"** entry at the top.
3. Pick an app — RetroArch, DuckStation, etc.
4. Browse its saves, memory cards, and configs.

---

## 5. Why is it not working?

| Problem | Fix |
|---|---|
| "User Folders" doesn't appear | Re-run the unlock (step 2) — it must be redone after a console reboot or a new X-Files install |
| Connection refused | Check the IP is correct and the console is in Developer Mode |
| Wrong password | In Way 1/2 use the **portal** password (Dev Home → Device Portal); in Way 3 use the **SSH** password shown in Dev Home |
| Nothing changed after unlock | Close X-Files fully and reopen it |

> 🔧 For the full technical explanation, see
> [Portal AppData (Developer Docs)](PORTAL-APPDATA.html).
