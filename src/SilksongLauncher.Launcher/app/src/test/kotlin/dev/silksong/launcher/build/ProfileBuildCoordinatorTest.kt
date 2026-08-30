package dev.silksong.launcher.build

import dev.silksong.launcher.profiles.GameProfiles
import dev.silksong.launcher.profiles.ProfilePaths
import java.io.File
import java.util.concurrent.CancellationException
import kotlinx.coroutines.runBlocking
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

class ProfileBuildCoordinatorTest {
    private lateinit var root: File
    private lateinit var paths: ProfilePaths
    private lateinit var publisher: GenerationPublisher

    @Before
    fun setUp() {
        root = File("build/test-profile-build-coordinator").absoluteFile
        root.deleteRecursively()
        root.mkdirs()
        paths = ProfilePaths(root, GameProfiles.require("hollow-knight"))
        publisher = GenerationPublisher(paths)
    }

    @After
    fun tearDown() {
        root.deleteRecursively()
    }

    @Test
    fun `all stages run in order and publish only after verification`() = runBlocking {
        val seen = mutableListOf<BuildStage>()
        val coordinator = ProfileBuildCoordinator(publisher)

        val installed = coordinator.run(request("job-1", "gen-1")) { stage, workspace ->
            seen += stage
            if (stage == BuildStage.ValidateSource) {
                File(workspace.packageDir, "payload.bin").apply {
                    parentFile.mkdirs()
                    writeText("complete")
                }
            }
        }

        assertEquals(BuildStage.executionOrder, seen)
        assertEquals("gen-1", installed.id)
        assertEquals("gen-1", paths.currentPointer.readText())
        assertTrue(File(installed.root, "pkg/payload.bin").isFile)
    }

    @Test
    fun `stage failure discards only its job and retains current`() = runBlocking {
        val coordinator = ProfileBuildCoordinator(publisher)
        coordinator.run(request("job-1", "gen-1")) { stage, workspace ->
            if (stage == BuildStage.ValidateSource) {
                File(workspace.packageDir, "payload.bin").apply {
                    parentFile.mkdirs()
                    writeText("first")
                }
            }
        }
        publisher.begin("neighbor", "neighbor-gen")

        assertThrows(IllegalStateException::class.java) {
            runBlocking {
                coordinator.run(request("job-2", "gen-2")) { stage, workspace ->
                    if (stage == BuildStage.ValidateSource) {
                        File(workspace.packageDir, "payload.bin").apply {
                            parentFile.mkdirs()
                            writeText("second")
                        }
                    }
                    if (stage == BuildStage.CompileNative) error("injected failure")
                }
            }
        }

        assertEquals("gen-1", paths.currentPointer.readText())
        assertFalse(File(paths.staging, "job-2").exists())
        assertTrue(File(paths.staging, "neighbor").isDirectory)
    }

    @Test
    fun `cooperative cancellation discards staging without changing current`() {
        val coordinator = ProfileBuildCoordinator(publisher)

        assertThrows(CancellationException::class.java) {
            runBlocking {
                coordinator.run(request("job-1", "gen-1")) { stage, workspace ->
                    if (stage == BuildStage.ValidateSource) {
                        File(workspace.packageDir, "payload.bin").apply {
                            parentFile.mkdirs()
                            writeText("partial")
                        }
                    }
                    if (stage == BuildStage.CompilePatches) throw CancellationException("stop")
                }
            }
        }

        assertFalse(File(paths.staging, "job-1").exists())
        assertFalse(paths.currentPointer.exists())
    }

    private fun request(jobId: String, generationId: String) = ProfileBuildRequest(
        jobId = jobId,
        generationId = generationId,
        metadata = GenerationMetadata(
            sourceManifestSha256 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            toolchainId = "unity-test",
            patchManifestSha256 = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
        ),
    )
}
