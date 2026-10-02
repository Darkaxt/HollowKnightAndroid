[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Depot,
    [string]$Player = "$env:USERPROFILE\.cache\silksong\unity-player\android\Variations\il2cpp\Managed",
    [string]$Output,
    [switch]$RetainArtifacts
)

$ErrorActionPreference = 'Stop'
$data = Get-Item -LiteralPath $Depot
$managed = if ($data.Name -eq 'Managed') {
    $data.FullName
} else {
    Join-Path $data.FullName 'Managed'
}
$assembly = Join-Path $managed 'Assembly-CSharp.dll'
$engine = Join-Path $Player 'UnityEngine.CoreModule.dll'
if (-not (Test-Path -LiteralPath $assembly -PathType Leaf)) {
    throw "Hollow Knight Assembly-CSharp.dll is missing: $assembly"
}
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
