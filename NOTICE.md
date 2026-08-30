# Notices

This project is MIT-licensed (see `LICENSE`). The APK it produces also
redistributes third-party components, listed below with their licences.

## What the APK does not contain

Stated first because it is the point of the whole design:

* **No Unity engine code.** The APK links against `com.unity3d.player.*` without
  containing it — Unity's `classes.jar` is a compile-time `--lib` only, and the
  classes it references are dexed on the device and injected into the app's
  class loader at process start. `apkanalyzer dex packages --defined-only`
  reports **zero** classes defined in the `com.unity3d` package.
* **No game code, art or audio.** Silksong is © Team Cherry. Nothing of the
  game travels in the APK; it is downloaded from the user's own Steam depot, on
  their own device, with their own account.
* **No Unity binaries.** `libunity.so`, `libmain.so` and `libil2cpp.so` are
  fetched or built on the device.

## Redistributed in the APK

### Apache License 2.0

* **D8** (`assets/d8.zip`) — Copyright (c) The Android Open Source Project
* **AndroidX** (annotation, startup, tracing) — Copyright (c) The Android Open
  Source Project
* **Kotlin standard library** and **kotlinx** (coroutines, serialization, io) —
  Copyright (c) JetBrains s.r.o. and Kotlin Programming Language contributors
* **Ktor** — Copyright (c) JetBrains s.r.o.
* **JetBrains Java Annotations** — Copyright (c) JetBrains s.r.o.
* **OkHttp** and **Okio** — Copyright (c) Square, Inc.
* **Apache Commons Compress** — Copyright (c) The Apache Software Foundation
* **ZXing** ("Zebra Crossing") — Copyright (c) ZXing authors

A copy of the Apache License 2.0 is at
<https://www.apache.org/licenses/LICENSE-2.0>.

### MIT License

* **.NET** — Copyright (c) .NET Foundation and Contributors. The Mono build of
  the .NET runtime for `android-arm64`, from
  `Microsoft.NETCore.App.Runtime.Mono.android-arm64`: the native libraries in
  `lib/arm64-v8a/` (`libmonosgen-2.0.so`, its components, and the
  `libSystem.*.so` shims) and the class library in `assets/mono-bcl/`. The
  launcher runs il2cpp, Roslyn and bundle-surgery on it, on the device.

  Upstream's own `LICENSE.TXT` and `THIRD-PARTY-NOTICES.TXT` are shipped
  beside the assemblies, in `assets/mono-bcl/`.
* **JavaSteam** and **JavaSteam Depot Downloader** — Copyright (c) Lossy and
  JavaSteam contributors
* **AssetsTools.NET** — Copyright (c) nesrak1
* **Mono.Cecil** — Copyright (c) Jb Evain
* **AssetRipper.Primitives** — Copyright (c) AssetRipper contributors
* **SLF4J** — Copyright (c) QOS.ch

### BSD family

* **protobuf-java** — Copyright (c) Google Inc. — BSD 3-Clause
* **zstd-jni** — Copyright (c) Luben Karavelov — BSD 2-Clause
* **XZ for Java** — 0BSD

### Bouncy Castle Licence

* **Bouncy Castle** (`bcprov-jdk18on`) — Copyright (c) The Legion of the Bouncy
  Castle Inc. The Bouncy Castle Licence is an adaptation of the MIT licence;
  see <https://www.bouncycastle.org/licence.html>.

## A note on `classdata.tpk`

`assets/ondevice/bundle-surgery/classdata.tpk` is a type-package database that
AssetsTools.NET uses to deserialise Unity asset bundles whose type trees have
been stripped, as Silksong's are. It describes Unity's **serialisation layouts**
— class identifiers, field names and their types — and contains no Unity code,
no Unity assemblies and no game data. It is redistributed from the
AssetsTools.NET ecosystem under the MIT licence.

It is included because parsing a type-tree-stripped bundle is impossible
without it, and it is the same kind of artefact as a file-format description.
