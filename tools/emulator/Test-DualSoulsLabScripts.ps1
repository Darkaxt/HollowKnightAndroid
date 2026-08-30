$ErrorActionPreference = 'Stop'

$modulePath = Join-Path $PSScriptRoot 'DualSoulsLab.psm1'
Import-Module $modulePath -Force

$failures = [System.Collections.Generic.List[string]]::new()
function Assert-Equal($Expected, $Actual, [string] $Name) {
    if ($Expected -ne $Actual) {
        $failures.Add("${Name}: expected '$Expected', got '$Actual'")
    }
}
function Assert-Throws([scriptblock] $Action, [string] $Name) {
    try {
        & $Action
        $failures.Add("${Name}: expected an exception")
    } catch {
    }
}

$definition = Get-DualSoulsLabDefinition
Assert-Equal 'DualSoulsLabApi35' $definition.Name 'AVD name'
Assert-Equal 'system-images;android-35;google_apis;x86_64' $definition.SystemImage 'system image'
Assert-Equal 'pixel_6' $definition.Device 'device profile'
Assert-Equal 'D:\Android\avd' $definition.AvdHome 'AVD home'

$arguments = @(Get-DualSoulsLabCreateArguments -Definition $definition)
Assert-Equal 'create|avd|--name|DualSoulsLabApi35|--package|system-images;android-35;google_apis;x86_64|--device|pixel_6|--path|D:\Android\avd\DualSoulsLabApi35.avd' ($arguments -join '|') 'create arguments'

$testRoot = Join-Path 'D:\Temp' "DualSoulsLabScriptTests-$PID"
$matching = Join-Path $testRoot 'matching.ini'
$mismatch = Join-Path $testRoot 'mismatch.ini'
$pointer = Join-Path $testRoot 'DualSoulsLabApi35.ini'
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
try {
    Set-Content -LiteralPath $matching -Encoding utf8 -Value @(
        'hw.device.name=pixel_6'
        'image.sysdir.1=system-images\android-35\google_apis\x86_64\'
    )
    Set-Content -LiteralPath $mismatch -Encoding utf8 -Value @(
        'hw.device.name=pixel_5'
        'image.sysdir.1=system-images\android-35\google_apis\x86_64\'
    )
    Set-Content -LiteralPath $pointer -Encoding utf8 -Value @(
        'path=D:\Android\avd\DualSoulsLabApi35.avd'
        'target=android-35'
    )
    Assert-Equal $true (Test-DualSoulsLabAvdConfig -Definition $definition -ConfigPath $matching) 'matching definition'
    Assert-Equal $true (Test-DualSoulsLabAvdPointer -Definition $definition -PointerPath $pointer) 'matching pointer'
    Assert-Throws { Test-DualSoulsLabAvdConfig -Definition $definition -ConfigPath $mismatch } 'mismatched definition'
} finally {
    Remove-Item -LiteralPath $testRoot -Recurse -Force
}

$devices = @('emulator-5554', 'bfa98654', 'emulator-5556')
$names = @{
    'emulator-5554' = 'AnotherAvd'
    'bfa98654' = 'DualSoulsLabApi35'
    'emulator-5556' = 'DualSoulsLabApi35'
}
$serial = Resolve-DualSoulsLabSerial -Definition $definition -Serials $devices -QueryAvdName { param($candidate) $names[$candidate] }
Assert-Equal 'emulator-5556' $serial 'exact lab emulator selection'
Assert-Throws {
    Resolve-DualSoulsLabSerial -Definition $definition -Serials @('emulator-5554') -QueryAvdName { 'AnotherAvd' }
} 'no first-device fallback'
Assert-Throws {
    Resolve-DualSoulsLabSerial -Definition $definition -Serials @('bfa98654') -QueryAvdName { 'DualSoulsLabApi35' }
} 'Thor serial refusal'

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host 'Dual Souls lab script contracts: PASS'
