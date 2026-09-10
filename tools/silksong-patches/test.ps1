[CmdletBinding()]
param(
    [string]$Player = "$env:USERPROFILE\.cache\silksong\unity-player\android\Variations\il2cpp\Managed",
    [string]$AndroidSdk = "$env:LOCALAPPDATA\Android\Sdk"
)

$ErrorActionPreference = 'Stop'
$tests = Join-Path $PSScriptRoot 'tests'
$shell = Join-Path (Split-Path -Parent $PSScriptRoot) 'depot-to-apk\shell'
$unityJar = Join-Path (Split-Path -Parent $Player) 'Release\Classes\classes.jar'
$androidJar = Get-ChildItem (Join-Path $AndroidSdk 'platforms') -Recurse -Filter android.jar |
    Sort-Object FullName -Descending | Select-Object -First 1
if (-not $androidJar -or -not (Test-Path $unityJar)) {
    throw 'The tests need the Android SDK and Unity player module; see COPILOT.md.'
}

$work = [System.IO.Path]::GetFullPath((Join-Path ([System.IO.Path]::GetTempPath()) (
    'silksong-dualscreen-tests-' + [Guid]::NewGuid().ToString('N'))))
New-Item -ItemType Directory -Path $work | Out-Null
try {
    $managed = Join-Path $work 'managed'
    dotnet build (Join-Path $tests 'DualScreenTests.csproj') -o $managed `
        "-p:Player=$Player" "-p:BaseIntermediateOutputPath=$work\obj\" `
        --nologo -v quiet -nodeReuse:false -p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) { throw 'Gesture test compilation failed' }
    dotnet (Join-Path $managed 'DualScreenTests.dll')
    if ($LASTEXITCODE -ne 0) { throw 'Gesture regression tests failed' }

    $javaOut = Join-Path $work 'java'
    New-Item -ItemType Directory -Path $javaOut | Out-Null
    $cp = $unityJar + [System.IO.Path]::PathSeparator + $androidJar.FullName
    javac --release 17 -nowarn -cp $cp -d $javaOut `
        (Join-Path $shell 'PlayerActivity.java') (Join-Path $shell 'SecondaryDisplay.java') `
        (Join-Path $tests 'SecondaryDisplayTest.java')
    if ($LASTEXITCODE -ne 0) { throw 'Secondary-display test compilation failed' }
    java -cp ($javaOut + [System.IO.Path]::PathSeparator + $cp) dev.silksong.shell.SecondaryDisplayTest
    if ($LASTEXITCODE -ne 0) { throw 'Secondary-display regression tests failed' }
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force
}
