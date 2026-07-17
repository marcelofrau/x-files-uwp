param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
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
    Write-Error "MSBuild not found. Run from Developer Command Prompt or install VS2022."
    exit 1
}

Write-Host "Building $Configuration|$Platform ..." -ForegroundColor Cyan
& $msbuild $sln "/p:Configuration=$Configuration" "/p:Platform=$Platform" '/m'

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed."
    exit $LASTEXITCODE
}

Write-Host "Build succeeded." -ForegroundColor Green
