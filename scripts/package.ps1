param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Debug',
    [ValidateSet('x64')][string]$Platform = 'x64',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# ── 1. Ensure signing certificate ──
$pfx = Join-Path $root 'certs\xfiles.pfx'
$cerPath = Join-Path $root 'certs\xfiles.cer'
$certPass = 'dev'
if (-not (Test-Path $pfx)) {
    Write-Host "Creating signing certificate ..." -ForegroundColor Cyan
    New-Item -ItemType Directory -Path (Split-Path $pfx -Parent) -Force | Out-Null
    $secPass = ConvertTo-SecureString $certPass -AsPlainText -Force
    $cert = New-SelfSignedCertificate -Subject "CN=X-Files" -FriendlyName "xfiles" `
        -Type CodeSigningCert -KeyUsage DigitalSignature `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3") `
        -CertStoreLocation Cert:\CurrentUser\My
    Export-PfxCertificate -Cert $cert -FilePath $pfx -Password $secPass -Force | Out-Null
    Export-Certificate -Cert $cert -FilePath $cerPath -Type CERT -Force | Out-Null
    Write-Host "  Cert created: $pfx" -ForegroundColor Green
    Write-Host "  Thumbprint: $($cert.Thumbprint)" -ForegroundColor Yellow

    # Auto-update csproj thumbprint via XML (safe encoding)
    $csproj = Join-Path $root 'XFiles' 'XFiles.csproj'
    [xml]$xml = Get-Content $csproj -Raw
    $ns = New-Object System.Xml.XmlNamespaceManager($xml.NameTable)
    $ns.AddNamespace('ms', 'http://schemas.microsoft.com/developer/msbuild/2003')
    $node = $xml.SelectSingleNode('//ms:PackageCertificateThumbprint', $ns)
    if ($node) { $node.InnerText = $cert.Thumbprint }
    $xml.Save($csproj)
    Write-Host "  Updated XFiles.csproj thumbprint" -ForegroundColor Green
}

# ── 2. Clean old packages ──
$appDir = Join-Path $root 'AppPackages'
if (Test-Path $appDir) {
    Remove-Item -Recurse -Force $appDir
    Write-Host "Cleaned old AppPackages/" -ForegroundColor DarkGray
}

# ── 3. Build (PreBuildEvent increments version) ──
if (-not $SkipBuild) {
    & "$PSScriptRoot\build.ps1" -Configuration $Configuration -Platform $Platform
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

# ── 4. Read version AFTER build ──
$versionFile = Join-Path $root 'version.txt'
if (-not (Test-Path $versionFile)) { Write-Error "version.txt not found"; exit 1 }
$version = (Get-Content $versionFile -Raw).Trim()

# ── 5. Locate MSIX ──
$pkgRoot = Join-Path $root "AppPackages"
$msix = $null
$pkgDir = $null

$msixDirs = @(
    "XFiles_${version}_${Platform}_${Configuration}_Test",
    "XFiles_${version}_${Platform}_Test",
    "XFiles_${version}_${Platform}_${Configuration}"
)
foreach ($d in $msixDirs) {
    $candidate = Join-Path $pkgRoot "$d\XFiles_${version}_${Platform}.msix"
    if (Test-Path $candidate) { $msix = $candidate; $pkgDir = Join-Path $pkgRoot $d; break }
    $candidate2 = Join-Path $pkgRoot "$d\XFiles_${version}_${Platform}_${Configuration}.msix"
    if (Test-Path $candidate2) { $msix = $candidate2; $pkgDir = Join-Path $pkgRoot $d; break }
}

# Fallback: glob for any recent MSIX package dir
if (-not $msix) {
    $found = Get-ChildItem $pkgRoot -Directory -Filter "XFiles_*_${Platform}_*" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($found) {
        $f = Get-ChildItem $found.FullName -Filter "*.msix" | Select-Object -First 1
        if ($f) { $msix = $f.FullName; $pkgDir = $found.FullName }
    }
}

if (-not $msix) {
    Write-Error "MSIX not found after build."
    exit 1
}

Write-Host "`n=== MSIX ===" -ForegroundColor Green
Write-Host "  $msix" -ForegroundColor Green

# ── 5. Collect distributable files ──
$distribDir = Join-Path $root "distribute"
if (Test-Path $distribDir) { Remove-Item $distribDir -Recurse -Force }
New-Item -ItemType Directory -Path $distribDir -Force | Out-Null

# Copy MSIX
$msixName = Split-Path $msix -Leaf
Copy-Item $msix (Join-Path $distribDir $msixName)
Write-Host "  $msixName" -ForegroundColor Gray

# Copy Dependencies (x64 only)
$depDir = Join-Path $pkgDir 'Dependencies'
if (Test-Path $depDir) {
    $x64DepDir = Join-Path $depDir 'x64'
    if (Test-Path $x64DepDir) {
        $destDepDir = Join-Path $distribDir 'Dependencies\x64'
        New-Item -ItemType Directory -Path $destDepDir -Force | Out-Null
        Get-ChildItem $x64DepDir -Filter "*.appx" | ForEach-Object {
            Copy-Item $_.FullName (Join-Path $destDepDir $_.Name)
            $size = [math]::Round($_.Length / 1KB)
            Write-Host "  Dependencies\x64\$($_.Name) ($size KB)" -ForegroundColor Gray
        }
    }
}

# Copy cert
if (Test-Path $cerPath) {
    Copy-Item $cerPath (Join-Path $distribDir 'xfiles.cer')
    Write-Host "  xfiles.cer" -ForegroundColor Gray
}

# ── 6. Create distributable zip ──
$zipName = "xfiles_${version}_${Platform}.zip"
$zipPath = Join-Path $appDir $zipName
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

Compress-Archive -Path (Join-Path $distribDir '*') -DestinationPath $zipPath -Force
$zipSize = [math]::Round((Get-Item $zipPath).Length / 1MB, 2)

# Clean staging dir
Remove-Item $distribDir -Recurse -Force

Write-Host "`n=== Distributable ===" -ForegroundColor Green
Write-Host "  $zipName ($zipSize MB)" -ForegroundColor Green
Write-Host "  $zipPath" -ForegroundColor Gray
Write-Host ""
Write-Host "  Xbox deploy: copy ZIP to Xbox, extract, install MSIX" -ForegroundColor Cyan
Write-Host ""
