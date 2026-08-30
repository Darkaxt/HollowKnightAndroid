package dev.silksong.launcher.build

import dev.silksong.launcher.UnityDex
import dev.silksong.launcher.UnityFetcher
import dev.silksong.launcher.profiles.GameProfiles
import java.io.File
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test

class UnityToolchainRegistryTest {
    @Test
    fun `registered profiles resolve exact distinct Unity descriptors`() {
        val silksong = UnityToolchainRegistry.resolve(GameProfiles.require("silksong"))
        val hollowKnight = UnityToolchainRegistry.resolve(GameProfiles.require("hollow-knight"))

        assertEquals("6000.0.50f1", silksong.unityVersion)
        assertEquals("6000.0.61f1", hollowKnight.unityVersion)
        assertNotEquals(silksong, hollowKnight)
        assertNotEquals(silksong.contentHash, hollowKnight.contentHash)
        assertEquals(
            "076a2f975c9b807f5b9d9c560ac3cbb8202653c71a721ae5ad6e61d9b46e0d9b",
            silksong.editorSha256,
        )
        assertEquals(
            "839de4ae756852b9f2ae9e193082b6d5d790bece180d616a6f40df3acf841e7d",
            silksong.androidModuleSha256,
        )
        assertEquals(
            "cf6182370a5c8911bc750122ee033d01d43e8bcf9348a930ab97f40831eef171",
            hollowKnight.editorSha256,
        )
        assertEquals(
            "af590a00ab049870c90b164ca86c1f245c01f2c22511f2a51148c566fa22afd5",
            hollowKnight.androidModuleSha256,
        )
    }

    @Test
    fun `unknown Unity versions and inexact profiles fail closed`() {
        assertThrows(IllegalArgumentException::class.java) {
            UnityToolchainRegistry.resolve("6000.0.999f1")
        }
        assertThrows(IllegalArgumentException::class.java) {
            UnityToolchainRegistry.resolve(
                GameProfiles.require("hollow-knight").copy(unityVersion = "6000.0.50f1"),
            )
        }
    }

    @Test
    fun `toolchain roots are descriptor content addressed`() {
        val filesDir = File("build/test-unity-toolchains/roots").absoluteFile
        filesDir.deleteRecursively()
        val silksong = UnityToolchainRegistry.resolve("6000.0.50f1")
        val hollowKnight = UnityToolchainRegistry.resolve("6000.0.61f1")

        assertEquals(
            File(filesDir, "toolchains/${silksong.contentHash}"),
            UnityToolchainRegistry.rootFor(filesDir, silksong),
        )
        assertEquals(
            File(filesDir, "toolchains/${hollowKnight.contentHash}"),
            UnityToolchainRegistry.rootFor(filesDir, hollowKnight),
        )
        assertThrows(IllegalArgumentException::class.java) {
            UnityFetcher.isPresent(
                hollowKnight,
                UnityToolchainRegistry.rootFor(filesDir, silksong),
            )
        }
    }

    @Test
    fun `wrong staged hash is evicted without touching another toolchain`() {
        val filesDir = File("build/test-unity-toolchains/eviction").absoluteFile
        filesDir.deleteRecursively()
        val descriptor = UnityToolchainRegistry.resolve("6000.0.61f1")
        val staging = File(UnityToolchainRegistry.rootFor(filesDir, descriptor), "staging")
        val wrong = File(staging, "android-support.pkg").apply {
            requireNotNull(parentFile).mkdirs()
            writeText("wrong")
        }
        val other = File(filesDir, "toolchains/another/staging/android-support.pkg").apply {
            requireNotNull(parentFile).mkdirs()
            writeText("preserve")
        }

        assertFalse(
            UnityToolchainRegistry.verifyStagedComponent(
                staging,
                "android-support.pkg",
                descriptor.androidModuleSha256,
            ),
        )
        assertFalse(wrong.exists())
        assertEquals("preserve", other.readText())
    }

    @Test
    fun `staged component verification rejects path escape`() {
        val staging = File("build/test-unity-toolchains/escape/staging").absoluteFile

        assertThrows(IllegalArgumentException::class.java) {
            UnityToolchainRegistry.verifyStagedComponent(
                staging,
                "../outside.pkg",
                "0".repeat(64),
            )
        }
    }

    @Test
    fun `Unity dex output is keyed by classes jar content rather than timestamp`() {
        val filesDir = File("build/test-unity-toolchains/dex").absoluteFile
        filesDir.deleteRecursively()
        val first = File(filesDir, "first/classes.jar").apply {
            requireNotNull(parentFile).mkdirs()
            writeText("first")
        }
        val same = File(filesDir, "same/classes.jar").apply {
            requireNotNull(parentFile).mkdirs()
            writeText("first")
            setLastModified(first.lastModified() + 10_000L)
        }
        val second = File(filesDir, "second/classes.jar").apply {
            requireNotNull(parentFile).mkdirs()
            writeText("second")
        }

        assertEquals(UnityDex.outputDir(filesDir, first), UnityDex.outputDir(filesDir, same))
        assertNotEquals(UnityDex.outputDir(filesDir, first), UnityDex.outputDir(filesDir, second))
    }
}
