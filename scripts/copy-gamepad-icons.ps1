param(
    [int]$Size = 64,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$assetPack = "F:\workspace\assets\XBOX BUTTONS - Premium Assets"
$srcPng = Join-Path $assetPack "Digital Buttons\ABXY"
$srcShoulder = Join-Path $assetPack "Digital Buttons\Shoulder"
$srcDpad = Join-Path $assetPack "D-Pad"
$srcTrigger = Join-Path $assetPack "Analog Triggers"
$srcSystem = Join-Path $assetPack "Digital Buttons\System"
$outRoot = Join-Path $root "XFiles\Assets\GamepadButtons"

$themes = @(
    @{ Name = "xbox-dark";    DpadPrefix = "button_xbox_dpad_dark";    TriggerPrefix = "button_xbox_analog_trigger_dark"; BumperPrefix = "button_xbox_digital_bumper_dark" },
    @{ Name = "xbox-light";   DpadPrefix = "button_xbox_dpad_light";   TriggerPrefix = "button_xbox_analog_trigger_light"; BumperPrefix = "button_xbox_digital_bumper_light" },
    @{ Name = "xboxone-dark"; DpadPrefix = "button_xboxone_dpad_dark"; TriggerPrefix = "button_xbox_analog_trigger_dark"; BumperPrefix = "button_xbox_digital_bumper_dark" },
    @{ Name = "xboxone-light";DpadPrefix = "button_xboxone_dpad_light";TriggerPrefix = "button_xbox_analog_trigger_light"; BumperPrefix = "button_xbox_digital_bumper_light" }
)

# ABXY: only xbox variant, same for all themes (transparent bg)
$abxyButtons = @(
    @{ Letter = "a"; SrcName = "button_xbox_digital_a" },
    @{ Letter = "b"; SrcName = "button_xbox_digital_b" },
    @{ Letter = "x"; SrcName = "button_xbox_digital_x" },
    @{ Letter = "y"; SrcName = "button_xbox_digital_y" }
)

$converted = 0
$skipped = 0

function Convert-ToPng {
    param([string]$SrcPath, [string]$OutPath)
    if (-not (Test-Path $SrcPath)) {
        Write-Warning "  Source not found: $SrcPath"
        return $false
    }
    if ($DryRun) {
        Write-Host "  [dry-run] $SrcPath -> $OutPath" -ForegroundColor DarkGray
        return $true
    }
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'SilentlyContinue'
    & inkscape "$SrcPath" --export-type=png --export-filename="$OutPath" --export-width=$Size --export-height=$Size 2>$null
    $ErrorActionPreference = $prev
    if (Test-Path $OutPath) {
        $script:converted++
        return $true
    }
    return $false
}

foreach ($theme in $themes) {
    $themeDir = Join-Path $outRoot $theme.Name
    if (-not $DryRun) {
        New-Item -ItemType Directory -Path $themeDir -Force | Out-Null
    }
    Write-Host "`n=== $($theme.Name) ===" -ForegroundColor Cyan

    # ABXY (same source for all themes — transparent bg)
    foreach ($btn in $abxyButtons) {
        for ($state = 1; $state -le 3; $state++) {
            $srcFile = "$($btn.SrcName)_$state.png"
            $srcPath = Join-Path $srcPng $srcFile
            $outFile = "$($btn.Letter)-$state.png"
            $outPath = Join-Path $themeDir $outFile
            if (Convert-ToPng $srcPath $outPath) {
                Write-Host "  $outFile" -ForegroundColor Green
            }
        }
    }

    # D-Pad (3 states)
    for ($state = 1; $state -le 3; $state++) {
        $srcFile = "$($theme.DpadPrefix)_$state.png"
        $srcPath = Join-Path $srcDpad $srcFile
        $outFile = "dpad-$state.png"
        $outPath = Join-Path $themeDir $outFile
        if (Convert-ToPng $srcPath $outPath) {
            Write-Host "  $outFile" -ForegroundColor Green
        }
    }

    # Bumpers: 1=LB, 2=RB
    $bumperMap = @(@{ Num = 1; Out = "lb" }, @{ Num = 2; Out = "rb" })
    foreach ($b in $bumperMap) {
        $srcFile = "$($theme.BumperPrefix)_$($b.Num).png"
        $srcPath = Join-Path $srcShoulder $srcFile
        $outFile = "$($b.Out).png"
        $outPath = Join-Path $themeDir $outFile
        if (Convert-ToPng $srcPath $outPath) {
            Write-Host "  $outFile" -ForegroundColor Green
        }
    }

    # Triggers: 1=LT, 2=RT
    $triggerMap = @(@{ Num = 1; Out = "lt" }, @{ Num = 2; Out = "rt" })
    foreach ($t in $triggerMap) {
        $srcFile = "$($theme.TriggerPrefix)_$($t.Num).png"
        $srcPath = Join-Path $srcTrigger $srcFile
        $outFile = "$($t.Out).png"
        $outPath = Join-Path $themeDir $outFile
        if (Convert-ToPng $srcPath $outPath) {
            Write-Host "  $outFile" -ForegroundColor Green
        }
    }

    # System buttons (view, menu, home — no theme variant)
    $systemButtons = @(
        @{ Src = "button_xbox_digital_view_1.png"; Out = "view.png" },
        @{ Src = "button_xbox_digital_menu_1.png"; Out = "menu.png" },
        @{ Src = "button_xbox_digital_home_white.png"; Out = "home.png" }
    )
    foreach ($sys in $systemButtons) {
        $srcPath = Join-Path $srcSystem $sys.Src
        $outPath = Join-Path $themeDir $sys.Out
        if (Convert-ToPng $srcPath $outPath) {
            Write-Host "  $($sys.Out)" -ForegroundColor Green
        }
    }
}

Write-Host "`n=== Done ===" -ForegroundColor Green
Write-Host "  Converted: $converted" -ForegroundColor Green
Write-Host "  Output: $outRoot" -ForegroundColor Gray
