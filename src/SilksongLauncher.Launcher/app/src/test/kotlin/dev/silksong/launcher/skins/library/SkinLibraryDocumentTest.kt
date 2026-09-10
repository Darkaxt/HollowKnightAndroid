package dev.silksong.launcher.skins.library

import org.junit.Assert.*
import org.junit.Test

class SkinLibraryDocumentTest {
    private val a = LibraryPack("a", "Pack A", "Unknown", "a".repeat(64), "b".repeat(64), "c".repeat(64))
    private val b = a.copy(id = "b", candidateKey = "d".repeat(64))

    @Test fun `round trip preserves explicit selection and eligibility order`() {
        val document = SkinLibraryDocument(mode = LibraryMode.ROTATE, selectedPackId = "a", packs = listOf(a, b), eligiblePackIds = listOf("b", "a"))
        assertEquals(document, SkinLibraryCodec.decode(SkinLibraryCodec.encode(document)))
    }
    @Test fun `fresh library is OFF and does not choose an imported pack`() {
        val document = SkinLibraryDocument(packs = listOf(a))
        assertEquals(LibraryMode.OFF, document.mode)
        assertNull(document.selectedPackId)
        assertTrue(document.eligiblePackIds.isEmpty())
        assertEquals(document, SkinLibraryCodec.decode(SkinLibraryCodec.encode(document)))
    }
    @Test fun `malformed identity duplicates dangling ids and unknown modes fail closed`() {
        val good = SkinLibraryCodec.encode(SkinLibraryDocument(packs = listOf(a))).toString(Charsets.UTF_8)
        for (bad in listOf(good.replace("hollow-knight", "silksong"), good.replace("\"OFF\"", "\"BOGUS\""),
            good.replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1"), good + "{}", "{}")) {
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
            addProperty("rotationRun", "e".repeat(32)); addProperty("lastDeath", 7); addProperty("pendingPackId", "b")
        }
        val decoded = runCatching { SkinLibraryCodec.decode(extended.toString().toByteArray()) }
        assertTrue("Bounded rotation extension must decode: ${decoded.exceptionOrNull()}", decoded.isSuccess)
        assertEquals(extended, com.google.gson.JsonParser.parseString(SkinLibraryCodec.encode(decoded.getOrThrow()).toString(Charsets.UTF_8)))
        val old = com.google.gson.JsonParser.parseString(legacy).asJsonObject.apply { remove("rotationRun"); remove("lastDeath"); remove("pendingPackId") }
        assertEquals("a", SkinLibraryCodec.decode(old.toString().toByteArray()).selectedPackId)
        for ((key, value) in listOf("lastDeath" to "-1", "pendingPackId" to "\"missing\"", "rotationRun" to "\"bad\"")) {
            val bad = extended.deepCopy().apply { add(key, com.google.gson.JsonParser.parseString(value)) }
            assertThrows(IllegalArgumentException::class.java) { SkinLibraryCodec.decode(bad.toString().toByteArray()) }
        }
    }

    @Test fun `document byte bound is enforced before parsing`() {
        assertThrows(IllegalArgumentException::class.java) { SkinLibraryCodec.decode(ByteArray(SkinLibraryCodec.MAX_BYTES + 1)) }
    }
}
