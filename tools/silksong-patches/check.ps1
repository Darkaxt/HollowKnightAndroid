# Compile the dual-screen patch sources the way the DEVICE will.
#
# The on-device build compiles tools/silksong-patches/src against the user's
# own depot, which is what lets this code call ToolItemManager or PlayerData as
# ordinary typed C# instead of by reflection. The catch is that a mistake in
# one of those calls is only discovered ~7 minutes later, on the phone, as a
# line in a log.
#
# This does the same compile locally in a few seconds. It is a CHECK, not a
# build: the assembly it produces is thrown away, because the device compiles
# the real one against the depot it already has.
#
# Usage:  pwsh tools/silksong-patches/check.ps1
[CmdletBinding()]
param(
    # Where the game's own assemblies live. Any depot copy will do.
    [string]$Depot,
    # Unity's Android player assemblies, as fetched by `make player`.
    [string]$Player = "$env:USERPROFILE\.cache\silksong\unity-player\android\Variations\il2cpp\Managed"
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$src  = Join-Path $PSScriptRoot 'src'
$sharedSrc = Join-Path $repo 'tools\shared-patches\src'

# Some game singleton accessors log an error as part of reporting that their
# scene object does not exist yet.  A catch block cannot suppress that log, so
# polling one from Update() turns an expected intro/loading state into a stream
# of false errors.  Keep the title-card lookup on Unity's quiet object scan.
$titleCardSource = Get-Content (Join-Path $src 'dualscreen\DsTitleCard.cs') -Raw
if ($titleCardSource -match 'var\s+ui\s*=\s*UIManager\.instance') {
    throw '[check] DsTitleCard must not poll UIManager.instance before the menu scene exists'
}
if ($titleCardSource -notmatch 'FindObjectsOfTypeAll<UIManager>') {
    throw '[check] DsTitleCard must locate UIManager through the quiet Unity object scan'
}

if (-not $Depot) {
    # The depot lives OUTSIDE the checkout -- it is 15 GB of somebody's game,
    # and nothing in the repo should imply it belongs here. SILKSONG_DEPOT
    # wins; otherwise a sibling of the repo is the convention, which is where
    # `make dev` and DepotDownloader tend to leave it.
    $roots = @()
    if ($env:SILKSONG_DEPOT) { $roots += $env:SILKSONG_DEPOT }
    $roots += (Join-Path (Split-Path -Parent $repo) 'silksong-install')

    $managed = $roots |
               Where-Object { Test-Path $_ } |
               ForEach-Object { Get-ChildItem $_ -Recurse -Directory -Filter 'Managed' -ErrorAction SilentlyContinue } |
               Where-Object { Test-Path (Join-Path $_.FullName 'Assembly-CSharp.dll') } |
               Select-Object -First 1

    if (-not $managed) {
        Write-Error @"
No depot found. Looked in:
$($roots | ForEach-Object { "  $_" } | Out-String)
Pass -Depot <path to ..._Data\Managed>, or set SILKSONG_DEPOT to the directory
holding your depot. Use the Linux depot: its shaders are precompiled to Vulkan
SPIR-V, which is what the Android build needs.
"@
    }
    $Depot = $managed.FullName
}
if (-not (Test-Path (Join-Path $Player 'UnityEngine.CoreModule.dll'))) {
    Write-Error "No Android player assemblies at $Player. Run 'make player'."
}

Write-Host "[check] depot:  $Depot"
Write-Host "[check] player: $Player"

$taskTempRoot = if ($env:DUALSOULS_TEMP_ROOT) {
    $env:DUALSOULS_TEMP_ROOT
} elseif (Test-Path -LiteralPath 'D:\Temp' -PathType Container) {
    'D:\Temp'
} else {
    [System.IO.Path]::GetTempPath()
}
$work = Join-Path $taskTempRoot ("dualsouls-patch-check-{0}" -f [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $work | Out-Null

try {
$sources = @($src, $sharedSrc) |
           Where-Object { Test-Path $_ } |
           ForEach-Object { Get-ChildItem $_ -Recurse -Filter *.cs } |
           ForEach-Object { "    <Compile Include=`"$($_.FullName)`" />" }

# Engine assemblies from the Android player, game assemblies from the depot --
# the same split PackageCompiler.patchReferences uses on the device. netstandard
# comes from the depot because Unity's engine assemblies are built against it.
$engine = @(
    'UnityEngine.CoreModule','UnityEngine.InputLegacyModule','UnityEngine.UIModule',
    'UnityEngine.TextRenderingModule','UnityEngine.IMGUIModule','UnityEngine.AndroidJNIModule',
    'UnityEngine.Physics2DModule','UnityEngine.AnimationModule','UnityEngine.ParticleSystemModule',
    'UnityEngine.AudioModule','UnityEngine.UnityWebRequestModule','UnityEngine.ImageConversionModule',
    'UnityEngine.ScreenCaptureModule','UnityEngine.JSONSerializeModule','UnityEngine.SpriteMaskModule',
    'UnityEngine.TilemapModule','UnityEngine.VideoModule','UnityEngine.AssetBundleModule'
) | ForEach-Object {
    $p = Join-Path $Player "$_.dll"
    if (Test-Path $p) { "    <Reference Include=`"$_`"><HintPath>$p</HintPath><Private>false</Private></Reference>" }
}

$game = @(
    'Assembly-CSharp','Assembly-CSharp-firstpass','UnityEngine.UI','Unity.InputSystem',
    'Unity.TextMeshPro','TeamCherry.Localization','TeamCherry.SharedUtils',
    'TeamCherry.NestedFadeGroup','TeamCherry.TK2D','TeamCherry.Cinematics','PlayMaker'
) | ForEach-Object {
    $p = Join-Path $Depot "$_.dll"
    if (Test-Path $p) { "    <Reference Include=`"$_`"><HintPath>$p</HintPath><Private>false</Private></Reference>" }
}
# Deliberately NOT the depot's netstandard.dll. It type-forwards to mscorlib,
# which the SDK does not supply, and the two together make System.Object
# resolve in a cycle -- ~2000 errors that all look like the class library is
# missing. On the device the class library comes from Unity's unityaot profile
# and the depot facade forwards into it; here the SDK's netstandard2.1
# reference pack plays the same role, so the facade is redundant.

@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <DefineConstants>UNITY_ANDROID;ENABLE_INPUT_SYSTEM</DefineConstants>
    <AssemblyName>PatchCheck</AssemblyName>
    <Nullable>disable</Nullable>
    <NoWarn>0169;0414;0649;0108;0114;0436</NoWarn>
    <DisableImplicitNamespaceImports>true</DisableImplicitNamespaceImports>
  </PropertyGroup>
  <ItemGroup>
$($sources -join "`n")
  </ItemGroup>
  <ItemGroup>
$($engine -join "`n")
$($game -join "`n")
  </ItemGroup>
</Project>
"@ | Set-Content (Join-Path $work 'PatchCheck.csproj')

Write-Host "[check] compiling $($sources.Count) source(s)..."

# dotnet's output goes to a FILE, not through the PowerShell pipeline.
#
# Piping it instead (`& dotnet ... 2>&1 | Select-String`) deadlocks
# intermittently: MSBuild writes to the console handle while the pipeline is
# buffering, and the whole thing stops with no output at all -- which looks
# exactly like a slow compile and wasted several minutes before it was
# understood.
#
# The other two flags are there for the same reason, and both are about
# processes that outlive the build:
#   -nodeReuse:false          MSBuild otherwise leaves workers alive holding a
#                             lock on the work directory, so the NEXT run hangs.
#   -p:UseSharedCompilation=false
#                             Roslyn otherwise starts a persistent compiler
#                             server (VBCSCompiler) and reuses it; when one is
#                             left in a bad state -- easy to do when a run is
#                             interrupted -- every later build waits on it
#                             forever. A throwaway compile gains nothing from
#                             either daemon.

$log = Join-Path $work 'build.log'
$proc = Start-Process dotnet `
    -ArgumentList 'build', (Join-Path $work 'PatchCheck.csproj'),
                  '-c', 'Release', '-o', (Join-Path $work 'bin'),
                  '--nologo', '-v', 'quiet', '-nodeReuse:false',
                  '-p:UseSharedCompilation=false' `
    -NoNewWindow -PassThru -RedirectStandardOutput $log -RedirectStandardError "$log.err"

$proc.WaitForExit()
$exitCode = $proc.ExitCode

$out = @()
foreach ($f in @($log, "$log.err")) {
    if (Test-Path $f) { $out += Get-Content $f -ErrorAction SilentlyContinue }
}
$errors = $out | Select-String -Pattern 'error '
if ($errors) {
    $errors | Select-Object -First 30 | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    throw "[check] FAILED ($($errors.Count) error(s))"
}
if ($exitCode -ne 0) {
    throw "[check] dotnet build exited with $exitCode"
}
Write-Host "[check] OK - $($sources.Count) sources compile against the depot" -ForegroundColor Green

# The entry-point list is hand-written (a player does not scan for
# [RuntimeInitializeOnLoadMethod]), so an entry point that is added to a .cs
# file and not to entrypoints.json is silently never called. Printing the count
# beside a successful compile is a cheap reminder that the two are separate.
$ep = Join-Path $PSScriptRoot 'entrypoints.json'
if (Test-Path $ep) {
    $count = (Get-Content $ep -Raw | ConvertFrom-Json).entryPoints.Count
    Write-Host "[check] entry points: $count"
}
} finally {
    $resolvedRoot = [System.IO.Path]::GetFullPath($taskTempRoot).TrimEnd('\', '/')
    $resolvedWork = [System.IO.Path]::GetFullPath($work)
    $ownedPrefix = $resolvedRoot + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolvedWork.StartsWith($ownedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean work directory outside task temp root: $resolvedWork"
    }
    Remove-Item -LiteralPath $resolvedWork -Recurse -Force -ErrorAction SilentlyContinue
}
