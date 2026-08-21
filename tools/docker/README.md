# Containerised APK build

Builds the APK inside Docker, so the only thing installed on the host is
Docker. No Unity, no JDK, no Android SDK.

This builds the *app*, not the game. The APK contains no game content and
nothing Unity-made; it builds the game on the phone. So the container needs no
Unity licence, no editor, and no copy of the game.

| | |
| --- | --- |
| Base image | `eclipse-temurin:17-jdk-jammy` |
| Installed | Android SDK (platform 36, build-tools 35/36), .NET 8 SDK, bsdtar |
| Fetched at run time | Unity's Android player module, into a container volume |

The JDK is pinned to 17 deliberately: d8 rejects class files newer than it
understands, and a JDK 25 emits major version 69, which fails the dex step
with no obvious connection to the JDK.

## Use

```bash
make docker-image      # build the image
make docker-apk        # build the APK
make install           # put it on the device (the container has no USB)
```

The APK lands in `build/` on the host. The first run downloads Unity's Android
player module (~642 MB) into a named volume; later runs reuse it.

For repeated builds, a container that outlives the build keeps Gradle's daemon
and incremental state warm:

```bash
make docker-up         # start it
make docker-dev        # rebuild, incrementally
make docker-down
```

## What it mounts

| Mount | Why |
| --- | --- |
| the checkout at `/workspace` | sources in, APK out |
| `silksong-unity-player` volume | the player module — container-owned, so the build never reads what the host happened to fetch |
| `silksong-gradle` volume | Gradle's caches, or every run re-resolves AGP and Kotlin |
| the host's NuGet cache | `bundle-surgery` pins both its packages, so the restore needs no network |

`local.properties` and `dev.env` describe the *host* — a Windows SDK path
means nothing in the container, and AGP reads `local.properties` in preference
to `ANDROID_HOME`. The entrypoint moves them aside for the run and puts them
back, including after a container that was killed outright.

## Architecture

**x86-64 only.** Google ships no arm64 Linux build-tools or NDK, so aapt2,
zipalign and apksigner are x86-64 whatever the host is. On an arm64 host
(Apple Silicon, Snapdragon X) this runs under emulation:

```bash
docker run --privileged --rm tonistiigi/binfmt --install amd64
```

That works, and it is several times slower than building natively — fine for
CI, painful as an edit-test loop. On an arm64 machine, build on the host
instead (see [COPILOT.md](../../COPILOT.md)); nothing in the host build needs
x86-64.

.NET's W^X JIT mapping is disabled in the image because qemu-user mishandles
it: `dotnet build` dies with "uncaught target signal 11" under emulation
otherwise.
