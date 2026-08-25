// SilksongLauncher.Launcher.app — Android library module producing
// silksong-launcher.aar, which tools/depot-to-apk/build.sh unpacks into
// the APK it assembles (classes, JNI libraries and assets alike).

import java.security.MessageDigest

plugins {
    id("com.android.library")
    id("org.jetbrains.kotlin.android")
}

android {
    namespace = "dev.silksong.launcher"
    compileSdk = 36

    // Pinned to the NDK Unity's Android player ships, which is what
    // tools/depot-to-apk builds against. Left unset, AGP picks its own
    // default and then refuses to run when ndk.dir disagrees with it.
    ndkVersion = "27.2.12479018"

    defaultConfig {
        // Must match the Silksong APK's minSdk so AGP doesn't reject
        // the merged manifest.
        minSdk = 26

        // arm64 only, matching the rest of the APK. Building the other ABIs
        // would double the work to produce something no Silksong device runs.
        ndk {
            abiFilters += "arm64-v8a"
        }
    }

    // monohost, the .NET host. See src/main/cpp/monohost.c for why the
    // runtime is Microsoft's android-arm64 build rather than a Linux one.
    externalNativeBuild {
        cmake {
            path = file("src/main/cpp/CMakeLists.txt")
            version = "3.22.1"
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    kotlinOptions {
        jvmTarget = "17"
    }

    // Don't ship a separate R class in the AAR (Unity's manifest merger
    // re-namespaces resources via androidx anyway).
    buildFeatures {
        buildConfig = false
    }

    // The .NET runtime staged by fetchMonoRuntime. Its shared libraries join
    // monohost in lib/arm64-v8a/, and the class library rides in assets --
    // build.sh copies both out of the AAR into the APK it assembles.
    sourceSets {
        getByName("main") {
            jniLibs.srcDir(layout.buildDirectory.dir("mono/jniLibs"))
            assets.srcDir(layout.buildDirectory.dir("mono/assets"))
        }
    }
}

dependencies {
    // Apache Commons Compress: TarArchiveInputStream we wrap around
    // the decompressed LZ4 stream during bundle extraction.
    implementation("org.apache.commons:commons-compress:1.27.1")


    // Coroutines for background extraction with progress flow + the
    // Steam auth/cloud-sync async pipelines.
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.10.1")

    // ─── Phase 1: JavaSteam (Java port of SteamKit2) ─────────────────────────
    //
    // QR auth, cloud-save list/download/upload, and the depot download — all
    // over Steam's unified-message protocol on a WebSocket connection.
    // JavaSteam is implemented in Kotlin internally; pulls in Kotlin runtime
    // 2.1, kotlinx-coroutines, ktor (its WebSocket transport), protobuf-java,
    // gson, slf4j-api, etc. — ~30 transitive JARs, ~40 MB on disk.
    //
    implementation("in.dragonbra:javasteam:1.8.0")

    // The depot downloader is its own artifact as of 1.8.0, and is the reason
    // for being on that version. Steam now serves depot chunks compressed with
    // zstd; the older in-tree ContentDownloader knew only LZMA and pkzip and
    // handed zstd data to the zip decoder, which failed with "Did not find any
    // zip entries in the given stream" a few megabytes into a download.
    implementation("in.dragonbra:javasteam-depotdownloader:1.8.0")
    // BouncyCastle: required by JavaSteam's AES handshake. The
    // bcprov-jdk18on variant works on Android API 26+.
    implementation("org.bouncycastle:bcprov-jdk18on:1.78.1")
    // ZSTD + XZ: needed by JavaSteam's depot content decompression
    // (transitive — declare explicitly so collectRuntimeDeps grabs them).
    implementation("com.github.luben:zstd-jni:1.5.7-3@aar")
    implementation("org.tukaani:xz:1.9")

    // ZXing: pure-Java QR code encoder. Used by LoginActivity to render
    // the challenge URL JavaSteam gives us into a Bitmap.
    implementation("com.google.zxing:core:3.5.3")

    // protobuf-java + okhttp are transitive deps of JavaSteam (its
    // public API exposes ProtocolMessageEnum/GeneratedMessage builder
    // types, and its WebSocket transport uses ktor which bundles
    // okhttp on the JVM). We need both on the COMPILE classpath of
    // any module that references them directly:
    //   - protobuf-java: building cloud-service request protos.
    //   - okhttp: HTTP fetch of cloud file download URLs.
    // compileOnly avoids double-shipping the jar — the runtime copy
    // already comes via JavaSteam → collectRuntimeDeps.
    compileOnly("com.google.protobuf:protobuf-java:4.29.2")
    compileOnly("com.squareup.okhttp3:okhttp:5.0.0-alpha.14")
}

// ── collectRuntimeDeps task ─────────────────────────────────────────────
//
// Neither Unity's APK build nor tools/depot-to-apk consumes an AAR's POM
// -- depot-to-apk assembles the APK by hand with no Gradle at all -- so
// the dependencies declared above are invisible unless they are
// materialised somewhere the packaging step can find them. build.sh runs
// this task and dexes every JAR it produces.
//
// AARs have to be unpacked rather than copied. zstd-jni ships as one, and
// it is not optional: JavaSteam decompresses zstd depot chunks through it
// and declares it compileOnly, leaving the application to say where the
// native code comes from. An earlier version of this task kept only files
// ending in .jar, which dropped it silently -- the build succeeded and the
// download then failed on the first zstd chunk.
tasks.register("collectRuntimeDeps") {
    description = "Materialises the release runtime classpath into build/outputs/runtime-deps/"
    group = "build"
    dependsOn("assembleRelease")

    val outDir = layout.buildDirectory.dir("outputs/runtime-deps")

    doLast {
        val out = outDir.get().asFile
        delete(out)
        val jni = File(out, "jni")
        out.mkdirs()
        for (f in configurations.getByName("releaseRuntimeClasspath").filter { it.isFile }) {
            when {
                f.name.endsWith(".jar") -> f.copyTo(File(out, f.name), overwrite = true)
                f.name.endsWith(".aar") -> {
                    val base = f.name.removeSuffix(".aar")
                    copy {
                        from(zipTree(f)) {
                            include("classes.jar")
                            rename { "$base.jar" }
                        }
                        into(out)
                    }
                    // The native side, in the layout the APK wants.
                    copy {
                        from(zipTree(f)) { include("jni/**") }
                        into(jni)
                        eachFile { path = path.removePrefix("jni/") }
                        includeEmptyDirs = false
                    }
                }
            }
        }
        val abis = jni.listFiles()?.joinToString { it.name } ?: "none"
        logger.lifecycle("runtime deps: ${out.listFiles()?.count { it.extension == "jar" }} jar(s), jni: $abis")

        // The .NET runtime's own Java classes, staged by fetchMonoRuntime.
        // Copied in here rather than there because this task empties the
        // directory before filling it, and it runs second.
        copy {
            from(layout.buildDirectory.dir("mono/libs")) { include("*.jar") }
            into(out)
        }
    }
}

// The on-device native build is driven by tools/ondevice-il2cpp/build-il2cpp.sh,
// which is also runnable by hand from a terminal on the device. It is copied
// into assets at build time rather than kept in two places: the flags in it
// were recovered from Unity's own build graph and are not the kind of thing
// that should be allowed to drift between a shell copy and an app copy.
val stageBuildScript by tasks.registering(Copy::class) {
    from(rootProject.file("../../tools/ondevice-il2cpp/build-il2cpp.sh"))
    into(layout.projectDirectory.dir("src/main/assets/ondevice"))
}
tasks.named("preBuild") { dependsOn(stageBuildScript) }

// ── the .NET runtime ────────────────────────────────────────────────────
//
// Microsoft's Mono build of .NET for android-arm64: the runtime MAUI ships,
// linked against bionic. It replaces a bundle that used to be assembled at
// run time from Microsoft's linux-arm64 tarball and Debian's own glibc --
// which cannot work on a current Android at all. Every app runs under a
// seccomp filter generated from the syscalls bionic itself makes, and it
// answers anything else with SIGSYS rather than ENOSYS, so glibc 2.35+ dies
// registering rseq before reaching main() with nothing printed. See
// src/main/cpp/monohost.c.
//
// Fetched here rather than committed: 25 MB of binaries do not belong in a
// git history. Pinned by version and checked against its hash, so what a
// build produces does not depend on what nuget.org served that day.
//
// MIT licensed; LICENSE.TXT and THIRD-PARTY-NOTICES.TXT ship beside it.
//
// The CDN host is deliberate. api.nuget.org is the metadata service and is
// not always reachable; globalcdn.nuget.org is where the packages themselves
// live and is what the client downloads from either way.
val monoVersion = "9.0.16"
val monoSha256 = "60fb0433157a54c580ca598743110c61a81feea19a29b76f611ed1448acc1100"
val monoStage = layout.buildDirectory.dir("mono")

val fetchMonoRuntime by tasks.registering {
    description = "Downloads the android-arm64 .NET runtime and stages it for packaging"
    group = "build"

    val pkg = "microsoft.netcore.app.runtime.mono.android-arm64"
    val nupkg = layout.buildDirectory.file("mono/$pkg.$monoVersion.nupkg")
    val stage = monoStage

    inputs.property("version", monoVersion)
    inputs.property("sha256", monoSha256)
    outputs.dir(stage)

    doLast {
        val file = nupkg.get().asFile
        file.parentFile.mkdirs()

        // Re-download only when the file that is there is not the file that
        // was asked for. A truncated download is the interesting case: it
        // unpacks far enough to look fine and fails much later.
        val have = file.isFile && sha256Of(file) == monoSha256
        if (!have) {
            val url = "https://globalcdn.nuget.org/packages/$pkg.$monoVersion.nupkg"
            logger.lifecycle("mono runtime: downloading $monoVersion")
            uri(url).toURL().openStream().use { input ->
                file.outputStream().use { input.copyTo(it) }
            }
            val got = sha256Of(file)
            if (got != monoSha256) {
                file.delete()
                throw GradleException("mono runtime $monoVersion hash mismatch: expected $monoSha256, got $got")
            }
        }

        val out = stage.get().asFile
        delete(File(out, "jniLibs"))
        delete(File(out, "assets"))

        val native = "runtimes/android-arm64/native"

        // The runtime's shared libraries, which have to be somewhere the
        // installer will extract to an executable directory -- lib/<abi>/ is
        // the only such place. monohost is built into the same directory by
        // CMake and lands beside them.
        copy {
            from(zipTree(file)) { include("$native/*.so") }
            into(File(out, "jniLibs/arm64-v8a"))
            eachFile { path = name }
            includeEmptyDirs = false
        }

        // The class library is data: it is read and JIT'd, never executed as
        // a file, so it goes in assets and is staged to filesDir on first
        // run. System.Private.CoreLib sits with the native files upstream
        // but belongs with the rest of the assemblies here.
        copy {
            from(zipTree(file)) {
                include("runtimes/android-arm64/lib/net9.0/*.dll")
                include("$native/System.Private.CoreLib.dll")
                include("LICENSE.TXT")
                include("THIRD-PARTY-NOTICES.TXT")
            }
            into(File(out, "assets/mono-bcl"))
            eachFile { path = name }
            includeEmptyDirs = false
        }

        // The Java half of the cryptography shim.
        //
        // .NET does not implement cryptography on Android; it calls Java's,
        // and libSystem.Security.Cryptography.Native.Android.so is the JNI
        // side of that. Its JNI_OnLoad resolves three classes in
        // net.dot.android.crypto and calls abort() if they are not on the
        // class path -- "GetClassGRef: class net/dot/android/crypto/
        // DotnetProxyTrustManager was not found", which takes the process
        // with it. The classes ship in the runtime pack beside the library
        // and have to be dexed into the APK. build.sh dexes every jar in
        // runtime-deps, so collectRuntimeDeps puts it there.
        copy {
            from(zipTree(file)) { include("$native/*.jar") }
            into(File(out, "libs"))
            eachFile { path = name }
            includeEmptyDirs = false
        }

        val libs = File(out, "jniLibs/arm64-v8a").listFiles()?.size ?: 0
        val bcl = File(out, "assets/mono-bcl").listFiles()?.count { it.extension == "dll" } ?: 0
        val jars = File(out, "libs").listFiles()?.count { it.extension == "jar" } ?: 0
        logger.lifecycle("mono runtime $monoVersion: $libs native libs, $bcl assemblies, $jars jar(s)")
    }
}
tasks.named("preBuild") { dependsOn(fetchMonoRuntime) }
// The crypto jar it stages is dexed by way of runtime-deps, so that task
// cannot run before this one has produced it.
tasks.named("collectRuntimeDeps") { dependsOn(fetchMonoRuntime) }

fun sha256Of(f: File): String =
    MessageDigest.getInstance("SHA-256").digest(f.readBytes())
        .joinToString("") { "%02x".format(it) }

// bundle-surgery, the tool that retargets Unity serialized files and bundles.
// It is a small framework-dependent .NET app -- around 550 KB with its type
// database -- and the device already has a .NET to run it on, so the same
// build output that a PC uses is carried in the APK and run there. Nothing in
// it is Unity's or the game's; it reads their formats, it does not contain
// them.
val stageBundleSurgery by tasks.registering(Copy::class) {
    val built = rootProject.file("../../tools/bundle-surgery/bin/Release/net8.0")
    from(built) {
        include(
            "BundleSurgery.dll", "BundleSurgery.runtimeconfig.json", "BundleSurgery.deps.json",
            "AssetsTools.NET.dll", "AssetRipper.Primitives.dll", "classdata.tpk",
        )
    }
    into(layout.projectDirectory.dir("src/main/assets/ondevice/bundle-surgery"))
    doFirst {
        require(File(built, "BundleSurgery.dll").isFile) {
            "bundle-surgery is not built. Run: dotnet build -c Release tools/bundle-surgery"
        }
    }
}
tasks.named("preBuild") { dependsOn(stageBundleSurgery) }

// The patches: our own game code, shipped as SOURCE and compiled on the
// device against the user's own depot.
//
// Not as a prebuilt DLL, which was the other option. Building it here would
// mean it could only reference stock Unity -- the repo has no copy of the
// game and must not -- so every touch of a game type would have to go through
// reflection. On the device the depot is right there, so the same code can
// reference UIManager or NestedFadeGroup as ordinary typed C#, and a wrong
// signature is a compile error rather than a null at runtime.
//
// It also means nothing game-derived is ever built into an artifact we
// publish: what ships is .cs text that names types, never a binary linked
// against them.
//
// The staging directory is emptied first. A Copy task only ever adds, so a
// source that is renamed or deleted upstream would linger here and still be
// compiled on the device -- and because the device compiles the whole
// directory, a leftover copy of a renamed file is a duplicate type and a
// build failure with a confusing message ("already contains a definition
// for ..." naming a file that looks perfectly fine in the repo).
val stagePatches by tasks.registering(Sync::class) {
    from(rootProject.file("../../tools/silksong-patches/src")) { into("src") }
    from(rootProject.file("../../tools/silksong-patches/entrypoints.json"))
    into(layout.projectDirectory.dir("src/main/assets/ondevice/patches"))
}
tasks.named("preBuild") { dependsOn(stagePatches) }
