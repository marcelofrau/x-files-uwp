param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Debug',
    [ValidateSet('x64')][string]$Platform = 'x64'
)

$root = Split-Path -Parent $PSScriptRoot
$sln = Join-Path $root 'XFiles.sln'

# Find MSBuild via vswhere
$vsPath = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" `
    -latest -requires Microsoft.Component.MSBuild -property installationPath 2>$null

$msbuild = $null
if ($vsPath) {
    $msbuild = Join-Path $vsPath 'MSBuild\Current\Bin\MSBuild.exe'
}
if (-not $msbuild -or -not (Test-Path $msbuild)) {
    $msbuild = "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
}
if (-not $msbuild -or -not (Test-Path $msbuild)) {
    Write-Error "MSBuild not found."
    exit 1
}

Write-Host "Cleaning $Configuration|$Platform ..." -ForegroundColor Cyan
& $msbuild $sln /t:Clean "/p:Configuration=$Configuration" "/p:Platform=$Platform"

$appDir = Join-Path $root 'AppPackages'
if (Test-Path $appDir) {
    Remove-Item -Recurse -Force $appDir
    Write-Host "Removed AppPackages/" -ForegroundColor DarkGray
}

$outDir = Join-Path $root 'x64'
if (Test-Path $outDir) {
    Remove-Item -Recurse -Force $outDir
    Write-Host "Removed x64/" -ForegroundColor DarkGray
}

$distribDir = Join-Path $root 'distribute'
if (Test-Path $distribDir) {
    Remove-Item -Recurse -Force $distribDir
    Write-Host "Removed distribute/" -ForegroundColor DarkGray
}

Write-Host "Clean done." -ForegroundColor Green
