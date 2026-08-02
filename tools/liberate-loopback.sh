#!/usr/bin/env bash
# liberate-loopback.sh
#
# Apply (or remove) the Xbox loopback exemption for a UWP app so it can reach
# the Device Portal REST API on the console itself.
#
# UWP apps on Xbox are isolated from local network loopback, so X-Files cannot
# call https://[::1]:11443 (the console's own Device Portal) to browse
# LocalAppData of other apps. This script grants the exemption by running, over
# SSH on the console:
#
#     checknetisolation loopbackexempt -a -n=<PackageFamilyName>
#
# Credential / discovery flow (mirrors xbHomebrewVault):
#   1. Device Portal credentials fetch the ROTATING SSH password from
#      GET /ext/smb/developerfolder
#   2. The target app's Package Family Name (PFN) is discovered from
#      GET /api/app/packagemanager/packages   (jq or python3)
#   3. checknetisolation runs over SSH (DevToolsUser, port 22) via sshpass
#      when available, otherwise interactive ssh.
#
# The exemption survives app relaunch but is LOST on re-install and on console
# reboot. Re-run after either.
#
# Usage:
#   ./liberate-loopback.sh                       # interactive
#   ./liberate-loopback.sh -ip 10.0.0.98 -user keita -app XFiles.Xbox
#   ./liberate-loopback.sh -pfn XFiles.Xbox_jgz7qwhvc5jpc -undo
#   ./liberate-loopback.sh -check
#
# Options:
#   -ip <ip>            Xbox IP/hostname
#   -user <u>           Device Portal username
#   -pass <p>           Device Portal password (prompted securely if omitted)
#   -app <name>         Installed app Name (default: XFiles.Xbox)
#   -pfn <pfn>          Skip discovery, use this Package Family Name directly
#   -portalPort <n>     Device Portal HTTPS port (default: 11443)
#   -sshPort <n>        SSH port (default: 22)
#   -sshUser <u>        SSH username (default: DevToolsUser)
#   -undo               Remove the exemption (-d) instead of adding it (-a)
#   -check              Only verify current exemption state; change nothing
#   -h | --help         Show this help

set -euo pipefail

IP=""
PORTAL_USER=""
PORTAL_PASS=""
APP="XFiles.Xbox"
PFN=""
PORTAL_PORT=11443
SSH_PORT=22
SSH_USER="DevToolsUser"
UNDO=0
CHECK=0
SSH_PASS=""

usage() {
    sed -n '2,40p' "$0" | sed 's/^# \{0,1\}//'
    exit 0
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        -ip)         IP="$2"; shift 2 ;;
        -user)       PORTAL_USER="$2"; shift 2 ;;
        -pass)       PORTAL_PASS="$2"; shift 2 ;;
        -app)        APP="$2"; shift 2 ;;
        -pfn)        PFN="$2"; shift 2 ;;
        -portalPort) PORTAL_PORT="$2"; shift 2 ;;
        -sshPort)    SSH_PORT="$2"; shift 2 ;;
        -sshUser)    SSH_USER="$2"; shift 2 ;;
        -undo)       UNDO=1; shift ;;
        -check)      CHECK=1; shift ;;
        -h|-help|--help) usage ;;
        *) echo "Unknown option: $1" >&2; usage ;;
    esac
done

prompt() { # $1 message, $2 var name
    read -rp "$1: " "$2"
}

prompt_secret() { # $1 message, $2 var name
    read -rsp "$1: " "$2"
    echo
}

portal_get() { # $1 path  -> body to stdout
    curl -sk -u "$PORTAL_USER:$PORTAL_PASS" "https://$IP:$PORTAL_PORT$1"
}

get_ssh_pass() {
    echo "Fetching rotating SSH password from portal..."
    local resp pw
    resp=$(portal_get "/ext/smb/developerfolder") || true
    if [[ -n "$resp" ]]; then
        if command -v jq >/dev/null 2>&1; then
            pw=$(printf '%s' "$resp" | jq -r '.Password // ""')
        elif command -v python3 >/dev/null 2>&1; then
            pw=$(printf '%s' "$resp" | python3 -c 'import json,sys; d=json.load(sys.stdin); print(d.get("Password",""))' 2>/dev/null) || pw=""
        fi
        if [[ -n "$pw" ]]; then
            SSH_PASS="$pw"
            echo "  SSH password discovered via portal (rotating)."
            return 0
        fi
    fi
    echo "  Could not fetch SSH password from portal." >&2
    prompt_secret "SSH password ($SSH_USER)" SSH_PASS
}

discover_pfn() {
    local json pfn
    echo "Discovering Package Family Name for '$APP'..."
    json=$(portal_get "/api/app/packagemanager/packages") || true
    if [[ -z "$json" ]]; then
        echo "  Packages endpoint returned nothing." >&2
        echo ""
        return 1
    fi

    pfn=""
    if command -v jq >/dev/null 2>&1; then
        pfn=$(printf '%s' "$json" | jq -r --arg n "$APP" '.InstalledPackages[]? | select(.Name==$n) | .PackageFamilyName' | head -n1)
    elif command -v python3 >/dev/null 2>&1; then
        pfn=$(printf '%s' "$json" | APP="$APP" python3 -c '
import json, sys, os
name = os.environ["APP"]
data = json.load(sys.stdin)
for p in data.get("InstalledPackages", []) or []:
    if p.get("Name") == name and p.get("PackageFamilyName"):
        sys.stdout.write(p["PackageFamilyName"])
        break
' 2>/dev/null) || pfn=""
    else
        echo "  Neither jq nor python3 available - cannot auto-discover PFN." >&2
    fi

    if [[ -n "$pfn" ]]; then
        echo "$pfn"
        return 0
    fi

    echo "  App '$APP' not found / PFN empty. Installed apps:" >&2
    if command -v jq >/dev/null 2>&1; then
        printf '%s' "$json" | jq -r '.InstalledPackages[]? | "    \(.Name)  (\(.PackageFamilyName // ""))"' >&2
    elif command -v python3 >/dev/null 2>&1; then
        printf '%s' "$json" | python3 -c '
import json, sys
data = json.load(sys.stdin)
for p in data.get("InstalledPackages", []) or []:
    print("    %s  (%s)" % (p.get("Name", "?"), p.get("PackageFamilyName") or ""))
' >&2
    fi
    echo ""
}

run_ssh() { # $1 command
    if command -v sshpass >/dev/null 2>&1; then
        sshpass -p "$SSH_PASS" ssh -p "$SSH_PORT" -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null "$SSH_USER@$IP" "$1"
    else
        echo "  sshpass not found - interactive ssh. Type the SSH password when prompted." >&2
        ssh -p "$SSH_PORT" -o StrictHostKeyChecking=accept-new "$SSH_USER@$IP" "$1"
    fi
}

# ---------------------------------------------------------------------------
# Interactive credential collection
# ---------------------------------------------------------------------------
if [[ -z "$IP" ]]; then prompt "Xbox IP/hostname" IP; fi
if [[ -z "$PORTAL_USER" ]]; then prompt "Device Portal username" PORTAL_USER; fi
if [[ -z "$PORTAL_PASS" ]]; then prompt_secret "Device Portal password" PORTAL_PASS; fi

get_ssh_pass

if [[ -z "$PFN" ]]; then
    PFN=$(discover_pfn) || true
    if [[ -z "$PFN" ]]; then prompt "Package Family Name (PFN)" PFN; fi
fi
echo
echo "Target PFN: $PFN"

# ---------------------------------------------------------------------------
# Check-only mode
# ---------------------------------------------------------------------------
if [[ $CHECK -eq 1 ]]; then
    echo "Checking current exemption state..."
    out=$(run_ssh "checknetisolation loopbackexempt -s") || true
    if grep -q -- "$PFN" <<<"$out"; then
        echo "  EXEMPT: '$PFN' is loopback-exempt."
    else
        echo "  NOT EXEMPT: '$PFN' is missing from the exemption list."
        echo "  Run without -check to apply it."
    fi
    exit 0
fi

# ---------------------------------------------------------------------------
# Apply / remove
# ---------------------------------------------------------------------------
flag="-a"
verb="Add"
if [[ $UNDO -eq 1 ]]; then flag="-d"; verb="Remove"; fi
cmd="checknetisolation loopbackexempt $flag -n=$PFN"
echo "Running: $cmd"
run_ssh "$cmd"

echo "Verifying..."
out=$(run_ssh "checknetisolation loopbackexempt -s") || true
if grep -q -- "$PFN" <<<"$out"; then
    if [[ $UNDO -eq 1 ]]; then echo "  OK: exemption for '$PFN' removed."; else echo "  OK: exemption for '$PFN' applied."; fi
else
    if [[ $UNDO -eq 1 ]]; then echo "  WARN: exemption for '$PFN' still present."; else echo "  WARN: exemption for '$PFN' NOT present."; fi
    echo "  For an add: is the app installed? Is the console in Developer Mode?" >&2
fi
