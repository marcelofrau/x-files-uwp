param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Debug',
    [ValidateSet('x64')][string]$Platform = 'x64'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# ── Build ──
& "$PSScriptRoot\build.ps1" -Configuration $Configuration -Platform $Platform
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# ── Read version ──
$versionFile = Join-Path $root 'version.txt'
if (!(Test-Path $versionFile)) { Write-Error "version.txt not found"; exit 1 }
$version = (Get-Content $versionFile -Raw).Trim()

# ── Locate MSIX ──
$configSuffix = if ($Configuration -eq 'Debug') { '_Debug' } else { '' }
$pkgDir = Join-Path $root "AppPackages\XFiles_${version}_${Platform}${configSuffix}_Test"
$msix = Join-Path $pkgDir "XFiles_${version}_${Platform}${configSuffix}.msix"

# Fallback: glob
if (!(Test-Path $msix)) {
    $found = Get-ChildItem (Join-Path $root "AppPackages") -Directory -Filter "XFiles_*_${Platform}_*" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($found) {
        $f = Get-ChildItem $found.FullName -Filter "*.msix" | Select-Object -First 1
        if ($f) { $msix = $f.FullName; $pkgDir = $found.FullName }
    }
}

if (!(Test-Path $msix)) { Write-Error "MSIX not found after build"; exit 1 }

# ── Extract ──
$extractDir = Join-Path $env:TEMP "xfiles-${Configuration}"
if (Test-Path $extractDir) { Remove-Item $extractDir -Recurse -Force; Start-Sleep -Milliseconds 200 }
New-Item -ItemType Directory -Path $extractDir -Force | Out-Null

$makeappx = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\MakeAppx.exe"
if (-not (Test-Path $makeappx)) {
    # Fallback: find any installed SDK
    $makeappx = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin" -Recurse -Filter "MakeAppx.exe" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $makeappx) { Write-Error "MakeAppx.exe not found. Install Windows SDK."; exit 1 }

Write-Host "=== Extract ===" -ForegroundColor Cyan
& $makeappx unpack /p $msix /d $extractDir /o
if ($LASTEXITCODE -ne 0) { Write-Error "Extract failed"; exit 1 }

# ── Register ──
Write-Host "=== Register ===" -ForegroundColor Cyan
$manifest = Join-Path $extractDir 'AppxManifest.xml'
Add-AppxPackage -Register $manifest

# ── Launch ──
Write-Host "=== Launch ===" -ForegroundColor Cyan
Add-Type -AssemblyName System.Runtime.InteropServices

$t = @"
using System;
using System.Runtime.InteropServices;

[ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IApplicationActivationManager {
    int ActivateApplication(string appUserModelId, string arguments, uint options, out uint processId);
    int ActivateForFile(string appUserModelId, IntPtr itemArray, string verb, out uint processId);
    int ActivateForProtocol(string appUserModelId, IntPtr itemArray, out uint processId);
}

public class AppActivator {
    static readonly Guid CLSID = new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C");
    public static uint Launch(string aumid) {
        var type = Type.GetTypeFromCLSID(CLSID);
        var mgr = (IApplicationActivationManager)Activator.CreateInstance(type);
        uint pid;
        int hr = mgr.ActivateApplication(aumid, null, 0, out pid);
        if (hr < 0) throw new COMException("ActivateApplication", hr);
        return pid;
    }
}
"@
Add-Type -TypeDefinition $t

$pkg = Get-AppxPackage -Name "XFiles.Xbox" -ErrorAction SilentlyContinue
if ($pkg) {
    $pfn = $pkg.PackageFamilyName
    $aumid = "${pfn}!App"
    $procId = [AppActivator]::Launch($aumid)
    Write-Host "Launched PID=$procId" -ForegroundColor Green
} else {
    Write-Warning "Package not found. Start manually from Start Menu."
}
