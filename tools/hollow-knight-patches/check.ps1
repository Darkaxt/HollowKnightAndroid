[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Depot,
    [string]$Player = "$env:USERPROFILE\.cache\silksong\unity-player\android\Variations\il2cpp\Managed",
    [string]$Output,
    [switch]$RetainArtifacts
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Depot -PathType Container)) {
    throw "Hollow Knight Assembly-CSharp.dll is missing: depot directory $Depot"
}
$data = Get-Item -LiteralPath $Depot
$managed = if ($data.Name -eq 'Managed') {
    $data.FullName
} else {
    Join-Path $data.FullName 'Managed'
}
$assembly = Join-Path $managed 'Assembly-CSharp.dll'
if (-not (Test-Path -LiteralPath $assembly -PathType Leaf)) {
    throw "Hollow Knight Assembly-CSharp.dll is missing: $assembly"
}

# Original and classic-converted PRE-WEAVE inputs share this manifest's copy identity.
# Read the existing authority, not a caller-selected expected digest.
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$manifestPath = Join-Path $repo 'src/SilksongLauncher.Launcher/app/src/main/assets/profiles/hollow-knight-1.5.12620.json'
$manifestDocument = $null
try {
    $manifestDocument = [System.Text.Json.JsonDocument]::Parse([string](Get-Content -LiteralPath $manifestPath -Raw))
    $manifest = $manifestDocument.RootElement
    if (@($manifest.EnumerateObject() | Group-Object -Property Name -CaseSensitive | Where-Object Count -gt 1).Count -ne 0) {
        throw 'Duplicate manifest properties'
    }
    if ($manifest.GetProperty('profileId').GetString() -cne 'hollow-knight' -or
        $manifest.GetProperty('gameVersion').GetString() -cne '1.5.12620') {
        throw 'Wrong profile or game version'
    }
    $entries = @($manifest.GetProperty('requiredFiles').EnumerateArray() | Where-Object {
        $_.GetProperty('relativePath').GetString() -ceq 'Managed/Assembly-CSharp.dll'
    })
    if ($entries.Count -ne 1) { throw 'Expected one Assembly-CSharp authority entry' }
    $entry = $entries[0]
    if (@($entry.EnumerateObject() | Group-Object -Property Name -CaseSensitive | Where-Object Count -gt 1).Count -ne 0) {
        throw 'Duplicate Assembly-CSharp authority properties'
    }
    $expectedSize = $entry.GetProperty('size').GetInt64()
    $expectedHash = $entry.GetProperty('sha256').GetString()
    if ($expectedSize -le 0 -or $expectedHash -cnotmatch '^[0-9a-f]{64}$' -or
        $entry.GetProperty('action').GetString() -cne 'copy') {
        throw 'Malformed Assembly-CSharp copy identity'
    }
} catch {
    throw "Hollow Knight input authority is invalid: $manifestPath ($($_.Exception.Message))"
} finally {
    if ($manifestDocument) { $manifestDocument.Dispose() }
}
$actualHash = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash.ToLowerInvariant()
if ((Get-Item -LiteralPath $assembly).Length -ne $expectedSize -or $actualHash -cne $expectedHash) {
    throw "Hollow Knight Assembly-CSharp.dll input identity mismatch for 1.5.12620: $assembly (SHA256 $actualHash)"
}

$engine = Join-Path $Player 'UnityEngine.CoreModule.dll'
if (-not (Test-Path -LiteralPath $engine -PathType Leaf)) {
    throw "Android UnityEngine.CoreModule.dll is missing: $engine"
}

$taskTempRoot = if ($env:DUALSOULS_TEMP_ROOT) {
    $env:DUALSOULS_TEMP_ROOT
} elseif (Test-Path -LiteralPath 'D:\Temp' -PathType Container) {
    'D:\Temp'
} else {
    [System.IO.Path]::GetTempPath()
}
if (-not $Output) {
    $Output = Join-Path $taskTempRoot ("dualsouls-hk-patch-check-{0}" -f [Guid]::NewGuid().ToString('N'))
}
$output = [System.IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $output) {
    throw "Refusing to overwrite existing patch-check output: $output"
}
New-Item -ItemType Directory -Path $output | Out-Null

function GetSha256([string]$Path) {
    $stream = [System.IO.File]::OpenRead($Path)
    $hash = [System.Security.Cryptography.SHA256]::Create()
    try { return [System.BitConverter]::ToString($hash.ComputeHash($stream)) }
    finally { $hash.Dispose(); $stream.Dispose() }
}

try {
    & dotnet build (Join-Path $PSScriptRoot 'HollowKnightPatches.csproj') `
        -c Release `
        -o $output `
        --nologo `
        -v minimal `
        -nodeReuse:false `
        -p:UseSharedCompilation=false `
        "-p:BaseIntermediateOutputPath=$(Join-Path $output 'patch-obj')/" `
        "-p:HollowKnightManaged=$managed" `
        "-p:UnityManaged=$Player"
    if ($LASTEXITCODE -ne 0) {
        throw "Hollow Knight patch compile failed with exit code $LASTEXITCODE"
    }
    $dll = Join-Path $output 'HollowKnightPatches.dll'
    if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) {
        throw "Hollow Knight patch compile produced no DLL"
    }
    $weaverOutput = Join-Path $output 'weaver'
    & dotnet build (Join-Path $PSScriptRoot '../mod-weaver/ModWeaver.csproj') `
        -c Release -o $weaverOutput `
        --artifacts-path (Join-Path $output 'weaver-artifacts') `
        --nologo -v minimal -nodeReuse:false -p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) {
        throw "Hollow Knight mandatory weaver build failed with exit code $LASTEXITCODE"
    }
    $staged = Join-Path $output 'staged'
    New-Item -ItemType Directory -Path $staged | Out-Null
    Get-ChildItem -LiteralPath $managed -Filter '*.dll' -File | Copy-Item -Destination $staged
    Copy-Item -LiteralPath $dll -Destination $staged
    $weaver = Join-Path $weaverOutput 'ModWeaver.dll'
    & dotnet $weaver builtin --assemblies $staged
    if ($LASTEXITCODE -ne 0) {
        throw "Hollow Knight mandatory gameplay weave failed with exit code $LASTEXITCODE"
    }
    $woven = Join-Path $staged 'Assembly-CSharp.dll'
    $firstHash = GetSha256 $woven
    & dotnet $weaver builtin --assemblies $staged
    if ($LASTEXITCODE -ne 0) {
        throw "Hollow Knight mandatory prior-weave verification failed with exit code $LASTEXITCODE"
    }
    if ($firstHash -ne (GetSha256 $woven)) {
        throw "Hollow Knight mandatory gameplay weave was not byte-idempotent"
    }
    $entryPoints = (Get-Content (Join-Path $PSScriptRoot 'entrypoints.json') -Raw | ConvertFrom-Json).entryPoints
    Write-Host "[check] OK - HollowKnightPatches.dll $((Get-Item $dll).Length) bytes; $($entryPoints.Count) entry point(s); mandatory gameplay weave verified"
} finally {
    if ($RetainArtifacts) {
        Write-Host "[check] retained compile/weave artifacts: $output"
    } else {
        $resolvedRoot = [System.IO.Path]::GetFullPath($taskTempRoot).TrimEnd('\', '/')
        $resolvedOutput = [System.IO.Path]::GetFullPath($output)
        $ownedPrefix = $resolvedRoot + [System.IO.Path]::DirectorySeparatorChar
        if (-not $resolvedOutput.StartsWith($ownedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clean output outside task temp root: $resolvedOutput"
        }
        Remove-Item -LiteralPath $resolvedOutput -Recurse -Force -ErrorAction SilentlyContinue
    }
}
