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

    @Test fun `catalog exposes every required row before optional extras`() {
        val hollowKnight = BuiltInModCatalog.forGame("hollow-knight")
        val silksong = BuiltInModCatalog.forGame("silksong")
        val required = listOf(
            "skins", "black_background",
            "run_speed", "fast_transitions", "auto_map", "innate_compass", "bench_teleport", "secret_radar",
            "nail_damage", "damage_taken", "damage_cap", "one_hit_kills", "unlimited_soul",
            "enemy_health_bars", "damage_numbers", "boss_retry",
            "equip_anywhere", "charm_costs", "unlimited_notches",
            "state_slot", "save_to_slot", "load_from_slot", "delete_slot",
            "geo_magnet", "keep_geo_on_death", "journal_one_kill", "geo_multiplier",
        )

        assertEquals(required, hollowKnight.take(required.size).map { it.contractId })
        assertEquals(required, silksong.take(required.size).map { it.contractId })
        assertTrue(hollowKnight.all { it.isAvailable })
        assertTrue(silksong.take(required.size).all { it.isAvailable })
        assertTrue(
            hollowKnight.none {
                it.id in setOf("instant_dialogue", "disable_world_rumble", "ignore_frost_slowdown")
            },
        )
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

    @Test fun `available choices cycle while operational rows remain non-persistent`() {
        val hollowKnightFile = temporary.newFile()
        val hollowKnight = controller("hollow-knight", hollowKnightFile)
        assertTrue(hollowKnight.setMaster(true).success)

        assertTrue(hollowKnight.cycle("auto_map").success)
        val afterChoice = hollowKnightFile.readText()
        assertFalse(hollowKnight.cycle("save_to_slot").success)
        assertEquals(afterChoice, hollowKnightFile.readText())
        assertTrue(hollowKnight.snapshot().descriptors.any { it.id == "save_to_slot" })

        val silksong = controller("silksong", temporary.newFile())
        assertTrue(silksong.setMaster(true).success)
        assertTrue(silksong.cycle("auto_map").success)
        assertTrue(silksong.snapshot().descriptors.any { it.id == "auto_map" })
    }

    @Test fun `reset preserves master without persisting command or route values`() {
        val file = temporary.newFile()
        val controller = controller("silksong", file)
        controller.setMaster(true)
        controller.cycle("damage_received")

        assertTrue(controller.reset().success)

        assertTrue(controller.snapshot().masterEnabled)
        assertFalse(file.readText().contains("value.save_to_slot"))
        assertFalse(file.readText().contains("value.skins"))
    }

    @Test fun `cycling supports both directions with wraparound`() {
        val file = temporary.newFile()
        val controller = controller("hollow-knight", file)
        controller.setMaster(true)

        assertTrue(controller.cycle("damage_received", -1).success)
        assertEquals("invincible", controller.snapshot().value("damage_received"))
        assertTrue(controller.cycle("damage_received", 1).success)
        assertEquals("vanilla", controller.snapshot().value("damage_received"))
        assertTrue(controller.cycle("damage_received").success)
        assertEquals("no_mask_loss", controller.snapshot().value("damage_received"))
    }

    @Test fun `backward cycling obeys lifecycle authority and does not write`() {
        val file = File(temporary.newFolder(), "state.txt")
        val authority = GameLifecycleAuthority(temporary.newFolder())
        val owner = authority.acquireForGame()
        try {
            val result = controller("silksong", file, authority).cycle("damage_received", -1)

            assertFalse(result.success)
            assertTrue(result.message.contains("closed", ignoreCase = true))
            assertFalse(file.exists())
        } finally {
            owner.close()
        }
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

    @Test fun `new Silksong rows default off and persist in the Silksong profile`() {
        val file = temporary.newFile()
        val controller = controller("silksong", file)

        for (id in listOf("instant_dialogue", "disable_world_rumble", "ignore_frost_slowdown")) {
            assertEquals("off", controller.snapshot().value(id))
        }
        assertTrue(controller.setMaster(true).success)
        assertTrue(controller.cycle("instant_dialogue").success)

        assertEquals("on", controller.snapshot().value("instant_dialogue"))
        assertTrue(file.readText().contains("dualsouls.mods.silksong.value.instant_dialogue=on"))
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
