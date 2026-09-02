package dev.silksong.launcher

import android.content.Context
import android.net.Uri
import android.os.Environment
import android.provider.DocumentsContract
import androidx.test.core.app.ApplicationProvider
import dev.silksong.launcher.build.GenerationMetadata
import dev.silksong.launcher.build.GenerationPublisher
import dev.silksong.launcher.profiles.GameProfiles
import dev.silksong.launcher.profiles.ProfilePaths
import kotlinx.coroutines.CancellationException
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.io.File
import java.io.IOException
import java.io.InputStream
import java.text.Normalizer
import java.util.UUID

private const val EXTERNAL_STORAGE_AUTHORITY = "com.android.externalstorage.documents"

@RunWith(RobolectricTestRunner::class)
class ModsTest {

    @get:Rule
    val temp = TemporaryFolder()

    @Test
    fun `discovery is recursive and excludes legacy config state`() {
        val mods = temp.newFolder("mods")
        File(mods, "Top.dll").writeText("top")
        File(mods, "pack/Nested.DLL").apply {
            requireNotNull(parentFile).mkdirs()
            writeText("nested")
        }
        File(mods, "pack/.dualsouls-import-v1.properties").writeText("identity metadata")
        File(mods, "config/NotAPlugin.dll").apply {
            requireNotNull(parentFile).mkdirs()
            writeText("config")
        }
        var legacyConfig = File(mods, "config")
        repeat(70) { legacyConfig = File(legacyConfig, "nested$it") }
        File(legacyConfig, "Ignored.dll").apply {
            requireNotNull(parentFile).mkdirs()
            writeText("ignored")
        }

        assertEquals(
            setOf("Top.dll", "pack${File.separator}Nested.DLL"),
            Mods.all(mods).map { Mods.relativePath(mods, it) }.toSet(),
        )
    }

    @Test
    fun `one shared mod library keeps independent published profile records`() {
        val mods = temp.newFolder("shared-mods")
        val plugin = File(mods, "Example.dll").apply { writeText("one") }
        val hollowKnightCandidate = temp.newFolder("hollow-knight-candidate")
        val hollowKnightGeneration = temp.newFolder("hollow-knight-generation")
        val silksongCandidate = temp.newFolder("silksong-candidate")
        val silksongGeneration = temp.newFolder("silksong-generation")

        val hollowKnightInput = Mods.snapshotForBuild(mods, hollowKnightCandidate)
        Mods.reportFile(hollowKnightCandidate).writeText("{\"plugins\":[]}")
        Mods.recordCandidate(hollowKnightInput, hollowKnightCandidate)
        Mods.stageForGeneration(hollowKnightCandidate, hollowKnightGeneration)
        val hollowKnightPublished = Mods.generationMetadataRoot(hollowKnightGeneration)
        val silksongPublished = Mods.generationMetadataRoot(silksongGeneration)

        assertFalse(Mods.isStale(mods, hollowKnightPublished))
        assertEquals(true, Mods.isBuilt(mods, hollowKnightPublished, plugin))
        assertEquals(null, Mods.isBuilt(mods, silksongPublished, plugin))
        assertTrue(Mods.isStale(mods, silksongPublished))
        plugin.writeText("two")
        assertTrue(Mods.isStale(mods, hollowKnightPublished))
        assertEquals(false, Mods.isBuilt(mods, hollowKnightPublished, plugin))

        val silksongInput = Mods.snapshotForBuild(mods, silksongCandidate)
        Mods.reportFile(silksongCandidate).writeText("{\"plugins\":[]}")
        Mods.recordCandidate(silksongInput, silksongCandidate)
        Mods.stageForGeneration(silksongCandidate, silksongGeneration)
        assertEquals(true, Mods.isBuilt(mods, Mods.generationMetadataRoot(silksongGeneration), plugin))
        assertEquals(false, Mods.isBuilt(mods, hollowKnightPublished, plugin))
    }

    @Test
    fun `mod import never treats dot names as storage paths`() {
        assertEquals("mod", ModImport.safeFolderName("."))
        assertEquals("mod", ModImport.safeFolderName(".."))
        assertEquals("Example_Mod", ModImport.safeFolderName("Example/Mod"))
    }

    @Test
    fun `mod import rejects source trees containing its destination`() {
        val storage = temp.newFolder("import-storage")
        val mods = File(storage, "mods").apply { mkdirs() }

        val failure = assertThrows(java.io.IOException::class.java) {
            ModImport.requireSourceOutsideDestination(storage, mods)
        }
        assertThrows(java.io.IOException::class.java) {
            ModImport.requireSourceOutsideDestination(requireNotNull(storage.toPath().root).toFile(), mods)
        }

        assertTrue(failure.message.orEmpty().contains("inside"))
        ModImport.requireSourceOutsideDestination(temp.newFolder("downloaded-mod"), mods)
    }

    @Test
    fun `mod replacement promotes complete staging and removes backup`() {
        val storage = temp.newFolder("successful-replacement")
        val mods = File(storage, "mods").apply { mkdirs() }
        val target = File(mods, "Example").apply { mkdirs() }
        File(target, "plugin.dll").writeText("old")
        val plan = ModImport.plan("Example", "test|successful", mods)
        val transaction = ModImport.beginTransaction(mods, plan)
        val staging = transaction.staging.apply { mkdirs() }
        File(staging, "plugin.dll").writeText("new")
        ModImport.writeIdentity(staging, plan)

        ModImport.replaceStaged(transaction)

        assertEquals("new", File(target, "plugin.dll").readText())
        assertFalse(transaction.root.exists())
    }

    @Test
    fun `mod replacement leaves prior target when backup move fails`() {
        val storage = temp.newFolder("failed-backup")
        val mods = File(storage, "mods").apply { mkdirs() }
        val target = File(mods, "Example").apply { mkdirs() }
        File(target, "plugin.dll").writeText("old")
        val plan = ModImport.plan("Example", "test|failed-backup", mods)
        val transaction = ModImport.beginTransaction(mods, plan)
        File(transaction.staging, "plugin.dll").apply {
            requireNotNull(parentFile).mkdirs()
            writeText("new")
        }
        ModImport.writeIdentity(transaction.staging, plan)

        assertThrows(IOException::class.java) {
            ModImport.replaceStaged(transaction) { _, _ -> false }
        }

        assertEquals("old", File(target, "plugin.dll").readText())
        assertFalse(transaction.root.exists())
    }

    @Test
    fun `mod replacement rolls back exact target when promotion fails`() {
        val storage = temp.newFolder("transactional")
        val mods = File(storage, "mods").apply { mkdirs() }
        val target = File(mods, "Example").apply { mkdirs() }
        File(target, "plugin.dll").writeText("old")
        val plan = ModImport.plan("Example", "test|rollback", mods)
        val transaction = ModImport.beginTransaction(mods, plan)
        File(transaction.staging, "plugin.dll").apply {
            requireNotNull(parentFile).mkdirs()
            writeText("new")
        }
        ModImport.writeIdentity(transaction.staging, plan)
        var moves = 0

        val failure = assertThrows(IOException::class.java) {
            ModImport.replaceStaged(transaction) { source, destination ->
                moves++
                if (moves == 2) false else source.renameTo(destination)
            }
        }

        assertTrue(failure.message.orEmpty().contains("promote"))
        assertEquals("old", File(target, "plugin.dll").readText())
        assertFalse(transaction.root.exists())
    }

    @Test
    fun `failed new promotion preserves an unverifiable target for recovery`() {
        val storage = temp.newFolder("failed-new-promotion")
        val mods = File(storage, "mods").apply { mkdirs() }
        val plan = ModImport.plan("Example", "test|failed-new-promotion", mods)
        val transaction = ModImport.beginTransaction(mods, plan)
        File(transaction.staging, "plugin.dll").apply {
            requireNotNull(parentFile).mkdirs()
            writeText("new")
        }
        ModImport.writeIdentity(transaction.staging, plan)

        assertThrows(IOException::class.java) {
            ModImport.replaceStaged(transaction) { source, destination ->
                val moved = source.renameTo(destination)
                if (moved) File(destination, "plugin.dll").writeText("tampered")
                moved
            }
        }

        assertEquals("tampered", File(plan.target, "plugin.dll").readText())
        assertTrue(transaction.root.exists())
    }

    @Test
    fun `target created after absent prepare is rejected without deleting external data`() {
        val storage = temp.newFolder("absent-target-race")
        val mods = File(storage, "mods").apply { mkdirs() }
        val plan = ModImport.plan("Example", "test|absent-target-race", mods)
        val transaction = ModImport.beginTransaction(mods, plan)
        File(transaction.staging, "plugin.dll").apply {
            requireNotNull(parentFile).mkdirs()
            writeText("new")
        }
        ModImport.writeIdentity(transaction.staging, plan)
        File(plan.target, "external.dll").apply {
            requireNotNull(parentFile).mkdirs()
            writeText("external")
        }

        val failure = assertThrows(IOException::class.java) { ModImport.replaceStaged(transaction) }

        assertTrue(failure.message.orEmpty().contains("destination changed"))
        assertEquals("external", File(plan.target, "external.dll").readText())
        assertFalse(File(plan.target, "plugin.dll").exists())
        assertFalse(transaction.root.exists())
        assertPipelineUsableAfterRejectedImport(
            mods,
            temp.newFolder("absent-target-race-state"),
            "Example/external.dll",
        )
    }

    @Test
    fun `existing target changed after prepare is rejected without replacement`() {
        val storage = temp.newFolder("existing-target-race")
        val mods = File(storage, "mods").apply { mkdirs() }
        val target = File(mods, "Example").apply { mkdirs() }
        val prior = File(target, "plugin.dll").apply { writeText("old") }
        val plan = ModImport.plan("Example", "test|existing-target-race", mods)
        val transaction = ModImport.beginTransaction(mods, plan)
        File(transaction.staging, "plugin.dll").apply {
            requireNotNull(parentFile).mkdirs()
            writeText("new")
        }
        ModImport.writeIdentity(transaction.staging, plan)
        prior.writeText("external mutation")

        val failure = assertThrows(IOException::class.java) { ModImport.replaceStaged(transaction) }

        assertTrue(failure.message.orEmpty().contains("destination changed"))
        assertEquals("external mutation", prior.readText())
        assertFalse(transaction.backup.exists())
        assertFalse(transaction.root.exists())
        assertPipelineUsableAfterRejectedImport(
            mods,
            temp.newFolder("existing-target-race-state"),
            "Example/plugin.dll",
        )
    }

    @Test
    fun `cancelled copy cleans owned staging after an external target appears`() {
        val storage = temp.newFolder("cancelled-copy-target-race")
        val mods = File(storage, "mods").apply { mkdirs() }
        val source = externalImportSource("cancelled-copy")
        try {
            File(source, "plugin.dll").writeText("incoming")
            val (uri, plan) = directImportPlan(source, mods)
            var checks = 0

            assertThrows(CancellationException::class.java) {
                ModImport.copy(
                    ApplicationProvider.getApplicationContext<Context>(),
                    uri,
                    mods,
                    requested = plan,
                    checkCancelled = {
                        if (++checks == 2) {
                            File(plan.target, "external.dll").apply {
                                requireNotNull(parentFile).mkdirs()
                                writeText("external")
                            }
                            throw CancellationException("cancelled")
                        }
                    },
                )
            }

            assertEquals("external", File(plan.target, "external.dll").readText())
            assertFalse(File(plan.target, "plugin.dll").exists())
            assertTrue(ModImport.transactionRoot(mods).listFiles().orEmpty().isEmpty())
            assertPipelineUsableAfterRejectedImport(
                mods,
                temp.newFolder("cancelled-copy-target-race-state"),
                "${plan.targetName}/external.dll",
            )
        } finally {
            source.deleteRecursively()
        }
    }

    @Test
    fun `failed copy cleans owned staging after an external target change`() {
        val storage = temp.newFolder("failed-copy-target-race")
        val mods = File(storage, "mods").apply { mkdirs() }
        val source = externalImportSource("failed-copy")
        try {
            File(source, "plugin.dll").writeText("incoming")
            val (uri, initial) = directImportPlan(source, mods)
            val external = File(initial.target, "external.dll").apply {
                requireNotNull(parentFile).mkdirs()
                writeText("before")
            }
            ModImport.writeIdentity(initial.target, initial)
            val plan = directImportPlan(source, mods).second
            var checks = 0

            val failure = assertThrows(IOException::class.java) {
                ModImport.copy(
                    ApplicationProvider.getApplicationContext<Context>(),
                    uri,
                    mods,
                    requested = plan,
                    checkCancelled = {
                        if (++checks == 2) {
                            external.writeText("external mutation")
                            throw IOException("copy failed")
                        }
                    },
                )
            }

            assertEquals("copy failed", failure.message)
            assertEquals("external mutation", external.readText())
            assertTrue(ModImport.transactionRoot(mods).listFiles().orEmpty().isEmpty())
            assertPipelineUsableAfterRejectedImport(
                mods,
                temp.newFolder("failed-copy-target-race-state"),
                "${plan.targetName}/external.dll",
            )
        } finally {
            source.deleteRecursively()
        }
    }

    @Test
    fun `backup is verified immediately before promotion`() {
        val storage = temp.newFolder("backup-verification-race")
        val mods = File(storage, "mods").apply { mkdirs() }
        val target = File(mods, "Example").apply { mkdirs() }
        File(target, "plugin.dll").writeText("old")
        val plan = ModImport.plan("Example", "test|backup-verification-race", mods)
        val transaction = ModImport.beginTransaction(mods, plan)
        File(transaction.staging, "plugin.dll").apply {
            requireNotNull(parentFile).mkdirs()
            writeText("new")
        }
        ModImport.writeIdentity(transaction.staging, plan)
        var moves = 0

        assertThrows(IOException::class.java) {
            ModImport.replaceStaged(transaction) { source, destination ->
                moves++
                val moved = source.renameTo(destination)
                if (moves == 1 && moved) File(destination, "plugin.dll").writeText("external mutation")
                moved
            }
        }

        assertEquals(1, moves)
        assertEquals("external mutation", File(transaction.backup, "plugin.dll").readText())
        assertFalse(target.exists())
        assertTrue(transaction.root.exists())
    }

    @Test
    fun `external target appearing during promotion is preserved with prior backup`() {
        val storage = temp.newFolder("promotion-target-race")
        val mods = File(storage, "mods").apply { mkdirs() }
        val target = File(mods, "Example").apply { mkdirs() }
        File(target, "plugin.dll").writeText("old")
        val plan = ModImport.plan("Example", "test|promotion-target-race", mods)
        val transaction = ModImport.beginTransaction(mods, plan)
        File(transaction.staging, "plugin.dll").apply {
            requireNotNull(parentFile).mkdirs()
            writeText("new")
        }
        ModImport.writeIdentity(transaction.staging, plan)
        var moves = 0

        assertThrows(IOException::class.java) {
            ModImport.replaceStaged(transaction) { source, destination ->
                moves++
                if (moves == 1) {
                    source.renameTo(destination)
                } else {
                    File(destination, "external.dll").apply {
                        requireNotNull(parentFile).mkdirs()
                        writeText("external")
                    }
                    false
                }
            }
        }

        assertEquals("external", File(target, "external.dll").readText())
        assertEquals("old", File(transaction.backup, "plugin.dll").readText())
        assertTrue(transaction.root.exists())
    }

    @Test
    fun `mod replacement surfaces unverifiable restoration and preserves backup`() {
        val storage = temp.newFolder("failed-restore")
        val mods = File(storage, "mods").apply { mkdirs() }
        val target = File(mods, "Example").apply { mkdirs() }
        File(target, "plugin.dll").writeText("old")
        val plan = ModImport.plan("Example", "test|failed-restore", mods)
        val transaction = ModImport.beginTransaction(mods, plan)
        File(transaction.staging, "plugin.dll").apply {
            requireNotNull(parentFile).mkdirs()
            writeText("new")
        }
        ModImport.writeIdentity(transaction.staging, plan)
        var moves = 0

        val failure = assertThrows(IOException::class.java) {
            ModImport.replaceStaged(transaction) { source, destination ->
                moves++
                when (moves) {
                    1 -> source.renameTo(destination)
                    else -> false
                }
            }
        }

        assertTrue(failure.message.orEmpty().contains("restore"))
        assertFalse(target.exists())
        assertEquals("old", File(transaction.backup, "plugin.dll").readText())
        assertTrue(transaction.staging.exists())
    }

    @Test
    fun `stream budget counts missing and underreported declared sizes cumulatively`() {
        val budget = ModImport.StreamBudget(maxFiles = 4, maxBytes = 8)
        val first = ByteArrayOutputStream()
        ModImport.copyChecked(
            ByteArrayInputStream(byteArrayOf(1, 2, 3, 4, 5)),
            first,
            declaredSize = 0,
            budget = budget,
        )
        val second = ByteArrayOutputStream()

        assertThrows(IOException::class.java) {
            ModImport.copyChecked(
                ByteArrayInputStream(byteArrayOf(6, 7, 8, 9)),
                second,
                declaredSize = 1,
                budget = budget,
            )
        }

        assertEquals(5L, budget.bytes)
        assertEquals(5, first.size())
        assertEquals(0, second.size())
    }

    @Test
    fun `declared provider size rejects before reading the stream`() {
        val budget = ModImport.StreamBudget(maxFiles = 1, maxBytes = 8)
        var read = false
        val input = object : java.io.InputStream() {
            override fun read(): Int {
                read = true
                return 1
            }
        }

        assertThrows(IOException::class.java) {
            ModImport.copyChecked(input, ByteArrayOutputStream(), declaredSize = 9, budget = budget)
        }

        assertFalse(read)
        assertEquals(0L, budget.bytes)
    }

    @Test
    fun `stream budget aborts before writing an overflowing mid-stream chunk`() {
        val firstChunk = 1 shl 16
        val budget = ModImport.StreamBudget(maxFiles = 1, maxBytes = firstChunk.toLong() + 1)
        val output = ByteArrayOutputStream()

        assertThrows(IOException::class.java) {
            ModImport.copyChecked(
                ByteArrayInputStream(ByteArray(firstChunk + 2) { 1 }),
                output,
                declaredSize = 1,
                budget = budget,
            )
        }

        assertEquals(firstChunk.toLong(), budget.bytes)
        assertEquals(firstChunk, output.size())
    }

    @Test
    fun `stream copy observes cancellation between chunks`() {
        val firstChunk = 1 shl 16
        val output = ByteArrayOutputStream()
        var checks = 0

        assertThrows(IOException::class.java) {
            ModImport.copyChecked(
                ByteArrayInputStream(ByteArray(firstChunk + 1) { 1 }),
                output,
                declaredSize = firstChunk.toLong() + 1,
                budget = ModImport.StreamBudget(maxFiles = 1, maxBytes = firstChunk.toLong() + 1),
                checkCancelled = {
                    if (++checks > 1) throw IOException("cancelled")
                },
            )
        }

        assertEquals(firstChunk, output.size())
    }

    @Test
    fun `provider path components reject traversal aliases controls and separators`() {
        for (name in listOf(
            "",
            ".",
            "..",
            "a/b",
            "a\\b",
            "nul" + 0.toChar() + "name",
            "line\nname",
            "a".repeat(256),
            ".dualsouls-import-v1.properties",
        )) {
            assertThrows(IOException::class.java) { ModImport.validateComponent(name) }
        }

        assertEquals("Résumé.dll", ModImport.validateComponent("Résumé.dll"))
        assertEquals(
            ModImport.collisionKey("Résumé.dll"),
            ModImport.collisionKey(Normalizer.normalize("Résumé.dll", Normalizer.Form.NFD)),
        )
        assertEquals(ModImport.collisionKey("Example.dll"), ModImport.collisionKey("example.DLL"))
        assertEquals("CaseName", ModImport.uniqueExistingName(listOf("CaseName"), "casename"))
        assertThrows(IOException::class.java) {
            ModImport.uniqueExistingName(listOf("CaseName", "casename"), "CASENAME")
        }
    }

    @Test
    fun `strict destination resolution never escapes staging`() {
        val staging = temp.newFolder("strict-destination")
        val child = ModImport.resolveDestination(staging, "plugin.dll", staging)

        assertEquals(File(staging, "plugin.dll").canonicalFile, child.canonicalFile)
        assertThrows(IOException::class.java) {
            ModImport.resolveDestination(staging, "..", staging)
        }
        assertThrows(IOException::class.java) {
            ModImport.resolveDestination(temp.newFolder("outside-staging"), "outside.dll", staging)
        }
    }

    @Test
    fun `direct traversal is iterative depth bounded and rejects canonical aliases`() {
        val source = temp.newFolder("bounded-direct-source")
        val deep = File(source, "one/two").apply { mkdirs() }
        File(deep, "Plugin.dll").writeText("plugin")
        val staging = temp.newFolder("bounded-direct-staging")

        assertThrows(IOException::class.java) {
            ModImport.copyDirectTree(
                source,
                staging,
                ModImport.StreamBudget(maxFiles = 8, maxBytes = 64, maxNodes = 8, maxDepth = 1),
            )
        }

        val seen = linkedSetOf<String>()
        ModImport.requireUniqueDirectPath(source, File(source, "one"), seen)
        assertThrows(IOException::class.java) {
            ModImport.requireUniqueDirectPath(source, File(source, "one/two/.."), seen)
        }
    }

    @Test
    fun `SAF metadata budget rejects oversized ids and cumulative cursor metadata`() {
        val oversized = ModImport.DocumentMetadataBudget(maxDocumentIdBytes = 4, maxMetadataBytes = 32)
        assertThrows(IOException::class.java) {
            oversized.entry("abcde", "Plugin.dll", directory = false, declaredSize = 1)
        }
        assertThrows(IOException::class.java) {
            oversized.entry("bad\nid", "Plugin.dll", directory = false, declaredSize = 1)
        }

        val aggregate = ModImport.DocumentMetadataBudget(maxDocumentIdBytes = 8, maxMetadataBytes = 9)
        val first = aggregate.entry("1234", "Name", directory = true, declaredSize = 0)
        assertEquals("1234", first.documentId)
        assertEquals("Name", first.displayName)
        assertThrows(IOException::class.java) {
            aggregate.entry("56", "X", directory = false, declaredSize = 1)
        }
    }

    @Test
    fun `provider traversal rejects repeated ids unsafe names and normalized collisions`() {
        class Source(private val rows: Map<String, List<ModImport.DocumentEntry>>) : ModImport.DocumentSource {
            override fun children(documentId: String) = rows[documentId].orEmpty()
            override fun open(documentId: String): InputStream = ByteArrayInputStream(byteArrayOf(1))
        }

        val repeated = Source(
            mapOf(
                "root" to listOf(
                    ModImport.DocumentEntry("same", "one", true, 0),
                    ModImport.DocumentEntry("same", "two", true, 0),
                ),
            ),
        )
        assertThrows(IOException::class.java) {
            ModImport.copyDocumentTree(repeated, "root", temp.newFolder("saf-repeat"), ModImport.StreamBudget())
        }

        val unsafe = Source(mapOf("root" to listOf(ModImport.DocumentEntry("bad", "../Plugin.dll", false, 1))))
        assertThrows(IOException::class.java) {
            ModImport.copyDocumentTree(unsafe, "root", temp.newFolder("saf-unsafe"), ModImport.StreamBudget())
        }

        val collisions = Source(
            mapOf(
                "root" to listOf(
                    ModImport.DocumentEntry("a", "Résumé.dll", false, 1),
                    ModImport.DocumentEntry(
                        "b",
                        Normalizer.normalize("Résumé.dll", Normalizer.Form.NFD),
                        false,
                        1,
                    ),
                ),
            ),
        )
        assertThrows(IOException::class.java) {
            ModImport.copyDocumentTree(
                collisions,
                "root",
                temp.newFolder("saf-collision"),
                ModImport.StreamBudget(),
            )
        }
    }

    @Test
    fun `bounded cleanup fails closed before traversing an oversized tree`() {
        val owned = temp.newFolder("bounded-cleanup")
        File(owned, "one/two/three").apply { mkdirs() }
        File(owned, "one/a").writeText("a")
        File(owned, "one/two/b").writeText("b")

        assertFalse(ModImport.deleteOwnedTree(owned, maxNodes = 2, maxDepth = 8))
        assertTrue(owned.exists())
    }

    @Test
    fun `recovery verification enforces file and byte bounds`() {
        val owned = temp.newFolder("bounded-recovery-verification")
        File(owned, "one.dll").writeText("123")
        File(owned, "two.dll").writeText("456")

        assertThrows(IOException::class.java) {
            ModImport.digestOwnedTree(
                owned,
                ModImport.StreamBudget(maxFiles = 1, maxBytes = 8, maxNodes = 8, maxDepth = 8),
            )
        }
        assertThrows(IOException::class.java) {
            ModImport.digestOwnedTree(
                owned,
                ModImport.StreamBudget(maxFiles = 2, maxBytes = 5, maxNodes = 8, maxDepth = 8),
            )
        }
    }

    @Test
    fun `legacy shared mod state migrates independently without overwriting profile state`() {
        val mods = temp.newFolder("legacy-mod-state")
        File(mods, "config/nested/example.cfg").apply {
            requireNotNull(parentFile).mkdirs()
            writeText("legacy")
        }
        File(mods, "disabled.txt").writeText("Example.dll\n")
        File(mods, "disabled-assemblies.txt").writeText("Example.Plugin\n")
        val hollowKnight = temp.newFolder("legacy-hollow-knight")
        val silksong = temp.newFolder("legacy-silksong")
        File(hollowKnight, "config/nested/example.cfg").apply {
            requireNotNull(parentFile).mkdirs()
            writeText("profile")
        }
        File(hollowKnight, "disabled.txt").writeText("Own.dll\n")
        File(hollowKnight, ".legacy-mod-state-migrated-v1.part").writeText("interrupted")

        Mods.ensure(mods, hollowKnight)
        Mods.ensure(mods, silksong)
        Mods.ensure(mods, silksong)

        assertEquals("profile", File(hollowKnight, "config/nested/example.cfg").readText())
        assertEquals("Own.dll", File(hollowKnight, "disabled.txt").readText().trim())
        assertEquals("legacy", File(silksong, "config/nested/example.cfg").readText())
        assertEquals("Example.dll", File(silksong, "disabled.txt").readText().trim())
        assertEquals("Example.Plugin", File(silksong, "disabled-assemblies.txt").readText().trim())
        for (state in listOf(hollowKnight, silksong)) {
            assertTrue(File(state, ".legacy-mod-state-migrated-v1").isFile)
            assertFalse(File(state, ".legacy-mod-state-migrated-v1.part").exists())
            assertFalse(File(state, ".legacy-adopted-v1").exists())
        }
    }

    @Test
    fun `mod state migration fails on directory type collisions and retries cleanly`() {
        val mods = temp.newFolder("collision-legacy-mod-state")
        File(mods, "config/nested/example.cfg").apply {
            requireNotNull(parentFile).mkdirs()
            writeText("legacy")
        }
        val state = temp.newFolder("collision-profile-state")
        val config = File(state, "config").apply { writeText("root collision") }
        val marker = File(state, ".legacy-mod-state-migrated-v1")

        assertThrows(IOException::class.java) { ModStateMigration.migrate(mods, state) }
        assertFalse(marker.exists())
        assertTrue(config.delete())
        assertTrue(config.mkdir())
        val nested = File(config, "nested").apply { writeText("nested collision") }

        assertThrows(IOException::class.java) { ModStateMigration.migrate(mods, state) }
        assertFalse(marker.exists())
        assertTrue(nested.delete())

        ModStateMigration.migrate(mods, state)

        assertEquals("legacy", File(state, "config/nested/example.cfg").readText())
        assertTrue(marker.isFile)
    }

    @Test
    fun `import identity prevents lossy collisions and requires confirmation for legacy replacement`() {
        val storage = temp.newFolder("identity-storage")
        val mods = File(storage, "mods").apply { mkdirs() }
        val first = ModImport.plan("Example:Mod", "provider|first", mods)
        val second = ModImport.plan("Example?Mod", "provider|second", mods)

        assertFalse(first.requiresConfirmation)
        assertFalse(second.requiresConfirmation)
        assertFalse(first.targetName == second.targetName)
        assertFalse(ModImport.transactionRoot(mods).canonicalPath.startsWith(mods.canonicalPath + File.separator))

        val legacy = File(mods, "Legacy").apply { mkdirs() }
        File(legacy, "Plugin.dll").writeText("old")
        val unverified = ModImport.plan("Legacy", "provider|legacy", mods)
        assertTrue(unverified.requiresConfirmation)

        ModImport.writeIdentity(legacy, unverified)
        assertFalse(ModImport.plan("Legacy", "provider|legacy", mods).requiresConfirmation)
        val distinct = ModImport.plan("Legacy", "provider|different", mods)
        assertFalse(distinct.targetName == unverified.targetName)
        assertThrows(IOException::class.java) { ModImport.plan("config", "provider|config", mods) }
        assertThrows(IOException::class.java) { ModImport.plan(".Example.backup", "provider|backup", mods) }
        assertThrows(IOException::class.java) { ModImport.plan(".Example.incoming", "provider|incoming", mods) }
    }

    @Test
    fun `recovery removes abandoned staging after an external target appears`() {
        val storage = temp.newFolder("abandoned-staging-target-race")
        val mods = File(storage, "mods").apply { mkdirs() }
        val plan = ModImport.plan("Example", "provider|abandoned-staging", mods)
        val transaction = ModImport.beginTransaction(mods, plan)
        File(transaction.staging, "partial.dll").apply {
            requireNotNull(parentFile).mkdirs()
            writeText("partial")
        }
        File(plan.target, "external.dll").apply {
            requireNotNull(parentFile).mkdirs()
            writeText("external")
        }

        ModImport.reconcileTransactions(mods)

        assertEquals("external", File(plan.target, "external.dll").readText())
        assertFalse(File(plan.target, "partial.dll").exists())
        assertFalse(transaction.root.exists())
        assertPipelineUsableAfterRejectedImport(
            mods,
            temp.newFolder("abandoned-staging-target-race-state"),
            "Example/external.dll",
        )
    }

    @Test
    fun `recovery removes empty transaction after an external target change`() {
        val storage = temp.newFolder("empty-transaction-target-race")
        val mods = File(storage, "mods").apply { mkdirs() }
        val target = File(mods, "Example").apply { mkdirs() }
        val external = File(target, "external.dll").apply { writeText("before") }
        val initial = ModImport.plan("Example", "provider|empty-transaction", mods)
        ModImport.writeIdentity(target, initial)
        val plan = ModImport.plan("Example", "provider|empty-transaction", mods)
        val transaction = ModImport.beginTransaction(mods, plan)
        external.writeText("external mutation")

        ModImport.reconcileTransactions(mods)

        assertEquals("external mutation", external.readText())
        assertFalse(transaction.root.exists())
        assertPipelineUsableAfterRejectedImport(
            mods,
            temp.newFolder("empty-transaction-target-race-state"),
            "Example/external.dll",
        )
    }

    @Test
    fun `global recovery restores backups removes abandoned staging and fails on ambiguity`() {
        val storage = temp.newFolder("global-recovery")
        val mods = File(storage, "mods").apply { mkdirs() }
        File(mods, ".Legacy.incoming/Partial.dll").apply {
            requireNotNull(parentFile).mkdirs()
            writeText("partial")
        }
        File(mods, ".Legacy.backup/Old.dll").apply {
            requireNotNull(parentFile).mkdirs()
            writeText("legacy-old")
        }
        ModImport.reconcileTransactions(mods)
        assertEquals("legacy-old", File(mods, "Legacy/Old.dll").readText())
        assertFalse(File(mods, ".Legacy.incoming").exists())
        assertFalse(File(mods, ".Legacy.backup").exists())

        val restoreTarget = File(mods, "Restore").apply { mkdirs() }
        File(restoreTarget, "Old.dll").writeText("old")
        val restorePlan = ModImport.plan("Restore", "provider|restore", mods)
        val restore = ModImport.beginTransaction(mods, restorePlan)
        assertTrue(restoreTarget.renameTo(restore.backup))
        File(restore.staging, "Partial.dll").apply { requireNotNull(parentFile).mkdirs(); writeText("partial") }

        val existingTarget = File(mods, "Existing").apply { mkdirs() }
        File(existingTarget, "Existing.dll").writeText("current")
        val abandonedPlan = ModImport.plan("Existing", "provider|existing", mods)
        val abandoned = ModImport.beginTransaction(mods, abandonedPlan)
        File(abandoned.staging, "Partial.dll").apply { requireNotNull(parentFile).mkdirs(); writeText("partial") }

        ModImport.reconcileTransactions(mods)

        assertEquals("old", File(restorePlan.target, "Old.dll").readText())
        assertFalse(restore.root.exists())
        assertEquals("current", File(abandonedPlan.target, "Existing.dll").readText())
        assertFalse(abandoned.root.exists())
        assertEquals(
            setOf("Existing/Existing.dll", "Legacy/Old.dll", "Restore/Old.dll"),
            Mods.all(mods).map { Mods.relativePath(mods, it).replace(File.separatorChar, '/') }.toSet(),
        )

        val missingStorage = temp.newFolder("missing-mod-root-recovery")
        val missingMods = File(missingStorage, "mods").apply { mkdirs() }
        val missingTarget = File(missingMods, "MissingRoot").apply { mkdirs() }
        File(missingTarget, "Old.dll").writeText("old")
        val missingPlan = ModImport.plan("MissingRoot", "provider|missing-root", missingMods)
        val missing = ModImport.beginTransaction(missingMods, missingPlan)
        assertTrue(missingTarget.renameTo(missing.backup))
        assertTrue(missingMods.delete())
        assertEquals(listOf("MissingRoot/Old.dll"), Mods.all(missingMods).map {
            Mods.relativePath(missingMods, it).replace(File.separatorChar, '/')
        })

        val ambiguousPlan = ModImport.plan("Ambiguous", "provider|ambiguous", mods)
        val ambiguous = ModImport.beginTransaction(mods, ambiguousPlan)
        File(ambiguousPlan.target, "New.dll").apply { requireNotNull(parentFile).mkdirs(); writeText("new") }
        File(ambiguous.backup, "Old.dll").apply { requireNotNull(parentFile).mkdirs(); writeText("old") }
        assertThrows(IOException::class.java) { Mods.all(mods) }
        assertTrue(ambiguousPlan.target.exists())
        assertTrue(ambiguous.backup.exists())
    }

    @Test
    fun `legacy recovery does not create a normalized target collision`() {
        val storage = temp.newFolder("legacy-recovery-collision")
        val mods = File(storage, "mods").apply { mkdirs() }
        val liveName = "Résumé"
        val remnantName = Normalizer.normalize(liveName, Normalizer.Form.NFD)
        File(mods, "$liveName/Current.dll").apply {
            requireNotNull(parentFile).mkdirs()
            writeText("current")
        }
        assertEquals(
            liveName,
            ModImport.resolveRecoveryTarget(mods, listOf(liveName), remnantName).name,
        )
        val backup = File(mods, ".$remnantName.backup/Old.dll").apply {
            requireNotNull(parentFile).mkdirs()
            writeText("old")
        }

        assertThrows(IOException::class.java) { ModImport.reconcileTransactions(mods) }
        assertEquals("current", File(mods, "$liveName/Current.dll").readText())
        assertEquals("old", backup.readText())
    }

    @Test
    fun `dedicated recovery rejects unknown transaction contents`() {
        val unknownStorage = temp.newFolder("unknown-transaction-content")
        val unknownMods = File(unknownStorage, "mods").apply { mkdirs() }
        val unknown = ModImport.beginTransaction(
            unknownMods,
            ModImport.plan("Unknown", "provider|unknown", unknownMods),
        )
        File(unknown.root, "unexpected").writeText("do not delete")

        assertThrows(IOException::class.java) { ModImport.reconcileTransactions(unknownMods) }
        assertEquals("do not delete", File(unknown.root, "unexpected").readText())

        val malformedStorage = temp.newFolder("malformed-transaction-content")
        val malformedMods = File(malformedStorage, "mods").apply { mkdirs() }
        val malformed = ModImport.beginTransaction(
            malformedMods,
            ModImport.plan("Malformed", "provider|malformed", malformedMods),
        )
        malformed.staging.writeText("not a directory")

        assertThrows(IOException::class.java) { ModImport.reconcileTransactions(malformedMods) }
        assertEquals("not a directory", malformed.staging.readText())
    }

    @Test
    fun `import coordinator exposes progress and prevents concurrent runs`() {
        val coordinator = ModImport.ImportCoordinator()
        val first = requireNotNull(coordinator.begin())
        val mods = temp.newFolder("busy-import-mods")

        assertEquals(null, coordinator.begin())
        assertThrows(IOException::class.java) {
            ModImport.reconcileTransactions(mods, coordinator = coordinator)
        }
        first.progress(ModImport.Progress(3, 9, "copying"))
        assertEquals(true, coordinator.snapshot().busy)
        assertEquals(3, coordinator.snapshot().progress.files)
        assertEquals("copying", coordinator.snapshot().progress.message)

        first.close()
        assertEquals(false, coordinator.snapshot().busy)
        requireNotNull(coordinator.begin()).close()
    }

    @Test
    fun `profiles share plugin discovery but isolate config disabled state and gates`() {
        val mods = temp.newFolder("shared-mods")
        val plugin = File(mods, "Example.dll").apply { writeText("plugin") }
        val hollowKnightState = temp.newFolder("hollow-knight-state")
        val silksongState = temp.newFolder("silksong-state")
        val candidate = temp.newFolder("candidate-mod-metadata")
        Mods.reportFile(candidate).writeText(
            """{"plugins":[{"File":"Example.dll","Assembly":"Example.Plugin","Guid":"example","Name":"Example","Version":"1.0","Status":"Ok","Patched":1,"Issues":[]}]}""",
        )
        Mods.recordCandidate(mods, candidate)
        val publisher = GenerationPublisher(
            ProfilePaths(temp.newFolder("published-files"), GameProfiles.require("hollow-knight")),
        )
        val staging = publisher.begin("job", "generation")
        File(staging, "payload.bin").writeText("payload")
        Mods.stageForGeneration(candidate, staging)
        publisher.finalizeGeneration(
            "job",
            "generation",
            GenerationMetadata("a".repeat(64), "unity-test", "b".repeat(64)),
        )
        publisher.publish("job", "generation")
        Mods.ensure(mods, hollowKnightState)
        Mods.ensure(mods, silksongState)
        File(Mods.configDir(hollowKnightState), "example.cfg").writeText("profile=hk")

        Mods.setEnabled(hollowKnightState, "Example.dll", enabled = false)
        Mods.writeCurrentGates(hollowKnightState, publisher)

        assertEquals(listOf(plugin), Mods.all(mods))
        assertEquals(listOf(plugin), Mods.enabled(mods, silksongState))
        assertTrue(Mods.enabled(mods, hollowKnightState).isEmpty())
        assertEquals(setOf("Example.dll"), Mods.disabled(hollowKnightState))
        assertTrue(Mods.disabled(silksongState).isEmpty())
        assertEquals("profile=hk", File(Mods.configDir(hollowKnightState), "example.cfg").readText())
        assertFalse(File(Mods.configDir(silksongState), "example.cfg").exists())
        assertEquals("Example.Plugin", Mods.gatesFile(hollowKnightState).readText().trim())
        assertFalse(Mods.gatesFile(silksongState).exists())
        assertEquals("plugin", plugin.readText())

        assertTrue(plugin.delete())
        Mods.writeCurrentGates(hollowKnightState, publisher)
        assertEquals("Example.Plugin", Mods.gatesFile(hollowKnightState).readText().trim())
        plugin.writeText("plugin")

        Mods.setEnabled(hollowKnightState, "Example.dll", enabled = true)
        Mods.writeCurrentGates(hollowKnightState, publisher)

        assertTrue(Mods.disabled(hollowKnightState).isEmpty())
        assertFalse(Mods.gatesFile(hollowKnightState).exists())
        assertEquals("plugin", plugin.readText())
    }

    private fun externalImportSource(label: String): File =
        File(
            Environment.getExternalStorageDirectory(),
            "dualsouls-$label-${UUID.randomUUID()}",
        ).apply { check(mkdirs()) }

    private fun directImportPlan(source: File, mods: File): Pair<Uri, ModImport.ImportPlan> {
        val external = Environment.getExternalStorageDirectory().canonicalFile
        val relative = source.canonicalFile.relativeTo(external).invariantSeparatorsPath
        val documentId = "primary:$relative"
        val uri = DocumentsContract.buildTreeDocumentUri(EXTERNAL_STORAGE_AUTHORITY, documentId)
        return uri to ModImport.plan(
            source.name,
            "${uri.scheme}|${uri.authority}|$documentId",
            mods,
        )
    }

    private fun assertPipelineUsableAfterRejectedImport(
        mods: File,
        state: File,
        expectedRelative: String,
    ) {
        Mods.ensure(mods, state)
        val normalizedExpected = expectedRelative.replace('/', File.separatorChar)
        assertTrue(Mods.all(mods).any { Mods.relativePath(mods, it) == normalizedExpected })

        val candidate = temp.newFolder("rejected-import-candidate-${state.name}")
        val snapshot = Mods.snapshotForBuild(mods, candidate)
        assertTrue(File(snapshot, normalizedExpected).isFile)

        val next = ModImport.plan("Later-${state.name}", "test|later|${state.name}", mods)
        val transaction = ModImport.beginTransaction(mods, next)
        File(transaction.staging, "Later.dll").apply {
            requireNotNull(parentFile).mkdirs()
            writeText("later")
        }
        ModImport.writeIdentity(transaction.staging, next)
        ModImport.replaceStaged(transaction)
        assertTrue(Mods.all(mods).any { it.name == "Later.dll" })
    }
}
