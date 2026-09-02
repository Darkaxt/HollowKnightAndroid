# Compile the BepInEx shims and resolve plugins against the same assembly
# precedence used by Il2cppConverter.stageAssemblies in production.
[CmdletBinding()]
param(
    [ValidateSet('hollow-knight', 'silksong')]
    [string]$Profile = 'silksong',
    [string[]]$Plugin = @(),
    [string]$Depot,
    [string]$UnityRoot,
    [string]$Player,
    # Production-built package directories. They are optional when only the
    # shims are being compiled, and required for an exact plugin resolution.
    [string[]]$PackageAssemblies = @(),
    [switch]$Fresh
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$src = Join-Path $PSScriptRoot 'src'
. (Join-Path $PSScriptRoot 'package-manifest.ps1')
$profileSpec = @{
    'silksong' = @{
        UnityVersion = '6000.0.50f1'
        RoslynVersion = '4.12.0'
        MscorlibSha256 = '2efab59f0bdc59e1242b40203aff1f96e529e880f752585286c2816871e4496c'
        DepotId = '1030303'
        GameVersion = '1.0.29980'
    }
    'hollow-knight' = @{
        UnityVersion = '6000.0.61f1'
        RoslynVersion = '4.12.0'
        MscorlibSha256 = 'ac34797a4113d642776394e192cededc7de5b781761de8fac3725a75aa783e9b'
        DepotId = '367522'
        GameVersion = '1.5.12620'
    }
}[$Profile]

if (-not $Player) {
    if ($UnityRoot) {
        $Player = Join-Path $UnityRoot 'android\Variations\il2cpp\Managed'
    } else {
        $Player = "$env:USERPROFILE\.cache\silksong\unity-player\android\Variations\il2cpp\Managed"
    }
}
if (-not $UnityRoot) {
    $UnityRoot = $Player
    1..4 | ForEach-Object { $UnityRoot = Split-Path -Parent $UnityRoot }
}
$Bcl = Join-Path $UnityRoot 'editor\Editor\Data\MonoBleedingEdge\lib\mono\unityaot-linux'

if (-not $Depot) {
    $roots = @()
    if ($Profile -eq 'silksong' -and $env:SILKSONG_DEPOT) { $roots += $env:SILKSONG_DEPOT }
    if ($Profile -eq 'hollow-knight' -and $env:HOLLOW_KNIGHT_DEPOT) { $roots += $env:HOLLOW_KNIGHT_DEPOT }
    $roots += Join-Path (Split-Path -Parent $repo) $(if ($Profile -eq 'silksong') { 'silksong-install' } else { 'hollow-knight-install' })
    $managed = $roots |
        Where-Object { Test-Path $_ } |
        ForEach-Object { Get-ChildItem $_ -Recurse -Directory -Filter 'Managed' -ErrorAction SilentlyContinue } |
        Where-Object { Test-Path (Join-Path $_.FullName 'Assembly-CSharp.dll') } |
        Select-Object -First 1
    if (-not $managed) {
        Write-Error "No $Profile depot found. Pass -Depot <path to the game's Managed directory>."
    }
    $Depot = $managed.FullName
}

foreach ($required in @(
    (Join-Path $Bcl 'mscorlib.dll'),
    (Join-Path $Player 'UnityEngine.CoreModule.dll'),
    (Join-Path $Depot 'Assembly-CSharp.dll')
)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        Write-Error "Required assembly is missing: $required"
    }
}
$bclHash = (Get-FileHash -LiteralPath (Join-Path $Bcl 'mscorlib.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
if ($bclHash -ne $profileSpec.MscorlibSha256) {
    Write-Error "Unity toolchain does not match profile $Profile. Expected Unity $($profileSpec.UnityVersion); pass its root with -UnityRoot."
}
foreach ($directory in $PackageAssemblies) {
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
        Write-Error "Package assembly directory is missing: $directory"
    }
    Test-ProductionPackageManifest -PackageDirectory $directory -Profile $Profile `
        -UnityVersion $profileSpec.UnityVersion -RoslynVersion $profileSpec.RoslynVersion `
        -DepotId $profileSpec.DepotId -GameVersion $profileSpec.GameVersion `
        -BclPath (Join-Path $Bcl 'mscorlib.dll') `
        -PlayerCorePath (Join-Path $Player 'UnityEngine.CoreModule.dll') `
        -DepotAssemblyPath (Join-Path $Depot 'Assembly-CSharp.dll') | Out-Null
}
if ($Plugin.Count -gt 0 -and $PackageAssemblies.Count -eq 0) {
    Write-Error 'Exact mod checking requires production-built package assemblies; pass -PackageAssemblies <profile package directory>.'
}

Write-Host "[shim] profile: $Profile"
Write-Host "[shim] depot:  $Depot"
Write-Host "[shim] player: $Player"
Write-Host "[shim] BCL:    $Bcl"

$work = Join-Path $env:TEMP "dualsouls-shim-check\$Profile"
New-Item -ItemType Directory -Force -Path $work | Out-Null

function Invoke-Dotnet {
    param([string[]]$Arguments, [string]$LogName)
    $log = Join-Path $work $LogName
    $proc = Start-Process dotnet -ArgumentList $Arguments -NoNewWindow -PassThru `
        -RedirectStandardOutput $log -RedirectStandardError "$log.err"
    if (-not $proc.WaitForExit(300000)) {
        $proc.Kill()
        Write-Error '[shim] dotnet did not finish within 300s'
    }
    $out = @()
    foreach ($file in @($log, "$log.err")) {
        if (Test-Path $file) { $out += Get-Content $file -ErrorAction SilentlyContinue }
    }
    return @{ Code = $proc.ExitCode; Output = $out }
}

function Get-FirstWinsAssemblyFiles {
    param([string[]]$Directories)
    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $result = [System.Collections.Generic.List[System.IO.FileInfo]]::new()
    foreach ($directory in $Directories) {
        foreach ($file in @(Get-ChildItem -LiteralPath $directory -File -Filter '*.dll' | Sort-Object Name)) {
            if ($seen.Add($file.Name)) {
                $result.Add($file)
            }
        }
    }
    return $result.ToArray()
}

function Get-AssemblyUniverseDigest {
    param(
        [string]$Profile,
        [string]$Toolchain,
        [string[]]$BaseDirectories,
        [string[]]$OverrideDirectories,
        [string[]]$SourceDirectories
    )
    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("profile=$Profile")
    $lines.Add("toolchain=$Toolchain")
    $tier = 0
    foreach ($directory in @($BaseDirectories + $OverrideDirectories)) {
        foreach ($file in @(Get-ChildItem -LiteralPath $directory -File -Filter '*.dll' | Sort-Object Name)) {
            $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            $lines.Add("assembly=$tier|$($file.Name)|$hash")
        }
        $tier++
    }
    foreach ($directory in $SourceDirectories) {
        foreach ($file in @(Get-ChildItem -LiteralPath $directory -Recurse -File -Filter '*.cs' | Sort-Object FullName)) {
            $relative = [System.IO.Path]::GetRelativePath($directory, $file.FullName).Replace('\', '/')
            $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            $lines.Add("source=$relative|$hash")
        }
    }
    $bytes = [System.Text.Encoding]::UTF8.GetBytes(($lines -join "`n") + "`n")
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([System.BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant()
    } finally {
        $sha.Dispose()
    }
}

function Copy-ProductionAssemblyUniverse {
    param(
        [string[]]$BaseDirectories,
        [string[]]$OverrideDirectories,
        [string]$Destination
    )
    if (Test-Path $Destination) { Remove-Item $Destination -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    foreach ($file in @(Get-FirstWinsAssemblyFiles -Directories $BaseDirectories)) {
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $Destination $file.Name)
    }
    foreach ($directory in $OverrideDirectories) {
        foreach ($file in @(Get-ChildItem -LiteralPath $directory -File -Filter '*.dll' | Sort-Object Name)) {
            Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $Destination $file.Name) -Force
        }
    }
}

# PackageCompiler.patchReferences uses this exact first-wins sequence when it
# compiles shims: unityaot BCL, Android player, then the selected game's depot.
$baseDirectories = @($Bcl, $Player, $Depot)
$compileFiles = @(Get-FirstWinsAssemblyFiles -Directories $baseDirectories)
$references = $compileFiles | ForEach-Object {
    $assembly = [System.IO.Path]::GetFileNameWithoutExtension($_.Name)
    $path = [System.Security.SecurityElement]::Escape($_.FullName)
    "    <Reference Include=`"$assembly`"><HintPath>$path</HintPath><Private>false</Private></Reference>"
}

$shimOut = Join-Path $work 'shim'
if (Test-Path $shimOut) { Remove-Item $shimOut -Recurse -Force }
New-Item -ItemType Directory -Force -Path $shimOut | Out-Null
foreach ($shim in @(@{ Assembly = 'BepInEx'; Folder = 'bepinex' }, @{ Assembly = '0Harmony'; Folder = 'harmony' })) {
    $directory = Join-Path $src $shim.Folder
    $sources = @(Get-ChildItem $directory -Recurse -Filter '*.cs' | ForEach-Object {
        $path = [System.Security.SecurityElement]::Escape($_.FullName)
        "    <Compile Include=`"$path`" />"
    })
    if (-not $sources) { Write-Error "[shim] no sources in $directory" }
    $project = Join-Path $work "$($shim.Assembly).csproj"
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
    <NoStdLib>true</NoStdLib>
    <DisableImplicitFrameworkReferences>true</DisableImplicitFrameworkReferences>
  </PropertyGroup>
  <ItemGroup>
$($sources -join "`n")
  </ItemGroup>
  <ItemGroup>
$($references -join "`n")
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath $project

    Write-Host "[shim] compiling $($shim.Assembly).dll ($($sources.Count) sources)..."
    $result = Invoke-Dotnet -LogName "$($shim.Assembly).log" -Arguments @(
        'build', $project, '-c', 'Release', '-o', $shimOut,
        '--nologo', '-v', 'quiet', '-nodeReuse:false', '-p:UseSharedCompilation=false')
    $errors = @($result.Output | Select-String -Pattern 'error ')
    if ($result.Code -ne 0 -or $errors.Count -gt 0 -or
        -not (Test-Path (Join-Path $shimOut "$($shim.Assembly).dll"))) {
        $errors | Select-Object -First 30 | ForEach-Object { Write-Host $_ -ForegroundColor Red }
        Write-Error "[shim] $($shim.Assembly) failed (exit $($result.Code), $($errors.Count) errors)"
    }
}
Write-Host '[shim] shims compile' -ForegroundColor Green

# Il2cppConverter.stageAssemblies: BCL -> Android player -> depot, first copy
# wins, then every rebuilt package assembly overrides by filename. The freshly
# compiled shims are production package outputs and therefore form the last
# override tier.
$overrideDirectories = @($PackageAssemblies + $shimOut)
$cacheSchema = 2
$dotnetVersion = (& dotnet --version).Trim()
$toolchain = "dotnet:$dotnetVersion"
$contentDigest = Get-AssemblyUniverseDigest -Profile $Profile -Toolchain $toolchain `
    -BaseDirectories $baseDirectories -OverrideDirectories $overrideDirectories -SourceDirectories @($src)
$markerText = "schema=$cacheSchema`nprofile=$Profile`ntoolchain=$toolchain`ncontent=$contentDigest`n"
$pristine = Join-Path $work 'production-universe'
$marker = Join-Path $work 'production-universe.key'
$stale = -not (Test-Path (Join-Path $pristine 'Assembly-CSharp.dll')) -or
    -not (Test-Path $marker) -or (Get-Content $marker -Raw) -ne $markerText
if ($Fresh -or $stale) {
    Write-Host "[shim] staging production assembly universe ($contentDigest)..."
    Copy-ProductionAssemblyUniverse -BaseDirectories $baseDirectories `
        -OverrideDirectories $overrideDirectories -Destination $pristine
    Set-Content -LiteralPath $marker -NoNewline -Value $markerText
}

$staged = Join-Path $work 'assemblies'
Copy-ProductionAssemblyUniverse -BaseDirectories @($pristine) -OverrideDirectories @() -Destination $staged
$assemblyCount = @(Get-ChildItem $staged -File -Filter '*.dll').Count
Write-Host "[shim] production universe: $assemblyCount assemblies; profile=$Profile; toolchain=$toolchain"

if (-not $Plugin) { exit 0 }

$weaver = Join-Path $repo 'tools\mod-weaver\bin\Release\net8.0\ModWeaver.dll'
if (-not (Test-Path $weaver)) {
    Write-Host '[shim] building mod-weaver...'
    $result = Invoke-Dotnet -LogName 'weaver-build.log' -Arguments @(
        'build', (Join-Path $repo 'tools\mod-weaver\ModWeaver.csproj'), '-c', 'Release',
        '--nologo', '-v', 'quiet', '-nodeReuse:false', '-p:UseSharedCompilation=false')
    if ($result.Code -ne 0 -or -not (Test-Path $weaver)) { Write-Error '[shim] mod-weaver did not build' }
}

$report = Join-Path $work 'report.json'
$arguments = @($weaver, 'weave', '--assemblies', $staged, '--report', $report)
foreach ($item in $Plugin) {
    $full = (Resolve-Path $item).Path
    $arguments += @('--mod', $full)
    Write-Host "[shim] plugin: $full"
}
$result = Invoke-Dotnet -LogName 'weave.log' -Arguments $arguments
$result.Output | Where-Object { $_ } | ForEach-Object { Write-Host "  $_" }
if ($result.Code -ne 0) { Write-Error "[shim] the weaver failed (exit $($result.Code))" }

$failed = 0
foreach ($entry in (Get-Content $report -Raw | ConvertFrom-Json).plugins) {
    $unresolved = @($entry.Issues | Where-Object { $_ -like '*which this build does not have*' })
    $fatal = $entry.Status -eq 'Failed' -or $unresolved.Count -gt 0
    $colour = if ($fatal) { 'Red' } elseif ($entry.Status -eq 'Ok') { 'Green' } else { 'Yellow' }
    Write-Host ''
    Write-Host "  $($entry.File): $(if ($fatal -and $entry.Status -ne 'Failed') { 'Failed (unresolved references)' } else { $entry.Status })" -ForegroundColor $colour
    if ($entry.Name) { Write-Host "    $($entry.Name) $($entry.Version) [$($entry.Guid)]" }
    Write-Host "    $($entry.Patched) patch(es) woven"
    foreach ($issue in $entry.Issues) {
        $bad = $issue -like '*which this build does not have*'
        Write-Host "    - $issue" -ForegroundColor $(if ($bad) { 'Red' } else { 'Yellow' })
    }
    if ($fatal) { $failed++ }
}
Write-Host ''
if ($failed -gt 0) {
    Write-Host "[shim] $failed plugin(s) cannot be built in" -ForegroundColor Red
    exit 1
}
Write-Host '[shim] OK' -ForegroundColor Green
