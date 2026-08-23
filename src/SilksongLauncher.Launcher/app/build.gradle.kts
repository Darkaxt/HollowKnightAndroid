// SilksongLauncher.Launcher.app — Android library module producing
// silksong-launcher.aar, which tools/depot-to-apk/build.sh unpacks into
// the APK it assembles (classes, JNI libraries and assets alike).

plugins {
    id("com.android.library")
    id("org.jetbrains.kotlin.android")
}

android {
    namespace = "dev.silksong.launcher"
    compileSdk = 36

    defaultConfig {
        // Must match the Silksong APK's minSdk so AGP doesn't reject
        // the merged manifest.
        minSdk = 26
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

// mod-weaver: the build-time chainloader. Same arrangement as bundle-surgery
// -- a small framework-dependent .NET app built on a PC, carried in the APK,
// run on the device by the .NET that is already there.
val stageModWeaver by tasks.registering(Copy::class) {
    val built = rootProject.file("../../tools/mod-weaver/bin/Release/net8.0")
    from(built) {
        include(
            "ModWeaver.dll", "ModWeaver.runtimeconfig.json", "ModWeaver.deps.json",
            "Mono.Cecil.dll", "Mono.Cecil.Rocks.dll", "Mono.Cecil.Mdb.dll", "Mono.Cecil.Pdb.dll",
        )
    }
    into(layout.projectDirectory.dir("src/main/assets/ondevice/mod-weaver"))
    doFirst {
        require(File(built, "ModWeaver.dll").isFile) {
            "mod-weaver is not built. Run: dotnet build -c Release tools/mod-weaver"
        }
    }
}
tasks.named("preBuild") { dependsOn(stageModWeaver) }

// The BepInEx shims, shipped as source and compiled on the device for the same
// reason the patches are: a plugin and the game have to agree about what
// UnityEngine.MonoBehaviour is, and only the depot can settle that.
val stageBepInExShim by tasks.registering(Sync::class) {
    from(rootProject.file("../../tools/bepinex-shim/src"))
    into(layout.projectDirectory.dir("src/main/assets/ondevice/bepinex"))
}
tasks.named("preBuild") { dependsOn(stageBepInExShim) }
