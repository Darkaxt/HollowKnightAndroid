package dev.silksong.launcher.builtinmods

import dev.silksong.launcher.runtime.GameLifecycleAuthority
import java.io.File
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder

class BuiltInModsControllerTest {
    @get:Rule val temporary = TemporaryFolder()

    @Test fun `catalog exposes only functional profile-specific rows`() {
        val hollowKnight = BuiltInModCatalog.forGame("hollow-knight")
        val silksong = BuiltInModCatalog.forGame("silksong")

        assertEquals(7, hollowKnight.size)
        assertEquals(
            listOf("damage_received", "nail_damage", "one_hit_kills", "run_speed", "unlimited_soul"),
            hollowKnight.filter { it.group != "PRESENTATION" }.map { it.id },
        )
        assertEquals(
            listOf("damage_received", "unlimited_silk", "one_hit_kills", "equip_anywhere"),
            silksong.map { it.id },
        )
        assertTrue((hollowKnight + silksong).all { it.actionable })
        assertTrue(BuiltInModCatalog.deferred("hollow-knight").isNotEmpty())
    }

    @Test fun `missing and malformed state fail to master off and descriptor defaults`() {
        val file = temporary.newFile().apply { writeText("not-a-state-line\n") }
        val controller = controller("hollow-knight", file)

        assertFalse(controller.snapshot().masterEnabled)
        assertEquals("vanilla", controller.snapshot().value("damage_received"))
    }

    @Test fun `whole row cycling requires master and persists exact namespaced keys`() {
        val file = temporary.newFile()
        val controller = controller("hollow-knight", file)

        assertFalse(controller.cycle("damage_received").success)
        assertEquals("vanilla", controller.snapshot().value("damage_received"))
        assertTrue(controller.setMaster(true).success)
        assertTrue(controller.cycle("damage_received").success)
        assertEquals("no_mask_loss", controller.snapshot().value("damage_received"))
        assertEquals(
            "dualsouls.mods.hollow-knight.master=1\n" +
                "dualsouls.mods.hollow-knight.value.damage_received=no_mask_loss\n",
            file.readText(),
        )
    }

    @Test fun `reset restores defaults without enabling master`() {
        val file = temporary.newFile()
        val controller = controller("silksong", file)
        controller.setMaster(true)
        controller.cycle("unlimited_silk")

        assertTrue(controller.reset().success)
        assertTrue(controller.snapshot().masterEnabled)
        assertEquals("off", controller.snapshot().value("unlimited_silk"))
    }

    @Test fun `profile state is isolated even when files share a parent`() {
        val root = temporary.newFolder()
        val hkFile = File(root, "hollow-knight/mods/builtin-state.txt")
        val ssFile = File(root, "silksong/mods/builtin-state.txt")
        val hk = controller("hollow-knight", hkFile)
        val ss = controller("silksong", ssFile)

        hk.setMaster(true)
        hk.cycle("one_hit_kills")

        assertEquals("on", hk.snapshot().value("one_hit_kills"))
        assertFalse(ss.snapshot().masterEnabled)
        assertEquals("off", ss.snapshot().value("one_hit_kills"))
        assertFalse(ssFile.exists())
    }

    @Test fun `active and unknown lifecycle authority fail closed without writing`() {
        val activeFile = File(temporary.newFolder(), "state.txt")
        val activeAuthority = GameLifecycleAuthority(temporary.newFolder())
        val owner = activeAuthority.acquireForGame()
        try {
            assertMutationBlocked(controller("silksong", activeFile, activeAuthority), activeFile)
        } finally {
            owner.close()
        }

        val unknownFile = File(temporary.newFolder(), "state.txt")
        val unknownAuthority = GameLifecycleAuthority(temporary.newFolder()).apply { markLaunchPending() }
        assertMutationBlocked(controller("silksong", unknownFile, unknownAuthority), unknownFile)
    }

    private fun assertMutationBlocked(controller: BuiltInModsController, file: File) {
        val result = controller.setMaster(true)
        assertFalse(result.success)
        assertTrue(result.message.contains("closed", ignoreCase = true))
        assertFalse(file.exists())
    }

    @Test fun `reload observes state written by the game process`() {
        val file = temporary.newFile()
        val controller = controller("silksong", file)
        file.writeText(
            "dualsouls.mods.silksong.master=1\n" +
                "dualsouls.mods.silksong.value.equip_anywhere=on\n",
        )

        controller.reload()

        assertTrue(controller.snapshot().masterEnabled)
        assertEquals("on", controller.snapshot().value("equip_anywhere"))
    }

    private fun controller(
        gameId: String,
        file: File,
        authority: GameLifecycleAuthority = GameLifecycleAuthority(File(file.parentFile, "lifecycle")),
    ) = BuiltInModsController(
        gameId,
        BuiltInModCatalog.forGame(gameId),
        LineModStateStore(file),
        authority,
    )
}
