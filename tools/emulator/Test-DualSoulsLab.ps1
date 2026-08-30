$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'DualSoulsLab.psm1') -Force

& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Test-DualSoulsLabScripts.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Dual Souls lab script contracts failed.' }

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$launcherRoot = Join-Path $repoRoot 'src\SilksongLauncher.Launcher'
$gradleLauncher = 'D:\Temp\dualsouls-unity-player\android\Tools\gradle\lib\gradle-launcher-8.11.jar'
if (-not (Test-Path -LiteralPath $gradleLauncher -PathType Leaf)) {
    throw "Pinned Gradle launcher is missing: $gradleLauncher"
}

Push-Location $launcherRoot
try {
    & java -classpath $gradleLauncher org.gradle.launcher.GradleMain -p . `
        :app:testDebugUnitTest `
        :emulator-test-app:testDebugUnitTest `
        :emulator-test-app:assembleDebug `
        :emulator-test-app:assembleDebugAndroidTest
    if ($LASTEXITCODE -ne 0) { throw 'Dual Souls lab Gradle gate failed.' }
} finally {
    Pop-Location
}

$definition = Get-DualSoulsLabDefinition
$sdkRoot = Resolve-AndroidSdkRoot
$adb = Get-AndroidToolPath -SdkRoot $sdkRoot -Tool adb
$env:ANDROID_AVD_HOME = $definition.AvdHome
$serial = Wait-DualSoulsLabDevice -Definition $definition -AdbPath $adb
if ($serial -eq $definition.ThorSerial) { throw 'Refusing to run lab tests on the Thor.' }

$appApk = Join-Path $launcherRoot 'emulator-test-app\build\outputs\apk\debug\emulator-test-app-debug.apk'
$testApk = Join-Path $launcherRoot 'emulator-test-app\build\outputs\apk\androidTest\debug\emulator-test-app-debug-androidTest.apk'
$apkHash = (Get-FileHash -LiteralPath $appApk -Algorithm SHA256).Hash.ToLowerInvariant()
$archiveEntries = @(& jar tf $appApk)
if ($LASTEXITCODE -ne 0) { throw 'Could not inspect the lab APK archive.' }
if ($archiveEntries | Where-Object { $_ -match '^lib/arm64-v8a/' }) {
    throw 'The x86-64 lab APK contains an ARM64 native library.'
}

$aapt = Get-ChildItem -LiteralPath (Join-Path $sdkRoot 'build-tools') -Filter aapt.exe -Recurse |
    Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName
if (-not $aapt) { throw 'Android aapt is required for lab package inspection.' }
$badging = (& $aapt dump badging $appApk) -join "`n"
$manifest = (& $aapt dump xmltree $appApk AndroidManifest.xml) -join "`n"
foreach ($requiredBadge in @(
    "package: name='io.github.darkaxt.dualsouls.emutest'",
    "application-label:'Dual Souls Lab'",
    'application-debuggable',
    "targetSdkVersion:'35'",
    "native-code: 'armeabi-v7a' 'x86' 'x86_64'"
)) {
    if (-not $badging.Contains($requiredBadge)) { throw "Lab APK badge is missing: $requiredBadge" }
}
foreach ($requiredManifestValue in @(
    'io.github.darkaxt.dualsouls.LAUNCHER_RUNTIME',
    'io.github.darkaxt.dualsouls.lab.LabLauncherRuntime'
)) {
    if (-not $manifest.Contains($requiredManifestValue)) {
        throw "Lab APK manifest is missing: $requiredManifestValue"
    }
}

& $adb -s $serial install -r $appApk
if ($LASTEXITCODE -ne 0) { throw 'Lab APK installation failed.' }
& $adb -s $serial install -r $testApk
if ($LASTEXITCODE -ne 0) { throw 'Lab test APK installation failed.' }

$instrumentation = & $adb -s $serial shell am instrument -w -r `
    -e class io.github.darkaxt.dualsouls.lab.LabLauncherIntegrationTest `
    'io.github.darkaxt.dualsouls.emutest.test/androidx.test.runner.AndroidJUnitRunner'
$instrumentation | ForEach-Object { Write-Host $_ }
if ($LASTEXITCODE -ne 0 -or ($instrumentation -join "`n") -notmatch '\bOK \(3 tests\)') {
    throw 'Dual Souls lab instrumentation failed.'
}

$launcherComponent = 'io.github.darkaxt.dualsouls.emutest/dev.silksong.launcher.LauncherActivity'
$uiDump = '/data/local/tmp/dualsouls-launcher.xml'
& $adb -s $serial shell am force-stop io.github.darkaxt.dualsouls.emutest
& $adb -s $serial shell am start -W -n $launcherComponent
if ($LASTEXITCODE -ne 0) { throw 'Could not cold-start the lab launcher after instrumentation.' }
& $adb -s $serial shell uiautomator dump $uiDump | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not capture the rendered lab launcher hierarchy.' }
$launcherUi = (& $adb -s $serial shell cat $uiDump) -join "`n"
& $adb -s $serial shell rm $uiDump
foreach ($requiredText in @(
    'TEST RUNTIME',
    'EMULATOR-FAKE',
    'Hollow Knight',
    'Hollow Knight: Silksong',
    'Import saves',
    'Export saves'
)) {
    if (-not $launcherUi.Contains($requiredText)) {
        throw "Rendered lab launcher is missing: $requiredText"
    }
}
foreach ($removedText in @('Download saves', 'Upload saves')) {
    if ($launcherUi.Contains($removedText)) {
        throw "Rendered lab launcher retains the removed transfer label: $removedText"
    }
}
if ($launcherUi -notmatch 'resource-id="io.github.darkaxt.dualsouls.emutest:id/radio_hollow_knight"[^>]*checked="true"') {
    throw 'Rendered lab launcher did not restore the selected Hollow Knight profile.'
}

$storageEvidence = (& $adb -s $serial shell run-as io.github.darkaxt.dualsouls.emutest `
    find files/profiles -type f 2>$null) -join "`n"
if (-not $storageEvidence.Contains('files/profiles/silksong/generations/lab-ss-1/generation.json') -or
    -not $storageEvidence.Contains('files/profiles/silksong/current') -or
    $storageEvidence.Contains('files/profiles/hollow-knight/current')) {
    throw "Profile-scoped storage evidence is unexpected:`n$storageEvidence"
}

Write-Host "Dual Souls lab package: io.github.darkaxt.dualsouls.emutest, SHA-256 $apkHash, no ARM64 JNI"
Write-Host "Dual Souls lab storage: Silksong generation retained after Hollow Knight reset"
Write-Host "Dual Souls lab integration gate: PASS ($serial, API 35, x86_64, EMULATOR-FAKE)"
