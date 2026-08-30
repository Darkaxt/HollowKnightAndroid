Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-DualSoulsLabDefinition {
    [pscustomobject]@{
        Name = 'DualSoulsLabApi35'
        SystemImage = 'system-images;android-35;google_apis;x86_64'
        Device = 'pixel_6'
        AvdHome = 'D:\Android\avd'
        ThorSerial = 'bfa98654'
        Api = '35'
        Abi = 'x86_64'
    }
}

function Resolve-AndroidSdkRoot {
    $candidates = @(
        $env:ANDROID_SDK_ROOT
        $env:ANDROID_HOME
        $(if ($env:LOCALAPPDATA) { Join-Path $env:LOCALAPPDATA 'Android\Sdk' })
    ) | Where-Object { $_ }
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Container) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }
    throw 'Android SDK not found through ANDROID_SDK_ROOT, ANDROID_HOME, or LOCALAPPDATA.'
}

function Get-AndroidToolPath {
    param(
        [Parameter(Mandatory)] [string] $SdkRoot,
        [Parameter(Mandatory)] [ValidateSet('adb', 'emulator', 'avdmanager')] [string] $Tool
    )
    $relative = switch ($Tool) {
        'adb' { 'platform-tools\adb.exe' }
        'emulator' { 'emulator\emulator.exe' }
        'avdmanager' { 'cmdline-tools\latest\bin\avdmanager.bat' }
    }
    $path = Join-Path $SdkRoot $relative
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required Android tool is missing: $path"
    }
    return $path
}

function Get-DualSoulsLabCreateArguments {
    param([Parameter(Mandatory)] $Definition)
    @(
        'create', 'avd',
        '--name', $Definition.Name,
        '--package', $Definition.SystemImage,
        '--device', $Definition.Device,
        '--path', (Join-Path $Definition.AvdHome "$($Definition.Name).avd")
    )
}

function Read-DualSoulsLabIni {
    param([Parameter(Mandatory)] [string] $Path)
    $values = @{}
    foreach ($line in Get-Content -LiteralPath $Path) {
        if ($line -match '^\s*([^#;][^=]*)=(.*)$') {
            $values[$matches[1].Trim()] = $matches[2].Trim()
        }
    }
    return $values
}

function Test-DualSoulsLabAvdConfig {
    param(
        [Parameter(Mandatory)] $Definition,
        [Parameter(Mandatory)] [string] $ConfigPath
    )
    if (-not (Test-Path -LiteralPath $ConfigPath -PathType Leaf)) {
        throw "AVD definition is missing: $ConfigPath"
    }
    $config = Read-DualSoulsLabIni -Path $ConfigPath
    $expectedImage = ($Definition.SystemImage -replace ';', '\') + '\'
    $actual = [ordered]@{
        Device = $config['hw.device.name']
        SystemImage = $config['image.sysdir.1']
    }
    if ($actual.Device -ne $Definition.Device -or
        $actual.SystemImage -ne $expectedImage) {
        throw "Existing AVD does not match the Dual Souls lab definition: $($actual | ConvertTo-Json -Compress)"
    }
    return $true
}

function Test-DualSoulsLabAvdPointer {
    param(
        [Parameter(Mandatory)] $Definition,
        [Parameter(Mandatory)] [string] $PointerPath
    )
    if ((Split-Path -Leaf $PointerPath) -ne "$($Definition.Name).ini" -or
        -not (Test-Path -LiteralPath $PointerPath -PathType Leaf)) {
        throw "AVD pointer is missing or has the wrong name: $PointerPath"
    }
    $pointer = Read-DualSoulsLabIni -Path $PointerPath
    $expectedPath = Join-Path $Definition.AvdHome "$($Definition.Name).avd"
    if ($pointer['path'] -ne $expectedPath -or $pointer['target'] -ne "android-$($Definition.Api)") {
        throw "Existing AVD pointer does not match the Dual Souls lab definition."
    }
    return $true
}

function Resolve-DualSoulsLabSerial {
    param(
        [Parameter(Mandatory)] $Definition,
        [Parameter(Mandatory)] [string[]] $Serials,
        [Parameter(Mandatory)] [scriptblock] $QueryAvdName
    )
    $matchingSerials = @()
    foreach ($serial in $Serials) {
        if ($serial -eq $Definition.ThorSerial -or $serial -notmatch '^emulator-\d+$') {
            continue
        }
        $name = & $QueryAvdName $serial
        if ($name -eq $Definition.Name) {
            $matchingSerials += $serial
        }
    }
    if ($matchingSerials.Count -ne 1) {
        throw "Expected exactly one running $($Definition.Name) emulator; found $($matchingSerials.Count)."
    }
    return $matchingSerials[0]
}

function Get-AdbDeviceSerials {
    param([Parameter(Mandatory)] [string] $AdbPath)
    $lines = & $AdbPath devices
    if ($LASTEXITCODE -ne 0) { throw 'adb devices failed.' }
    @($lines | ForEach-Object {
        if ($_ -match '^(\S+)\s+device$') { $matches[1] }
    })
}

function Get-AdbAvdName {
    param(
        [Parameter(Mandatory)] [string] $AdbPath,
        [Parameter(Mandatory)] [string] $Serial
    )
    $lines = & $AdbPath -s $Serial emu avd name 2>$null
    if ($LASTEXITCODE -ne 0) { return $null }
    @($lines | Where-Object { $_ -and $_ -ne 'OK' })[0]
}

function Get-DualSoulsLabSerial {
    param(
        [Parameter(Mandatory)] $Definition,
        [Parameter(Mandatory)] [string] $AdbPath
    )
    $serials = Get-AdbDeviceSerials -AdbPath $AdbPath
    Resolve-DualSoulsLabSerial -Definition $Definition -Serials $serials -QueryAvdName {
        param($serial)
        Get-AdbAvdName -AdbPath $AdbPath -Serial $serial
    }
}

function Wait-DualSoulsLabDevice {
    param(
        [Parameter(Mandatory)] $Definition,
        [Parameter(Mandatory)] [string] $AdbPath
    )
    while ($true) {
        try {
            $serial = Get-DualSoulsLabSerial -Definition $Definition -AdbPath $AdbPath
            $boot = (& $AdbPath -s $serial shell getprop sys.boot_completed 2>$null).Trim()
            $api = (& $AdbPath -s $serial shell getprop ro.build.version.sdk 2>$null).Trim()
            $abi = (& $AdbPath -s $serial shell getprop ro.product.cpu.abi 2>$null).Trim()
            if ($boot -eq '1' -and $api -eq $Definition.Api -and $abi -eq $Definition.Abi) {
                return $serial
            }
            Write-Host "Dual Souls lab heartbeat: serial=$serial boot=$boot api=$api abi=$abi"
        } catch {
            Write-Host "Dual Souls lab heartbeat: waiting for exact AVD $($Definition.Name)"
        }
        Start-Sleep -Seconds 2
    }
}

Export-ModuleMember -Function @(
    'Get-DualSoulsLabDefinition',
    'Resolve-AndroidSdkRoot',
    'Get-AndroidToolPath',
    'Get-DualSoulsLabCreateArguments',
    'Test-DualSoulsLabAvdConfig',
    'Test-DualSoulsLabAvdPointer',
    'Resolve-DualSoulsLabSerial',
    'Get-DualSoulsLabSerial',
    'Wait-DualSoulsLabDevice'
)
