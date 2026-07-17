$root = Split-Path -Parent $PSScriptRoot

Write-Host "=== Git ===" -ForegroundColor Cyan
git -C $root status --short

Write-Host "`n=== Package ===" -ForegroundColor Cyan
$pkg = Get-AppxPackage -Name "XFiles.Xbox" -ErrorAction SilentlyContinue
if ($pkg) {
    Write-Host "  Installed: $($pkg.PackageFullName)" -ForegroundColor Green
} else {
    Write-Host "  Not installed." -ForegroundColor DarkGray
}

Write-Host "`n=== Build Outputs ===" -ForegroundColor Cyan
foreach ($config in @('Debug', 'Release')) {
    $exe = Join-Path $root "x64\$config\XFiles\XFiles.exe"
    if (Test-Path $exe) {
        $ver = (Get-Item $exe).VersionInfo
        Write-Host "  XFiles.exe ($config) $($ver.FileVersion)" -ForegroundColor Green
    }
}

Write-Host "`n=== Version ===" -ForegroundColor Cyan
$versionFile = Join-Path $root 'version.txt'
if (Test-Path $versionFile) {
    $version = (Get-Content $versionFile -Raw).Trim()
    Write-Host "  $version" -ForegroundColor Green
} else {
    Write-Host "  (no version.txt)" -ForegroundColor DarkGray
}

Write-Host "`n=== Distributable ===" -ForegroundColor Cyan
$zips = Get-ChildItem $root -Filter "xfiles_*.zip" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending
if ($zips) {
    foreach ($z in $zips) {
        $size = [math]::Round($z.Length / 1MB, 2)
        Write-Host "  $($z.Name) ($size MB)" -ForegroundColor Green
    }
} else {
    Write-Host "  (none)" -ForegroundColor DarkGray
}
