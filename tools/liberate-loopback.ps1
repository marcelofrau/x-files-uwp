#!/usr/bin/env pwsh
#Requires -Version 7
<#
.SYNOPSIS
  Apply (or remove) the Xbox loopback exemption for a UWP app so it can reach
  the Device Portal REST API on the console itself.

.DESCRIPTION
  UWP apps on Xbox are isolated from the local network loopback, so X-Files
  cannot call https://[::1]:11443 (the console's own Device Portal) to browse
  LocalAppData of other apps. This script grants the exemption by running, over
  SSH on the console:

      checknetisolation loopbackexempt -a -n=<PackageFamilyName>

  Credential / discovery flow (mirrors xbHomebrewVault):
    1. Device Portal credentials are used to fetch the ROTATING SSH password
       from GET /ext/smb/developerfolder
    2. The target app's Package Family Name (PFN) is discovered from
       GET /api/app/packagemanager/packages
    3. The checknetisolation command runs over SSH (DevToolsUser, port 22)
       via plink when available, otherwise interactive ssh.

  The exemption survives app relaunch but is LOST on re-install and on console
  reboot. Re-run this script (or press Y on X-Files About screen) after either.

  SSH user/password on Xbox:
    user  = DevToolsUser
    pass  = rotating, fetched via /ext/smb/developerfolder (this script does it)

.PARAMETER Ip
  Xbox console IP or hostname. Prompted if omitted.

.PARAMETER User
  Device Portal username. Prompted if omitted.

.PARAMETER Pass
  Device Portal password. Prompted if omitted (secure prompt).

.PARAMETER App
  Installed app Name to exempt (default 'XFiles.Xbox').

.PARAMETER Pfn
  Skip discovery and use this Package Family Name directly.

.PARAMETER PortalPort
  Device Portal HTTPS port (default 11443).

.PARAMETER SshPort
  SSH port (default 22).

.PARAMETER SshUser
  SSH username (default 'DevToolsUser').

.PARAMETER Undo
  Remove the exemption (-d) instead of adding it (-a).

.PARAMETER Check
  Only verify the current exemption state; change nothing.

.EXAMPLE
  ./liberate-loopback.ps1
  ./liberate-loopback.ps1 -Ip 10.0.0.98 -User keita -App XFiles.Xbox
  ./liberate-loopback.ps1 -Pfn XFiles.Xbox_jgz7qwhvc5jpc -Undo
  ./liberate-loopback.ps1 -Check
#>
param(
    [string]$Ip,
    [string]$User,
    [string]$Pass,
    [string]$App = 'XFiles.Xbox',
    [string]$Pfn,
    [int]$PortalPort = 11443,
    [int]$SshPort = 22,
    [string]$SshUser = 'DevToolsUser',
    [switch]$Undo,
    [switch]$Check
)

$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSVersion.Major -lt 7) {
    Write-Error 'This script requires PowerShell 7 (pwsh) for -SkipCertificateCheck.'
}

function Read-Password {
    param([string]$Prompt)
    $sec = Read-Host -AsSecureString -Prompt $Prompt
    if ($null -eq $sec -or $sec.Length -eq 0) { throw 'Password is required.' }
    return [System.Management.Automation.PSCredential]::new('user', $sec).GetNetworkCredential().Password
}

function Invoke-PortalJson {
    param(
        [string]$Path,
        [string]$PortalUser,
        [string]$PortalPassword
    )
    $pair = "${PortalUser}:${PortalPassword}"
    $b64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($pair))
    $headers = @{ Authorization = "Basic $b64" }
    $uri = "https://${Ip}:${PortalPort}${Path}"
    Write-Host "  GET $Path" -ForegroundColor DarkGray
    return Invoke-RestMethod -SkipCertificateCheck -Uri $uri -Headers $headers
}

function Get-SshPassword {
    param(
        [string]$PortalUser,
        [string]$PortalPassword
    )
    try {
        $resp = Invoke-PortalJson -Path '/ext/smb/developerfolder' -PortalUser $PortalUser -PortalPassword $PortalPassword
        if ($resp.Password) {
            Write-Host "  SSH password discovered via portal (rotating)." -ForegroundColor Green
            return [string]$resp.Password
        }
        Write-Host '  /ext/smb/developerfolder returned no Password field.' -ForegroundColor Yellow
    }
    catch {
        Write-Host "  SMB/SSH password endpoint failed: $($_.Exception.Message)" -ForegroundColor Yellow
    }
    Write-Host '  Falling back to manual SSH password entry.' -ForegroundColor Yellow
    return Read-Password "SSH password ($SshUser)"
}

function Get-Pfn {
    param(
        [string]$PortalUser,
        [string]$PortalPassword,
        [string]$AppName
    )
    $resp = Invoke-PortalJson -Path '/api/app/packagemanager/packages' -PortalUser $PortalUser -PortalPassword $PortalPassword
    $pkgs = @($resp.InstalledPackages)
    if ($pkgs.Count -eq 0) { throw 'Portal returned no installed packages.' }

    $match = $pkgs | Where-Object { $_.Name -eq $AppName } | Select-Object -First 1
    if ($null -ne $match -and $match.PackageFamilyName) {
        return [string]$match.PackageFamilyName
    }

    Write-Host "  App '$AppName' not found exactly among installed packages." -ForegroundColor Yellow
    $fuzzy = $pkgs | Where-Object {
        ($_.Name -like "*$AppName*") -or ($AppName -like "*$($_.Name)*")
    } | Select-Object -First 5

    if ($fuzzy) {
        Write-Host '  Closest matches:' -ForegroundColor Yellow
        for ($i = 0; $i -lt $fuzzy.Count; $i++) {
            Write-Host ("    [{0}] {1}  ({2})" -f $i, $fuzzy[$i].Name, $fuzzy[$i].PackageFamilyName)
        }
        $idx = Read-Host '  Pick index, or press Enter to type the PFN manually'
        if ($idx -match '^\d+$' -and [int]$idx -lt $fuzzy.Count) {
            return [string]$fuzzy[[int]$idx].PackageFamilyName
        }
    }
    else {
        Write-Host '  Available apps (Name / PackageFamilyName):' -ForegroundColor Yellow
        foreach ($p in $pkgs) {
            Write-Host ("    {0}  ({1})" -f $p.Name, $p.PackageFamilyName)
        }
    }
    return (Read-Host '  Package Family Name (PFN)')
}

function Invoke-Ssh {
    param(
        [string]$Command,
        [string]$SshPassword
    )
    $plink = Get-Command plink -ErrorAction SilentlyContinue
    if ($null -ne $plink) {
        Write-Host "  [plink] ${SshUser}@${Ip}:${SshPort}" -ForegroundColor DarkGray
        # "y" accepts/stores the host key on first connect.
        $out = "y`n" | & $plink.Source -ssh -P $SshPort -pw $SshPassword "${SshUser}@${Ip}" $Command 2>&1
        return @{ Output = ($out -join "`n"); ExitCode = $LASTEXITCODE }
    }

    Write-Host '  plink not found - using interactive ssh. Type the SSH password when prompted.' -ForegroundColor Yellow
    $out = & ssh -p $SshPort -o StrictHostKeyChecking=accept-new "${SshUser}@${Ip}" $Command 2>&1
    return @{ Output = ($out -join "`n"); ExitCode = $LASTEXITCODE }
}

# ---------------------------------------------------------------------------
# Interactive credential collection
# ---------------------------------------------------------------------------
if (-not $Ip)    { $Ip = Read-Host 'Xbox IP/hostname' }
if (-not $User)  { $User = Read-Host 'Device Portal username' }
if (-not $Pass)  { $Pass = Read-Password 'Device Portal password' }

Write-Host ''
$sshPassword = Get-SshPassword -PortalUser $User -PortalPassword $Pass
if (-not $Pfn) {
    $Pfn = Get-Pfn -PortalUser $User -PortalPassword $Pass -AppName $App
}
Write-Host ''
Write-Host "Target PFN: $Pfn"

# ---------------------------------------------------------------------------
# Check-only mode
# ---------------------------------------------------------------------------
if ($Check) {
    Write-Host 'Checking current exemption state...' -ForegroundColor Cyan
    $res = Invoke-Ssh -Command 'checknetisolation loopbackexempt -s' -SshPassword $sshPassword
    if ($res.Output -match [regex]::Escape($Pfn)) {
        Write-Host "  EXEMPT: '$Pfn' is loopback-exempt." -ForegroundColor Green
    }
    else {
        Write-Host "  NOT EXEMPT: '$Pfn' is missing from the exemption list." -ForegroundColor Yellow
        Write-Host '  Run without -Check to apply it.' -ForegroundColor DarkGray
    }
    exit 0
}

# ---------------------------------------------------------------------------
# Apply / remove
# ---------------------------------------------------------------------------
$flag = if ($Undo) { '-d' } else { '-a' }
$verb = if ($Undo) { 'Remove' } else { 'Add' }
$cmd = "checknetisolation loopbackexempt $flag -n=$Pfn"
Write-Host "Running: $cmd" -ForegroundColor Cyan
$res = Invoke-Ssh -Command $cmd -SshPassword $sshPassword
if ($res.Output) { Write-Host $res.Output }
if ($res.ExitCode -ne 0) {
    Write-Error "checknetisolation failed (exit $($res.ExitCode)). Is the console in Developer Mode?"
}

Write-Host 'Verifying...'
$res2 = Invoke-Ssh -Command 'checknetisolation loopbackexempt -s' -SshPassword $sshPassword
$found = $res2.Output -match [regex]::Escape($Pfn)
if ($found) {
    $state = if ($Undo) { 'removed' } else { 'applied' }
    Write-Host "  OK: exemption for '$Pfn' $state." -ForegroundColor Green
}
else {
    $state = if ($Undo) { 'still present' } else { 'NOT present' }
    Write-Host "  WARN: exemption for '$Pfn' $state after the command." -ForegroundColor Yellow
    Write-Host '  For an Add: is the app installed? Is the console in Developer Mode?' -ForegroundColor DarkGray
}
