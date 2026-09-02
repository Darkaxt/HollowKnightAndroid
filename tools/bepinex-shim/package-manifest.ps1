# Strict validation for production package assemblies consumed by check.ps1.
function Test-ProductionPackageManifest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$PackageDirectory,
        [Parameter(Mandatory)][ValidateSet('hollow-knight', 'silksong')][string]$Profile,
        [Parameter(Mandatory)][string]$UnityVersion,
        [Parameter(Mandatory)][string]$RoslynVersion,
        [Parameter(Mandatory)][string]$DepotId,
        [Parameter(Mandatory)][string]$GameVersion,
        [Parameter(Mandatory)][string]$BclPath,
        [Parameter(Mandatory)][string]$PlayerCorePath,
        [Parameter(Mandatory)][string]$DepotAssemblyPath
    )

    if (-not (Test-Path -LiteralPath $PackageDirectory -PathType Container)) {
        throw "Package assembly directory is missing: $PackageDirectory"
    }
    $manifestPath = Join-Path $PackageDirectory 'dualsouls-package-assemblies-v1.properties'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Production package assembly manifest is missing: $manifestPath"
    }
    if ((Get-Item -LiteralPath $manifestPath).Length -gt 65536) {
        throw "Production package assembly manifest is too large: $manifestPath"
    }

    $values = [System.Collections.Generic.Dictionary[string,string]]::new(
        [System.StringComparer]::Ordinal
    )
    foreach ($raw in Get-Content -LiteralPath $manifestPath -Encoding UTF8) {
        $line = $raw.Trim()
        if (-not $line -or $line.StartsWith('#') -or $line.StartsWith('!')) { continue }
        $separator = $line.IndexOf('=')
        if ($separator -le 0) { throw "Malformed production package manifest line: $line" }
        $key = $line.Substring(0, $separator)
        $value = $line.Substring($separator + 1)
        if (-not $values.TryAdd($key, $value)) {
            throw "Duplicate production package manifest field: $key"
        }
    }

    $requiredAssemblies = @('Unity.InputSystem.dll', '0Harmony.dll', 'BepInEx.dll')
    $selectedPatch = if ($Profile -eq 'hollow-knight') { 'HollowKnightPatches.dll' } else { 'SilksongPatches.dll' }
    $oppositePatch = if ($Profile -eq 'hollow-knight') { 'SilksongPatches.dll' } else { 'HollowKnightPatches.dll' }
    $requiredAssemblies += $selectedPatch
    if ($Profile -eq 'silksong') { $requiredAssemblies += 'SilksongIo.dll' }

    $expected = [ordered]@{
        schema = '1'
        profile = $Profile
        unityVersion = $UnityVersion
        roslynVersion = $RoslynVersion
        steamDepotId = $DepotId
        gameVersion = $GameVersion
    }
    foreach ($entry in $expected.GetEnumerator()) {
        if (-not $values.ContainsKey($entry.Key) -or $values[$entry.Key] -cne $entry.Value) {
            throw "Production package manifest $($entry.Key) does not match $Profile"
        }
    }

    $authorityFiles = [ordered]@{
        unityMscorlibSha256 = $BclPath
        androidCoreModuleSha256 = $PlayerCorePath
        depotAssemblySha256 = $DepotAssemblyPath
    }
    foreach ($entry in $authorityFiles.GetEnumerator()) {
        if (-not (Test-Path -LiteralPath $entry.Value -PathType Leaf)) {
            throw "Package manifest authority is missing: $($entry.Value)"
        }
        $actual = (Get-FileHash -LiteralPath $entry.Value -Algorithm SHA256).Hash.ToLowerInvariant()
        if (-not $values.ContainsKey($entry.Key) -or $values[$entry.Key] -cne $actual) {
            throw "Production package manifest authority is stale: $($entry.Key)"
        }
    }

    if (Test-Path -LiteralPath (Join-Path $PackageDirectory $oppositePatch)) {
        throw "Opposite-profile package assembly is present: $oppositePatch"
    }
    foreach ($name in $requiredAssemblies) {
        $path = Join-Path $PackageDirectory $name
        $key = "assembly.$name"
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Required production package assembly is missing: $name"
        }
        $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        if (-not $values.ContainsKey($key) -or $values[$key] -cne $actual) {
            throw "Production package assembly digest is stale: $name"
        }
    }

    $expectedKeys = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::Ordinal
    )
    foreach ($key in @($expected.Keys + $authorityFiles.Keys)) { [void]$expectedKeys.Add($key) }
    foreach ($name in $requiredAssemblies) { [void]$expectedKeys.Add("assembly.$name") }
    foreach ($key in $values.Keys) {
        if (-not $expectedKeys.Contains($key)) {
            throw "Unexpected production package manifest field: $key"
        }
    }
    if ($values.Count -ne $expectedKeys.Count) {
        throw 'Production package assembly manifest is incomplete'
    }

    return $true
}
