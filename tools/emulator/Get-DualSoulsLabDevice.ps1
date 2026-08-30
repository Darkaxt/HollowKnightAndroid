$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'DualSoulsLab.psm1') -Force

$definition = Get-DualSoulsLabDefinition
$sdkRoot = Resolve-AndroidSdkRoot
$adb = Get-AndroidToolPath -SdkRoot $sdkRoot -Tool adb
$env:ANDROID_AVD_HOME = $definition.AvdHome
$serial = Wait-DualSoulsLabDevice -Definition $definition -AdbPath $adb
if ($serial -eq $definition.ThorSerial) {
    throw 'The Dual Souls lab resolver returned the protected Thor serial.'
}
Write-Output $serial
