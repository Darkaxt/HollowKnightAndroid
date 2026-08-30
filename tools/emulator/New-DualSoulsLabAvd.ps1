$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'DualSoulsLab.psm1') -Force

$definition = Get-DualSoulsLabDefinition
$sdkRoot = Resolve-AndroidSdkRoot
$avdManager = Get-AndroidToolPath -SdkRoot $sdkRoot -Tool avdmanager
$imageDir = Join-Path $sdkRoot ($definition.SystemImage -replace ';', '\')
if (-not (Test-Path -LiteralPath $imageDir -PathType Container)) {
    throw "Required system image is not installed: $($definition.SystemImage)"
}

New-Item -ItemType Directory -Path $definition.AvdHome -Force | Out-Null
$env:ANDROID_AVD_HOME = $definition.AvdHome
$avdDir = Join-Path $definition.AvdHome "$($definition.Name).avd"
$configPath = Join-Path $avdDir 'config.ini'
$pointerPath = Join-Path $definition.AvdHome "$($definition.Name).ini"

if (Test-Path -LiteralPath $configPath -PathType Leaf) {
    Test-DualSoulsLabAvdConfig -Definition $definition -ConfigPath $configPath | Out-Null
    Test-DualSoulsLabAvdPointer -Definition $definition -PointerPath $pointerPath | Out-Null
    Write-Host "Validated existing AVD $($definition.Name) at $avdDir"
    exit 0
}
if ((Test-Path -LiteralPath $avdDir) -or (Test-Path -LiteralPath $pointerPath)) {
    throw "Incomplete or mismatched AVD state exists for $($definition.Name); refusing to overwrite it."
}

$arguments = Get-DualSoulsLabCreateArguments -Definition $definition
'no' | & $avdManager @arguments
if ($LASTEXITCODE -ne 0) { throw "avdmanager failed with exit code $LASTEXITCODE" }
Test-DualSoulsLabAvdConfig -Definition $definition -ConfigPath $configPath | Out-Null
Test-DualSoulsLabAvdPointer -Definition $definition -PointerPath $pointerPath | Out-Null
Write-Host "Created AVD $($definition.Name) at $avdDir"
