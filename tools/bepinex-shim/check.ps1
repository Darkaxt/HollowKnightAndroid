# Does this plugin link against our BepInEx?
#
# A published BepInEx plugin references BepInEx.dll and 0Harmony.dll by name,
# and here those are the port's own shims -- a vocabulary and a runtime, not a
# loader. A plugin that calls something the shims do not have cannot be
# converted, and on the device that is discovered by il2cpp, seventeen minutes
# into a build that then fails.
#
# This answers the same question in about a minute, on a machine with a depot:
# it compiles the shims the way the device compiles them, stages them beside
# the game's own assemblies, and runs mod-weaver -- the same weaver, with the
# same resolver -- over the plugin. What comes back is the report the launcher
# would have shown.
#
# It checks that a plugin RESOLVES. It cannot tell you the mod is any good: a
# transpiler is reported here, but so is a patch on a method that exists and
# does something else than the mod's author thought.
#
# Usage:
#   pwsh tools/bepinex-shim/check.ps1 -Plugin path\to\Plugin.dll [-Plugin ...]
#   pwsh tools/bepinex-shim/check.ps1            # just compile the shims
[CmdletBinding()]
param(
    # Plugin DLLs to check. A mod split across several assemblies wants all of
    # them, the same way the mods folder would hold all of them.
    [string[]]$Plugin = @(),
    # Where the game's own assemblies live. Any depot copy will do.
    [string]$Depot,
    # Unity's Android player assemblies, as fetched by `make player`.
    [string]$Player = "$env:USERPROFILE\.cache\silksong\unity-player\android\Variations\il2cpp\Managed",
    # Re-copy the staged assembly set even if one is already cached.
    [switch]$Fresh
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$src  = Join-Path $PSScriptRoot 'src'

# ── the inputs ──────────────────────────────────────────────────────────────

if (-not $Depot) {
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
Pass -Depot <path to ..._Data\Managed>, or set SILKSONG_DEPOT.
"@
    }
    $Depot = $managed.FullName
}
if (-not (Test-Path (Join-Path $Player 'UnityEngine.CoreModule.dll'))) {
    Write-Error "No Android player assemblies at $Player. Run 'make player'."
}

Write-Host "[shim] depot:  $Depot"
Write-Host "[shim] player: $Player"

$work = Join-Path $env:TEMP 'silksong-shim-check'
New-Item -ItemType Directory -Force -Path $work | Out-Null

function Invoke-Dotnet {
    param([string[]]$Arguments, [string]$LogName)

    # Redirected to files rather than piped, for the reason
    # tools/silksong-patches/check.ps1 spells out: MSBuild writing to a
    # console handle while PowerShell buffers the pipeline deadlocks.
    $log = Join-Path $work $LogName
    $proc = Start-Process dotnet -ArgumentList $Arguments -NoNewWindow -PassThru `
        -RedirectStandardOutput $log -RedirectStandardError "$log.err"
    if (-not $proc.WaitForExit(300000)) { $proc.Kill(); Write-Error "[shim] dotnet did not finish within 300s" }

    $out = @()
    foreach ($f in @($log, "$log.err")) {
        if (Test-Path $f) { $out += Get-Content $f -ErrorAction SilentlyContinue }
    }
    return @{ Code = $proc.ExitCode; Output = $out }
}

# ── the shims, compiled as the device compiles them ─────────────────────────
#
# Engine assemblies from the Android player, game assemblies from the depot:
# the same split PackageCompiler.patchReferences uses, and the same one
# tools/silksong-patches/check.ps1 uses for the patches.

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
    'Assembly-CSharp','Assembly-CSharp-firstpass','UnityEngine.UI','Unity.InputSystem'
) | ForEach-Object {
    $p = Join-Path $Depot "$_.dll"
    if (Test-Path $p) { "    <Reference Include=`"$_`"><HintPath>$p</HintPath><Private>false</Private></Reference>" }
}

$shimOut = Join-Path $work 'shim'
New-Item -ItemType Directory -Force -Path $shimOut | Out-Null

foreach ($shim in @(@{ Assembly = 'BepInEx'; Folder = 'bepinex' }, @{ Assembly = '0Harmony'; Folder = 'harmony' })) {
    $dir = Join-Path $src $shim.Folder
    $sources = Get-ChildItem $dir -Recurse -Filter *.cs |
               ForEach-Object { "    <Compile Include=`"$($_.FullName)`" />" }
    if (-not $sources) { Write-Error "[shim] no sources in $dir" }

    $proj = Join-Path $work "$($shim.Assembly).csproj"
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <DefineConstants>UNITY_ANDROID;ENABLE_INPUT_SYSTEM</DefineConstants>
    <AssemblyName>$($shim.Assembly)</AssemblyName>
    <Nullable>disable</Nullable>
    <NoWarn>0169;0414;0649;0067;0108;0114;0436</NoWarn>
    <DisableImplicitNamespaceImports>true</DisableImplicitNamespaceImports>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
  </PropertyGroup>
  <ItemGroup>
$($sources -join "`n")
  </ItemGroup>
  <ItemGroup>
$($engine -join "`n")
$($game -join "`n")
  </ItemGroup>
</Project>
"@ | Set-Content $proj

    Write-Host "[shim] compiling $($shim.Assembly).dll ($($sources.Count) sources)..."
    $r = Invoke-Dotnet -LogName "$($shim.Assembly).log" -Arguments @(
        'build', $proj, '-c', 'Release', '-o', $shimOut,
        '--nologo', '-v', 'quiet', '-nodeReuse:false', '-p:UseSharedCompilation=false')

    $errors = $r.Output | Select-String -Pattern 'error '
    if ($errors) {
        $errors | Select-Object -First 30 | ForEach-Object { Write-Host $_ -ForegroundColor Red }
        Write-Host "[shim] $($shim.Assembly) FAILED ($($errors.Count) error(s))" -ForegroundColor Red
        exit 1
    }
}
Write-Host "[shim] shims compile" -ForegroundColor Green

if (-not $Plugin) { exit 0 }

# ── the staged set the weaver is handed ─────────────────────────────────────
#
# What il2cpp gets on the device: the depot's own assemblies with the shims
# beside them. Copied rather than resolved in place because the weaver rewrites
# what it patches, and a depot is not ours to write to.
#
# Two copies, and the second one is not waste. The weaver writes patched game
# assemblies back into the set and copies each surviving plugin in beside them,
# so a set that was used once is no longer a clean room: the next run would
# weave a plugin's patches into a game that already has them, and would refuse
# the plugin outright for "already part of the game". The pristine copy is made
# once; the working copy is made per run, from it.

$pristine = Join-Path $work 'depot'
$marker = Join-Path $work 'depot.from'
# Keyed by which depot it was taken from, because -Depot is a parameter and a
# cache that ignores it would compile the shims against one game and weave
# against another. An updated depot is caught the same way, by -Fresh, which
# is then an optimisation control rather than a correctness one.
$stale = -not (Test-Path (Join-Path $pristine 'Assembly-CSharp.dll')) -or
         -not (Test-Path $marker) -or
         (Get-Content $marker -Raw).Trim() -ne $Depot
if (($Fresh -or $stale) -and (Test-Path $pristine)) { Remove-Item $pristine -Recurse -Force }
if ($Fresh -or $stale) {
    Write-Host "[shim] staging the depot's assemblies..."
    New-Item -ItemType Directory -Force -Path $pristine | Out-Null
    Copy-Item (Join-Path $Depot '*.dll') $pristine -Force
    Set-Content $marker $Depot
}

$staged = Join-Path $work 'assemblies'
if (Test-Path $staged) { Remove-Item $staged -Recurse -Force }
New-Item -ItemType Directory -Force -Path $staged | Out-Null
Copy-Item (Join-Path $pristine '*.dll') $staged -Force
Copy-Item (Join-Path $shimOut 'BepInEx.dll') $staged -Force
Copy-Item (Join-Path $shimOut '0Harmony.dll') $staged -Force

# ── the weaver ──────────────────────────────────────────────────────────────

$weaver = Join-Path $repo 'tools\mod-weaver\bin\Release\net8.0\ModWeaver.dll'
if (-not (Test-Path $weaver)) {
    Write-Host "[shim] building mod-weaver..."
    $r = Invoke-Dotnet -LogName 'weaver-build.log' -Arguments @(
        'build', (Join-Path $repo 'tools\mod-weaver\ModWeaver.csproj'), '-c', 'Release',
        '--nologo', '-v', 'quiet', '-nodeReuse:false', '-p:UseSharedCompilation=false')
    if (-not (Test-Path $weaver)) {
        $r.Output | Select-String -Pattern 'error ' | Select-Object -First 20 | ForEach-Object { Write-Host $_ -ForegroundColor Red }
        Write-Error "[shim] mod-weaver did not build"
    }
}

$report = Join-Path $work 'report.json'
$argv = @($weaver, 'weave', '--assemblies', $staged, '--report', $report)
foreach ($p in $Plugin) {
    $full = (Resolve-Path $p).Path
    $argv += @('--mod', $full)
    Write-Host "[shim] plugin: $full"
}

$r = Invoke-Dotnet -LogName 'weave.log' -Arguments $argv
$r.Output | Where-Object { $_ } | ForEach-Object { Write-Host "  $_" }
if ($r.Code -ne 0) { Write-Error "[shim] the weaver failed (exit $($r.Code))" }

$failed = 0
foreach ($p in (Get-Content $report -Raw | ConvertFrom-Json).plugins) {
    # A missing member is as fatal as a missing type, whatever the weaver
    # calls it. The weaver only NOTES one, because Cecil cannot always resolve
    # a member of a generic type it resolves perfectly well and refusing a
    # working mod would be worse -- but that judgement is about not blocking a
    # build. This is a check whose entire job is to find that member, and
    # reporting the first four gaps ConfigurationManager had as "Partial, OK"
    # is how it would have missed all four.
    $unresolved = @($p.Issues | Where-Object { $_ -like '*which this build does not have*' })
    $fatal = $p.Status -eq 'Failed' -or $unresolved.Count -gt 0

    $colour = if ($fatal) { 'Red' } elseif ($p.Status -eq 'Ok') { 'Green' } else { 'Yellow' }
    Write-Host ""
    Write-Host "  $($p.File): $(if ($fatal -and $p.Status -ne 'Failed') { 'Failed (unresolved references)' } else { $p.Status })" -ForegroundColor $colour
    if ($p.Name)  { Write-Host "    $($p.Name) $($p.Version) [$($p.Guid)]" }
    Write-Host "    $($p.Patched) patch(es) woven"
    foreach ($issue in $p.Issues) {
        $bad = $issue -like '*which this build does not have*'
        Write-Host "    - $issue" -ForegroundColor $(if ($bad) { 'Red' } else { 'Yellow' })
    }
    if ($fatal) { $failed++ }
}
Write-Host ""
if ($failed -gt 0) { Write-Host "[shim] $failed plugin(s) cannot be built in" -ForegroundColor Red; exit 1 }
Write-Host "[shim] OK" -ForegroundColor Green
