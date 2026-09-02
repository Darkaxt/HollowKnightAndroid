# On-device IL2CPP build

Builds Silksong's `libil2cpp.so` **on the Android device itself** — no PC, no
Unity Editor, no emulation. Verified end to end on an AYN Thor (QCS8550,
8 cores, 11.5 GB RAM): the full game converts and compiles on the phone, and
the resulting library boots inside the APK.

Both halves of the native build run on the device:

| Stage | On-device result |
| --- | --- |
| IL → C++ (`il2cpp.dll`) | 185 assemblies → 946 `.cpp` + 196 `.c` + 35.5 MB metadata, **28 s** |
| C++ → `libil2cpp.so` (clang) | 1502 TUs → 293 MB, 241 `il2cpp_*` exports, **16.7 min** |

The conversion is not merely equivalent to a PC run, it is *identical*: all
1142 generated sources and `global-metadata.dat` match a desktop conversion
byte for byte by SHA-256.

## Why this matters

The APK splits cleanly along the copyright line:

| Component | Origin | Redistributable |
| --- | --- | --- |
| `lib/arm64-v8a/libil2cpp.so` | compiled from Team Cherry's code | **no** |
| `assets/bin/Data/Managed/Metadata/global-metadata.dat` (35.5 MB) | ditto | **no** |
| `lib/arm64-v8a/libunity.so` (26 MB) | stock Unity, identical for every game | yes |
| `libmain.so`, `libc++_shared.so`, launcher hooks | generic / ours | yes |

`libunity.so` never links against the game: it has zero undefined `il2cpp_*`
symbols, `dlopen`s a library named `il2cpp`, and resolves ~241 entry points by
`dlsym`. No `classes*.dex` mentions il2cpp at all. So the engine will accept
*any* `libil2cpp.so` built by the matching Unity version — which is what makes
an on-device build worth doing.

Both game-derived artifacts are now produced on the device from the user's own
Steam depot, so a distributable APK need contain no part of the game.

## What is proven

| Step | Status |
| --- | --- |
| .NET running on Android arm64 | **works** — glibc bundle, no proot, no emulation |
| Unity's `il2cpp.dll` converter (IL → C++) on device | **works** — 28 s, output byte-identical to PC |
| `clang` running on Android | **works** — Termux's aarch64-hosted clang 21 |
| Compiling the generated C++ on device | **works** — 946/946 C++, 196/196 C |
| Compiling libil2cpp + bdwgc + zlib + brotli on device | **works** — 360/360 + GC + 14/14 + 29/29 |
| Linking `libil2cpp.so` on device | **works** — 293 MB, 241 `il2cpp_*` exports |
| Loading it (`dlopen` + `dlsym`) on device | **works** |
| Swapping it into the APK and booting the game | **works** — reaches the game's own managed code |
| UnityLinker stripping on device | *not yet tested* |
| Incremental IL → C++ conversion | **does not work** — see below |

## Why the conversion is not incremental

It is the largest fixed cost in a rebuild and it should be avoidable, so this
was measured rather than assumed. It is not avoidable with the converter we
have.

A mod-only rebuild changes **28 of the 1160** generated files: the native build
hashes each one and recompiles only those, finishing in about a minute, while
the conversion that produced them takes three and a half regardless. So the
conversion is ~75% of a rebuild and 97.6% of its output is byte-identical to
the output it replaced.

il2cpp does have the machinery, and its options are reachable: `Unity.Options`
derives every flag from a field name by lowercasing it and inserting a hyphen
before each capital (`OptionsParser.NormalizeName`), which is why this port
already passes the odd-looking `--static-lib-il2-cpp` for `StaticLibIl2Cpp`.
The full surface is the public fields of the `[ContainsOptions]` types in
`Unity.IL2CPP.Api.dll`. Measured on an AYN Thor, whole game:

| | Conversion | Native compile |
| --- | --- | --- |
| as shipped | **201 s** | **72 s** (28 of 1160 rebuilt) |
| `--code-conversion-cache` | **dies after 15 s** | — |
| `--cachedirectory=<dir>` alone | accepted, changes nothing | — |
| `--conversion-mode=PartialPerAssemblyInProcess` | 288 s | 532 s |
| `--jobs=8` | 200 s | — |

`--code-conversion-cache` is the one that would matter and it takes the process
down with it: fifteen seconds in, 1.9 GB allocated, no exit code, and nothing
written to stdout or stderr. That is a native death rather than a managed
error -- il2cpp's own exit codes are 0 to 4 and -1, and what the launcher
reports is its own "the run never started" value. It behaves the same with the
cache directory on internal storage, so it is not the filesystem refusing the
links a build cache is made of. The likely reason is that the cache belongs to
the Bee-driven `--convert-in-graph` path, which expects a build backend that
cannot run here.

`PartialPerAssemblyInProcess` works and is worse twice over: the conversion is
43% slower, and it emits different C++ (960 files rather than 958) so the
object cache misses and the native build goes from 72 s to 532 s.

`--jobs` is already saturated; the converter uses the cores it is given.

So the conversion stays whole-program. What makes rebuilds bearable is
downstream: `build-il2cpp.sh` hashes every generated file and skips the
unchanged ones.

## Running .NET on Android

Unity's converter is a .NET application, and Android has no .NET. Microsoft
publishes no standalone runtime for it, and the `linux-bionic-arm64` runtime
packs are awkward to obtain.

None of that turned out to matter. Android is Linux; the only thing that
differs is the C library, and a C library is just a shared library. Carrying
Debian's glibc along beside Microsoft's ordinary `linux-arm64` runtime is
enough to run .NET on a stock, unrooted phone at full native speed — no proot,
no chroot, no emulation. `DotnetFetcher` assembles that bundle on the device
from Microsoft's runtime tarball and Debian's package pool.

Two things make it work:

**Invoke the loader, not the program.** A glibc binary names its interpreter by
absolute path (`/lib/ld-linux-aarch64.so.1`), which does not exist on Android.
Running the loader explicitly and handing it the program sidesteps the missing
path entirely:

```
$DN/lib/ld-linux-aarch64.so.1 --library-path $DN/lib <program> <args...>
```

**Give the loader the muxer's name.** Launched that way, `dotnet` fails with
*"cannot execute dotnet when renamed to ld-linux-aarch64.so.1"* — the muxer
identifies itself from `/proc/self/exe`, which is now the loader. `--argv0`
does not help, because it is not argv[0] being checked. The fix is to put a
copy of the loader named `dotnet` inside the dotnet root and move the real
muxer aside: the name check passes, and `host/fxr` still resolves relative to
it.

```
$DN/dotnet/dotnet --library-path $DN/lib $DN/dotnet/dotnet.real --list-runtimes
# Microsoft.NETCore.App 8.0.30 [...]
```

`il2cpp` itself needs one more change: Unity ships it self-contained, with a
private CoreCLR and a Linux-x64 apphost. `Il2cppConverter` deletes the
private runtime and the `deps.json` and writes a `runtimeconfig.json` asking
for a shared framework instead, which turns `il2cpp.dll` back into an ordinary
portable .NET app.

## The five non-obvious flags

These are not guessable; all were recovered from Unity's own build graph
(`build/project/Library/Bee/Player*.dag.json`) after hours of failures:

1. **`-DBASELIB_INLINE_NAMESPACE=il2cpp_baselib`** — without it baselib's classes
   land directly in `namespace baselib` and collide with il2cpp's own forward
   declarations. Every TU including `il2cpp-object-internals.h` then fails with
   *"reference to 'ReentrantLock' is ambiguous"*.
2. **bdwgc must be built as the amalgamated `extra/gc.c`**, not as its
   individual sources. Several cross-file helpers (e.g. `maybe_finalize`) are
   `static`, so a per-file build links but dies at `dlopen`.
3. **zlib needs `-DZ_PREFIX`** — Unity renames every symbol to `il2cpp_z_*` in
   `zconf.h`, and the runtime only resolves against a prefixed build.
4. **`baselib.a` is prebuilt-only.** It ships in the Editor at
   `PlaybackEngines/AndroidPlayer/Variations/il2cpp/Release/StaticLibs/arm64-v8a/`
   and cannot be compiled from `external/baselib`. Without it the link succeeds
   but `dlopen` fails on `Baselib_Timer_TickToNanosecondsConversionFactor`.
5. **brotli's headers include `il2cpp-config.h`**, so `-I<libil2cpp>` is needed
   when compiling it. Skip brotli and the link still succeeds, but `dlopen`
   fails on `BrotliDecoderCreateInstance`.

Also required: `-stdlib=libc++` (Android has no libstdc++ headers, so even
`<cmath>` fails without it). On a *cross* build add `-rtlib=compiler-rt`, since
Ubuntu's clang defaults to libgcc.

One trap specific to building on the device: **do not force the NDK's
`-resource-dir`.** The resource dir carries compiler-specific headers,
`arm_neon.h` among them. Termux's clang 21 against the NDK's clang 18 resource
dir rejects brotli's NEON intrinsics with 20 *"incompatible constant for this
`__builtin_neon` function"* errors. Let the compiler use its own resource dir;
only the sysroot needs to come from the NDK.

Object files must also be named from each source's **full path**, not its
basename: libil2cpp has 360 sources but only 247 distinct file names.

## How it runs now

Everything below used to be a manual `adb push` procedure with a set of
host-side helper scripts. The app does all of it now: `UnityFetcher`,
`ToolchainFetcher` and `DotnetFetcher` fetch the pieces, `Il2cppConverter`
runs the conversion, and `NativeBuild` runs `build-il2cpp.sh` -- the one
script here that still ships, as an APK asset.

The staging that used to be done by hand:

| Piece | Where it comes from |
| --- | --- |
| clang, lld, libc++ | Termux's aarch64 packages |
| NDK sysroot | Google's NDK r27 |
| .NET runtime + glibc | Microsoft's CDN and Debian's archive |
| `il2cpp.dll`, `baselib.a`, IL2CPP sources | Unity's own downloads |
| Generated C++ | produced on the device from the user's depot |

`Il2cppConverter` strips il2cpp's private CoreCLR and writes its own
`runtimeconfig.json`, which is what makes Unity's converter run on a phone --
the same trick the retired `make-il2cpp-tool.sh` did on a PC.

The assemblies fed to the converter are not simply the depot's `Managed/`
folder -- that holds the *Mono* class library, which fails deep inside the
converter. See `tools/depot-to-apk/README.md`.

## Measured cost

Whole game on an AYN Thor (Snapdragon 8 Gen 2, 8 cores), clang 21.1.8:

| Phase | Units | Time |
| --- | --- | --- |
| IL → C++ conversion | 185 assemblies | 28 s |
| A — generated C++ | 947 | 560 s |
| B — generated C | 197 | 17 s |
| C — libil2cpp runtime | 360 | 66 s |
| bdwgc + zlib + brotli | 43 | 8 s |
| link | | 5 s |
| **total** | **1547** | **664 s** |

Phase A is 84% of it; the C phases run about seven times faster per unit.
Earlier runs took ~830 s for phase A alone -- the precompiled header, an
`xargs -P` pool instead of batch-and-wait, and largest-first ordering account
for the difference.

Peak disk is roughly 3 GB of sources, objects and the output, plus ~1.5 GB for
the generated C++.

The unstripped `libil2cpp.so` is 300 MB; running UnityLinker first would cut
both this and the compile time substantially.
