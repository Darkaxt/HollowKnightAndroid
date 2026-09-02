package dev.silksong.launcher.build

import dev.silksong.launcher.Mods
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
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner

@RunWith(RobolectricTestRunner::class)
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
    fun `post-conversion failure retains published mod status gates and rebuild prompt`() = runBlocking {
        val coordinator = ProfileBuildCoordinator(publisher)
        val mods = File(root, "shared-mods").apply { mkdirs() }
        val plugin = File(mods, "Example.dll").apply { writeText("one") }
        val state = File(root, "hollow-knight-mod-state")
        val firstCandidate = File(root, "candidate-one").apply { mkdirs() }
        Mods.ensure(mods, state)
        Mods.setEnabled(state, "Example.dll", enabled = false)

        val first = coordinator.run(request("job-1", "gen-1")) { stage, workspace ->
            when (stage) {
                BuildStage.ValidateSource -> File(workspace.packageDir, "payload.bin").apply {
                    parentFile.mkdirs()
                    writeText("first")
                }
                BuildStage.ConvertIl2Cpp -> {
                    val input = Mods.snapshotForBuild(mods, firstCandidate)
                    writeReport(firstCandidate, "Published.Plugin")
                    Mods.recordCandidate(input, firstCandidate)
                }
                BuildStage.Verify -> Mods.stageForGeneration(firstCandidate, workspace.root)
                else -> Unit
            }
        }
        Mods.writeCurrentGates(state, publisher)
        assertEquals("Published.Plugin", Mods.gatesFile(state).readText().trim())
        assertTrue(first.files.keys.containsAll(
            setOf("mods/mods.stamp", "mods/mods.built", "mods/mods.report.json"),
        ))

        plugin.writeText("two")
        val failedCandidate = File(root, "candidate-two").apply { mkdirs() }
        assertThrows(IllegalStateException::class.java) {
            runBlocking {
                coordinator.run(request("job-2", "gen-2")) { stage, workspace ->
                    when (stage) {
                        BuildStage.ValidateSource -> File(workspace.packageDir, "payload.bin").apply {
                            parentFile.mkdirs()
                            writeText("second")
                        }
                        BuildStage.ConvertIl2Cpp -> {
                            val input = Mods.snapshotForBuild(mods, failedCandidate)
                            writeReport(failedCandidate, "Unpublished.Plugin")
                            Mods.recordCandidate(input, failedCandidate)
                        }
                        BuildStage.CompileNative -> error("injected post-conversion failure")
                        else -> Unit
                    }
                }
            }
        }

        val current = requireNotNull(publisher.current())
        val currentMetadata = Mods.publishedMetadataRoot(current)
        Mods.writeCurrentGates(state, publisher)
        assertEquals("gen-1", current.id)
        assertEquals("Published.Plugin", Mods.lastReport(currentMetadata).single().assembly)
        assertEquals("Published.Plugin", Mods.gatesFile(state).readText().trim())
        assertTrue(Mods.isStale(mods, currentMetadata))

        writeReport(currentMetadata, "Tampered.Plugin")
        assertThrows(IllegalStateException::class.java) {
            Mods.writeCurrentGates(state, publisher)
        }
        assertEquals("Published.Plugin", Mods.gatesFile(state).readText().trim())
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

    private fun writeReport(root: File, assembly: String) {
        Mods.reportFile(root).writeText(
            """{"plugins":[{"File":"Example.dll","Assembly":"$assembly","Guid":"example","Name":"Example","Version":"1.0","Status":"Ok","Patched":1,"Issues":[]}]}""",
        )
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
