# build-native.ps1
# Builds RetroAudio.dll (native chiptune decoder shim) for X-Files.
#
# Requires: Visual Studio (MSVC x64 toolset) + CMake (not used for the libs,
# both are compiled directly with cl.exe - deterministic, CI-friendly).
#
# Output: XFiles\Native\bin\RetroAudio.dll (x64, Release, static CRT /MT).
#
# Vendored third-party sources:
#   third_party\game-music-emu-0.6.5   (LGPL-2.1+)
#   third_party\libopenmpt-0.8.7       (BSD-3-Clause)
#   third_party\aosdk_psf              (PSF engine: BSD-3 + MAME GPL-2.0+ + peops GPL-2.0+)
#   third_party\lazyusf                (N64 USF, CC0-1.0)
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File build-native.ps1

[CmdletBinding()]
param(
    [string]$VcVarsAll = "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat",
    [switch]$Clean
)

$ErrorActionPreference = "Stop"
$scriptRoot = $PSScriptRoot
if (-not $scriptRoot) { $scriptRoot = (Get-Location).Path }
$nativeRoot = $scriptRoot
$vendorRoot = Join-Path $nativeRoot "third_party"
$binRoot = Join-Path $nativeRoot "bin"
$objRoot = Join-Path $nativeRoot "obj"
$outDll = Join-Path $binRoot "RetroAudio.dll"

$gmeDir = Join-Path $vendorRoot "game-music-emu-0.6.5\gme"
$lopRoot = Join-Path $vendorRoot "libopenmpt-0.8.7"
$zlibDir = Join-Path $vendorRoot "zlib-1.3.1"
$psfDir = Join-Path $vendorRoot "aosdk_psf"
$usfRoot = Join-Path $vendorRoot "lazyusf"

if (-not (Test-Path $VcVarsAll)) {
    # Auto-detect via vswhere when the configured path is missing (e.g. CI runners
    # where VS edition/version differs from the local default).
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $vsPath = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath 2>$null
        if ($vsPath) {
            $candidate = Join-Path ($vsPath | Select-Object -First 1) "VC\Auxiliary\Build\vcvars64.bat"
            if (Test-Path $candidate) {
                Write-Host "Auto-detected MSVC toolset via vswhere: $candidate"
                $VcVarsAll = $candidate
            }
        }
    }
}
if (-not (Test-Path $VcVarsAll)) {
    Write-Error "vcvars64.bat not found at '$VcVarsAll' and vswhere auto-detection failed. Pass -VcVarsAll with the correct path."
}

Write-Host "== RetroAudio native build =="
Write-Host "  toolset : $VcVarsAll"

New-Item -ItemType Directory -Force -Path $binRoot, $objRoot | Out-Null
if ($Clean) { Get-ChildItem $objRoot -File -Filter "*.obj" | Remove-Item -Force }

# Run everything through cmd.exe so vcvars64.bat environment sticks.
$buildCmd = @'
@echo off
setlocal EnableDelayedExpansion
call "%VCVARSALL%"
if errorlevel 1 exit /b 1

set "OBJ=%OBJROOT%"
set "GME=%GMEDIR%"
set "LOP=%LOPROOT%"
set "PSF=%PSFROOT%"
set "USF=%USFROOT%"

rem ---------- clean stale objects ----------
if exist "%OBJ%\*.obj" del /Q "%OBJ%\*.obj"

rem ---------- compile game-music-emu ----------
echo [gme] compiling...
for %%F in ("%GME%\*.cpp") do (
    cl /nologo /c /O2 /MT /EHsc /std:c++17 /W2 /DNDEBUG /DBLARGG_LITTLE_ENDIAN=1 /DVGM_YM2612_GENS /DHAVE_ZLIB_H /I "%GME%" /I "%ZLIBDIR%" "%%F" /Fo"%OBJ%\gme_%%~nF.obj" /Fd"%OBJ%\gme.pdb"
    if errorlevel 1 exit /b 1
)
rem emu2413 (VRC7 OPLL) - plain C sources under gme\ext
for %%F in ("%GME%\ext\*.c") do (
    cl /nologo /c /O2 /MT /W2 /DNDEBUG /DBLARGG_LITTLE_ENDIAN=1 /DHAVE_ZLIB_H /I "%GME%" /I "%ZLIBDIR%" "%%F" /Fo"%OBJ%\gme_%%~nF.obj" /Fd"%OBJ%\gme.pdb"
    if errorlevel 1 exit /b 1
)
rem zlib (gzip support for GME: .vgz files)
echo [gme] compiling zlib...
for %%F in ("%ZLIBDIR%\*.c") do (
    cl /nologo /c /O2 /MT /W2 /DNDEBUG /I "%ZLIBDIR%" "%%F" /Fo"%OBJ%\zlib_%%~nF.obj" /Fd"%OBJ%\zlib.pdb"
    if errorlevel 1 exit /b 1
)

rem ---------- compile libopenmpt ----------
echo [libopenmpt] compiling...
for %%D in ("%LOP%\common" "%LOP%\soundlib" "%LOP%\soundlib\plugins" "%LOP%\soundlib\plugins\dmo" "%LOP%\sounddsp" "%LOP%\libopenmpt") do (
    for %%F in ("%%~D\*.cpp") do (
        if /I not "%%~nxF"=="load_j2b.cpp" (
            cl /nologo /c /O2 /MT /EHsc /std:c++17 /W2 /DNDEBUG /DLIBOPENMPT_BUILD /DMPT_WITH_MINIZ /I "%LOP%\src" /I "%LOP%\common" /I "%LOP%\include" /I "%LOP%" "%%F" /Fo"%OBJ%\lop_%%~nF.obj" /Fd"%OBJ%\lop.pdb"
            if errorlevel 1 exit /b 1
        )
    )
)
rem load_j2b (also contains the AM loader) - needs zlib/miniz inflate
cl /nologo /c /O2 /MT /EHsc /std:c++17 /W2 /DNDEBUG /DLIBOPENMPT_BUILD /DMPT_WITH_MINIZ /I "%LOP%\src" /I "%LOP%\common" /I "%LOP%\include" /I "%LOP%" "%LOP%\soundlib\load_j2b.cpp" /Fo"%OBJ%\lop_load_j2b.obj" /Fd"%OBJ%\lop.pdb"
if errorlevel 1 exit /b 1
rem miniz (vendored in-tree zlib replacement)
cl /nologo /c /O2 /MT /W2 /DNDEBUG /I "%LOP%\include\miniz" "%LOP%\include\miniz\miniz.c" /Fo"%OBJ%\lop_miniz.obj" /Fd"%OBJ%\lop.pdb"
if errorlevel 1 exit /b 1

rem ---------- compile aosdk PSF engine (Audio Overload SDK / MAME PSX core) ----------
echo [aosdk_psf] compiling...
for %%F in ("%PSF%\corlett.c" "%PSF%\eng_psf.c" "%PSF%\psx.c" "%PSF%\psx_hw.c") do (
    cl /nologo /c /O2 /MT /W2 /DNDEBUG /D_CRT_SECURE_NO_WARNINGS /I "%PSF%" /I "%ZLIBDIR%" "%%F" /Fo"%OBJ%\psf_%%~nF.obj" /Fd"%OBJ%\psf.pdb"
    if errorlevel 1 exit /b 1
)
rem peops is single-file: spu.c #includes reverb.c/adsr.c/registers.c/dma.c
for %%F in ("%PSF%\peops\spu.c" "%PSF%\ps2_stubs.c") do (
    cl /nologo /c /O2 /MT /W2 /DNDEBUG /D_CRT_SECURE_NO_WARNINGS /I "%PSF%" /I "%ZLIBDIR%" "%%F" /Fo"%OBJ%\psf_%%~nF.obj" /Fd"%OBJ%\psf.pdb"
    if errorlevel 1 exit /b 1
)

rem ---------- compile lazyusf (N64 USF core) ----------
rem NOTE: rsp\ and rsp_hle\ have files sharing base names with the root
rem (memory.c, audio.c). Prefix each object with its source folder so the
rem later loop can't clobber the earlier one's object file.
echo [lazyusf] compiling...
for %%F in ("%USF%\*.c") do (
    cl /nologo /c /O2 /MT /W2 /DNDEBUG /D_CRT_SECURE_NO_WARNINGS /I "%USF%" /I "%USF%\rsp" /I "%USF%\rsp_hle" "%%F" /Fo"%OBJ%\usf_%%~nF.obj" /Fd"%OBJ%\usf.pdb"
    if errorlevel 1 exit /b 1
)
for %%F in ("%USF%\rsp\*.c") do (
    if /I not "%%~nxF"=="bench.c" (
        cl /nologo /c /O2 /MT /W2 /DNDEBUG /D_CRT_SECURE_NO_WARNINGS /I "%USF%" /I "%USF%\rsp" /I "%USF%\rsp_hle" "%%F" /Fo"%OBJ%\usf_rsp_%%~nF.obj" /Fd"%OBJ%\usf.pdb"
        if errorlevel 1 exit /b 1
    )
)
for %%F in ("%USF%\rsp_hle\*.c") do (
    cl /nologo /c /O2 /MT /W2 /DNDEBUG /D_CRT_SECURE_NO_WARNINGS /I "%USF%" /I "%USF%\rsp" /I "%USF%\rsp_hle" "%%F" /Fo"%OBJ%\usf_hle_%%~nF.obj" /Fd"%OBJ%\usf.pdb"
    if errorlevel 1 exit /b 1
)

rem ---------- compile shim ----------
echo [retroaudio] compiling shim...
cl /nologo /c /O2 /MT /EHsc /std:c++17 /W2 /DNDEBUG /DRETROAUDIO_BUILD /I "%GME%" /I "%LOP%" /I "%ZLIBDIR%" /I "%PSF%" /I "%USF%" /I "%USF%\rsp_hle" "%RETRODIR%\retroaudio.cpp" /Fo"%OBJ%\retroaudio.obj" /Fd"%OBJ%\retroaudio.pdb"
if errorlevel 1 exit /b 1

rem ---------- link ----------
echo [link] RetroAudio.dll
link /nologo /DLL /OUT:"%OUTDLL%" /PDB:"%OBJ%\RetroAudio.pdb" "%OBJ%\*.obj" user32.lib kernel32.lib
if errorlevel 1 exit /b 1

echo [done] %OUTDLL%
'@

$env:VCVARSALL = $VcVarsAll
$env:OBJROOT = $objRoot
$env:GMEDIR = $gmeDir
$env:ZLIBDIR = $zlibDir
$env:LOPROOT = $lopRoot
$env:PSFROOT = $psfDir
$env:USFROOT = $usfRoot
$env:RETRODIR = (Join-Path $nativeRoot "retroaudio")
$env:OUTDLL = $outDll

$cmdFile = Join-Path $objRoot "build-native.cmd"
Set-Content -Path $cmdFile -Value $buildCmd -Encoding ASCII
& cmd.exe /c "`"$cmdFile`""
if ($LASTEXITCODE -ne 0) { Write-Error "Native build failed (exit $LASTEXITCODE)." }

Get-Item $outDll | Select-Object FullName, @{n='KB';e={[math]::Round($_.Length/1KB)}}, LastWriteTime
Write-Host "OK: RetroAudio.dll built."

