$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'DualSoulsLab.psm1') -Force

$definition = Get-DualSoulsLabDefinition
$sdkRoot = Resolve-AndroidSdkRoot
$adb = Get-AndroidToolPath -SdkRoot $sdkRoot -Tool adb
$emulator = Get-AndroidToolPath -SdkRoot $sdkRoot -Tool emulator
$env:ANDROID_AVD_HOME = $definition.AvdHome

try {
    $serial = Get-DualSoulsLabSerial -Definition $definition -AdbPath $adb
    Write-Host "Dual Souls lab AVD is already running as $serial"
} catch {
    Start-Process -FilePath $emulator -ArgumentList @(
        '-avd', $definition.Name,
        '-no-snapshot-save',
        '-no-boot-anim'
    ) -WindowStyle Hidden | Out-Null
}

$serial = Wait-DualSoulsLabDevice -Definition $definition -AdbPath $adb
Write-Output $serial
