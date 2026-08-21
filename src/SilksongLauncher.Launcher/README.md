# SilksongLauncher.Launcher

The app. An Android library module, built into an AAR and packaged into the
APK by `tools/depot-to-apk/build.sh`.

It is not a menu in front of the game — it is what builds the game. On first
run it fetches Unity's toolchain, signs in to Steam, downloads the user's own
depot, converts the game's assemblies to C++, compiles them with clang, and
assembles the player image, all on the device.

## Layout

```
app/src/main/
├── kotlin/dev/silksong/launcher/
│   ├── SetupActivity.kt        # first-run flow: one question, one button, one bar
│   ├── LauncherActivity.kt     # the menu once the game is built
│   ├── LoginActivity.kt        # Steam sign-in, incl. QR
│   ├── SettingsActivity.kt     # the toggles the game reads at launch
│   ├── UnityFetcher.kt         # Unity's editor pieces and Android module
│   ├── UnityDex.kt             # dexes Unity's player classes on the device
│   ├── ToolchainFetcher.kt     # clang + NDK sysroot
│   ├── DotnetFetcher.kt        # .NET 8 + glibc
│   ├── DepotFetcher.kt         # the game, from Steam, via JavaSteam
│   ├── Il2cppConverter.kt      # IL -> C++
│   ├── NativeBuild.kt          # C++ -> libil2cpp.so
│   ├── PlayerImage.kt          # assembles the player image and data package
│   ├── PackageCompiler.kt      # compiles the port's patches and the Input System
│   └── Toolchain.kt            # running fetched binaries at all (see targetSdk 28)
├── java/dev/silksong/dualscreen/   # the presentation the DualScreen patch drives
└── assets/ondevice/                # the patch sources and build script shipped to the device
```

## Building

Normally not directly — `make dev` builds this and packages it in one step.
On its own:

```bash
cd src/SilksongLauncher.Launcher
java -classpath "<player-module>/Tools/gradle/lib/gradle-launcher-8.11.jar" \
     org.gradle.launcher.GradleMain :app:assembleRelease :app:collectRuntimeDeps
```

No Gradle wrapper is checked in; the Android player module carries Gradle
8.11, which is what the build uses. `collectRuntimeDeps` materialises the
runtime classpath so the packaging step can dex it.

## Notes

- **The APK contains no Unity classes.** This module compiles against
  `classes.jar` as a library only; `UnityDex` dexes it on the device and
  injects the result into the app's class loader from
  `SilksongApp.attachBaseContext`, before any activity is loaded.
- **targetSdk 28 is load-bearing.** `untrusted_app_27` is the only SELinux
  domain that permits `execute_no_trans` on app data, which is what lets the
  fetched clang and .NET run at all.
- **The patches are C# source**, compiled on the device against the depot's
  own assemblies, which is what gives them typed access to the game's classes.
