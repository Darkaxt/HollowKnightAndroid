package dev.silksong.launcher.skins.library

import org.junit.Assert.*
import org.junit.Test

class SkinLibraryDocumentTest {
    private val a = LibraryPack("a", "Pack A", "Unknown", "a".repeat(64), "b".repeat(64), "c".repeat(64))
    private val b = a.copy(id = "b", candidateKey = "d".repeat(64))

    @Test fun `round trip preserves explicit selection eligibility order and sprite scope`() {
        val document = SkinLibraryDocument(mode = LibraryMode.ROTATE, selectedPackId = "a", packs = listOf(a, b),
            eligiblePackIds = listOf("b", "a"), spriteScope = SpriteScope.CHARACTER_HUD)
        assertEquals(document, SkinLibraryCodec.decode(SkinLibraryCodec.encode(document)))
    }
    @Test fun `fresh library is OFF and does not choose an imported pack`() {
        val document = SkinLibraryDocument(packs = listOf(a))
        assertEquals(LibraryMode.OFF, document.mode)
        assertEquals(SpriteScope.ALL, document.spriteScope)
        assertNull(document.selectedPackId)
        assertTrue(document.eligiblePackIds.isEmpty())
        assertEquals(document, SkinLibraryCodec.decode(SkinLibraryCodec.encode(document)))
    }
    @Test fun `malformed identity duplicates dangling ids and unknown modes fail closed`() {
        val good = SkinLibraryCodec.encode(SkinLibraryDocument(packs = listOf(a))).toString(Charsets.UTF_8)
        for (bad in listOf(good.replace("hollow-knight", "silksong"), good.replace("\"OFF\"", "\"BOGUS\""),
            good.replace("\"schemaVersion\":2", "\"schemaVersion\":2,\"schemaVersion\":2"), good + "{}", "{}")) {
            assertThrows(IllegalArgumentException::class.java) { SkinLibraryCodec.decode(bad.toByteArray()) }
        }
        for (bad in listOf(SkinLibraryDocument(packs = listOf(a, a)), SkinLibraryDocument(selectedPackId = "missing"),
            SkinLibraryDocument(packs = listOf(a), eligiblePackIds = listOf("a", "a")), SkinLibraryDocument(mode = LibraryMode.ON))) {
            assertThrows(IllegalArgumentException::class.java) { SkinLibraryCodec.encode(bad) }
        }
    }
    @Test fun `rotation extension round trips and legacy document remains accepted`() {
        val legacy = SkinLibraryCodec.encode(SkinLibraryDocument(LibraryMode.ROTATE, "a", listOf(a,b), listOf("a","b"))).toString(Charsets.UTF_8)
        val extended = com.google.gson.JsonParser.parseString(legacy).asJsonObject.apply {
            remove("spriteScope")
            addProperty("rotationRun", "e".repeat(32)); addProperty("lastDeath", 7); addProperty("pendingPackId", "b")
        }
        val decoded = runCatching { SkinLibraryCodec.decode(extended.toString().toByteArray()) }
        assertTrue("Bounded rotation extension must decode: ${decoded.exceptionOrNull()}", decoded.isSuccess)
        assertEquals(SpriteScope.CHARACTER_HUD, decoded.getOrThrow().spriteScope)
        assertEquals("CHARACTER_HUD", com.google.gson.JsonParser.parseString(
            SkinLibraryCodec.encode(decoded.getOrThrow()).toString(Charsets.UTF_8)).asJsonObject["spriteScope"].asString)
        val old = com.google.gson.JsonParser.parseString(legacy).asJsonObject.apply {
            remove("rotationRun"); remove("lastDeath"); remove("pendingPackId"); remove("queuedDeathOccurrences")
        }
        assertEquals("a", SkinLibraryCodec.decode(old.toString().toByteArray()).selectedPackId)
        for ((key, value) in listOf("lastDeath" to "-1", "pendingPackId" to "\"missing\"", "rotationRun" to "\"bad\"")) {
            val bad = extended.deepCopy().apply { add(key, com.google.gson.JsonParser.parseString(value)) }
            assertThrows(IllegalArgumentException::class.java) { SkinLibraryCodec.decode(bad.toString().toByteArray()) }
        }
    }

    @Test fun `vanilla pending state round trips and old documents default it off`() {
        val pendingVanilla = SkinLibraryDocument(
            mode = LibraryMode.ROTATE,
            selectedPackId = "a",
            packs = listOf(a, b),
            eligiblePackIds = listOf("a", "b"),
            rotationRun = "e".repeat(32),
            lastDeath = 7,
            pendingVanilla = true,
        )
        assertEquals(pendingVanilla, SkinLibraryCodec.decode(SkinLibraryCodec.encode(pendingVanilla)))

        val old = com.google.gson.JsonParser.parseString(SkinLibraryCodec.encode(pendingVanilla).toString(Charsets.UTF_8))
            .asJsonObject.apply {
                remove("pendingVanilla")
                add("pendingPackId", com.google.gson.JsonNull.INSTANCE)
                addProperty("lastDeath", 0)
            }
        assertFalse(SkinLibraryCodec.decode(old.toString().toByteArray()).pendingVanilla)
        assertThrows(IllegalArgumentException::class.java) {
            SkinLibraryCodec.encode(pendingVanilla.copy(pendingPackId = "b"))
        }
    }

    @Test fun `legacy sprite scope defaults use profile authority and mode`() {
        fun legacy(mode: LibraryMode, profileId: String): SkinLibraryDocument {
            val selected = if (mode == LibraryMode.OFF) null else "a"
            val root = com.google.gson.JsonParser.parseString(SkinLibraryCodec.encode(
                SkinLibraryDocument(mode = mode, selectedPackId = selected, packs = listOf(a)), profileId,
            ).toString(Charsets.UTF_8)).asJsonObject
            root.remove("spriteScope")
            return SkinLibraryCodec.decode(root.toString().toByteArray(), profileId)
        }

        assertEquals(SpriteScope.ALL, legacy(LibraryMode.OFF, "hollow-knight").spriteScope)
        assertEquals(SpriteScope.ALL, legacy(LibraryMode.ON, "hollow-knight").spriteScope)
        assertEquals(SpriteScope.CHARACTER_HUD, legacy(LibraryMode.ROTATE, "hollow-knight").spriteScope)
        assertEquals(SpriteScope.CHARACTER, legacy(LibraryMode.ROTATE, "silksong").spriteScope)
    }

    @Test fun `unknown sprite scope fails closed`() {
        val encoded = SkinLibraryCodec.encode(SkinLibraryDocument()).toString(Charsets.UTF_8)
            .replace("\"ALL\"", "\"HUD_ONLY\"")
        assertThrows(IllegalArgumentException::class.java) { SkinLibraryCodec.decode(encoded.toByteArray()) }
    }

    @Test fun `schema one migrates without attributing intent to any save`() {
        val legacy = """{"schemaVersion":1,"profileId":"silksong","mode":"ON","selectedPackId":"a","packs":[{"id":"a","name":"Pack A","author":"Unknown","candidateKey":"${a.candidateKey}","treeSha256":"${a.treeSha256}","receiptSha256":"${a.receiptSha256}"}],"eligiblePackIds":[]}"""
        val migrated = SkinLibraryCodec.decode(legacy.toByteArray(), "silksong")
        assertEquals("a", migrated.selectedPackId)
        assertNull(migrated.activeSaveSlot)
        assertTrue(migrated.saveAffinities.isEmpty())
        assertTrue(SkinLibraryCodec.encode(migrated, "silksong").toString(Charsets.UTF_8).contains("\"schemaVersion\":2"))
    }

    @Test fun `five actual slot identities imported and true default round trip with strict bounds`() {
        val document = SkinLibraryDocument(packs = listOf(a, b), activeSaveSlot = 0,
            saveAffinities = listOf(SaveSkinAffinity(0, "a", a.treeSha256), SaveSkinAffinity(1, "b", b.treeSha256),
                SaveSkinAffinity(2), SaveSkinAffinity(3), SaveSkinAffinity(4)))
        for (profile in listOf("hollow-knight", "silksong"))
            assertEquals(document, SkinLibraryCodec.decode(SkinLibraryCodec.encode(document, profile), profile))
        for (bad in listOf(document.copy(activeSaveSlot = -1), document.copy(activeSaveSlot = 5),
            document.copy(saveAffinities = document.saveAffinities + SaveSkinAffinity(0)),
            document.copy(saveAffinities = listOf(SaveSkinAffinity(0, "a", "d".repeat(64))))))
            assertThrows(IllegalArgumentException::class.java) { SkinLibraryCodec.encode(bad) }
    }

    @Test fun `document byte bound is enforced before parsing`() {
        assertThrows(IllegalArgumentException::class.java) { SkinLibraryCodec.decode(ByteArray(SkinLibraryCodec.MAX_BYTES + 1)) }
    }
}
