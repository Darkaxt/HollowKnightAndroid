[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Depot,
    [string]$Player = "$env:USERPROFILE\.cache\silksong\unity-player\android\Variations\il2cpp\Managed"
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
$output = Join-Path $taskTempRoot ("dualsouls-hk-patch-check-{0}" -f [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $output | Out-Null

try {
    & dotnet build (Join-Path $PSScriptRoot 'HollowKnightPatches.csproj') `
        -c Release `
        -o $output `
        --nologo `
        -v minimal `
        -nodeReuse:false `
        -p:UseSharedCompilation=false `
        "-p:HollowKnightManaged=$managed" `
        "-p:UnityManaged=$Player"
    if ($LASTEXITCODE -ne 0) {
        throw "Hollow Knight patch compile failed with exit code $LASTEXITCODE"
    }
    $dll = Join-Path $output 'HollowKnightPatches.dll'
    if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) {
        throw "Hollow Knight patch compile produced no DLL"
    }
    $entryPoints = (Get-Content (Join-Path $PSScriptRoot 'entrypoints.json') -Raw | ConvertFrom-Json).entryPoints
    Write-Host "[check] OK - HollowKnightPatches.dll $((Get-Item $dll).Length) bytes; $($entryPoints.Count) entry point(s)"
} finally {
    $resolvedRoot = [System.IO.Path]::GetFullPath($taskTempRoot).TrimEnd('\', '/')
    $resolvedOutput = [System.IO.Path]::GetFullPath($output)
    $ownedPrefix = $resolvedRoot + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolvedOutput.StartsWith($ownedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean output outside task temp root: $resolvedOutput"
    }
    Remove-Item -LiteralPath $resolvedOutput -Recurse -Force -ErrorAction SilentlyContinue
}
