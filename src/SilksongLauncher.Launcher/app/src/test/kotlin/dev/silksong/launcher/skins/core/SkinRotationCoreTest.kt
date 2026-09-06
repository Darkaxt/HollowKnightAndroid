package dev.silksong.launcher.skins.core

import dev.silksong.launcher.skins.registry.*
import org.junit.Assert.*
import org.junit.Test

class SkinRotationCoreTest {
    private val core = SkinRotationCore()
    private val hero = HeroBindingToken("hero-1")
    private val skin = SkinBindingToken("skin-1")
    private fun pack(id: String) = ActiveVisual.Pack(id, "a".repeat(64), "b".repeat(64), "c".repeat(64))
    private fun descriptor(id: String, name: String = id, eligible: Boolean = true) = RotationDescriptor(name, pack(id), eligible)
    private fun ring(vararg d: RotationDescriptor) = requireNotNull(RotationRing.tryCreate(d.toList()))
    private fun state(r: RotationRing = ring(descriptor("a"), descriptor("b"), descriptor("c")),
        selected: String? = "a", active: ActiveVisual = pack("a"), mode: SkinMode = SkinMode.ROTATE) =
        core.decide(RotationState(r, ActivationSnapshot(mode, selected, active, 7)), RotationEvent.Rebind(hero, skin)).state
    private fun death(n: ULong = 1uL, h: HeroBindingToken = hero, s: SkinBindingToken = skin) = RotationEvent.ConfirmDeath(DeathEpoch(n), h, s)
    private fun stable(n: ULong = 1uL, h: HeroBindingToken = hero, s: SkinBindingToken = skin) = RotationEvent.StableRespawn(StableRespawnToken(DeathEpoch(n), h, s))
    private fun unchanged(s: RotationState, e: RotationEvent) {
        val d = core.decide(s, e); assertEquals(s, d.state); assertTrue(d.intents.isEmpty())
    }

    @Test fun confirmationChoosesOnceAndStableEmitsReadinessNotVisualPermission() {
        val s = state(); val confirmed = core.decide(s, death())
        assertEquals(pack("b"), confirmed.state.pending?.candidate?.currentObject)
        assertEquals(DeathEpoch(1uL), confirmed.state.epochHighWater)
        assertEquals(RotationPendingPhase.AWAITING_STABILITY, confirmed.state.pending?.phase)
        assertTrue(confirmed.intents.isEmpty()); assertEquals(s.activation, confirmed.state.activation)
        val ready = core.decide(confirmed.state, stable())
        assertEquals(RotationPendingPhase.INTENT_ISSUED, ready.state.pending?.phase)
        val intent = ready.intents.single()
        assertEquals(RotationReadyIntent(DeathEpoch(1uL), hero, skin, descriptor("b"), s.activation), intent)
        assertEquals(intent, ready.state.pending?.issuedIntent)
        assertEquals(s.activation, ready.state.activation)
        unchanged(ready.state, stable()); unchanged(ready.state, death(2uL))
    }
    @Test fun ringUsesUnsignedUtf8NameThenAsciiIdNotInputOrUtf16Order() {
        val r = ring(descriptor("z", "same"), descriptor("a", "same"), descriptor("supplement", "😀"),
            descriptor("bmp", ""), descriptor("upper", "A"), descriptor("lower", "a"))
        assertEquals(listOf("upper", "lower", "a", "z", "bmp", "supplement"), r.entries.map { it.currentObject.id })
        assertEquals(r.entries, ring(*r.entries.reversed().toTypedArray()).entries)
    }
    @Test fun ringAndDecisionsDefensivelyCopyCallerLists() {
        val source = mutableListOf(descriptor("c"), descriptor("a"), descriptor("b"))
        val r = requireNotNull(RotationRing.tryCreate(source)); source.clear()
        assertEquals(listOf("a", "b", "c"), r.entries.map { it.currentObject.id })
        assertThrows(UnsupportedOperationException::class.java) { (r.entries as MutableList).clear() }
        val ready = core.decide(core.decide(state(r), death()).state, stable())
        assertThrows(UnsupportedOperationException::class.java) { (ready.intents as MutableList).clear() }
    }
    @Test fun ringRejectsInvalidNamesIdsHashesDuplicatesAndOverBound() {
        for (name in listOf("", " a", "a ", "Ａ", "é", "a\n", "a‮b", "\ud800", "x".repeat(81)))
            assertNull(RotationRing.tryCreate(listOf(descriptor("a", name))))
        for (id in listOf("", "A", "a/../b", "a-", "a".repeat(65)))
            assertNull(RotationRing.tryCreate(listOf(descriptor(id))))
        for (p in listOf(pack("a").copy(treeSha256 = "A".repeat(64)), pack("a").copy(contentSha256 = "bad"), pack("a").copy(importReceiptSha256 = "")))
            assertNull(RotationRing.tryCreate(listOf(RotationDescriptor("a", p, true))))
        assertNull(RotationRing.tryCreate(listOf(descriptor("a"), descriptor("a", "different"))))
        assertNull(RotationRing.tryCreate((1..65).map { descriptor("p$it") }))
        assertEquals(64, RotationRing.tryCreate((1..64).map { descriptor("p$it") })!!.entries.size)
        assertNotNull(RotationRing.tryCreate(listOf(descriptor("a", "😀".repeat(80)))))
    }
    @Test fun activeAnchorWinsThenSelectedThenBeforeFirstWithWrap() {
        assertEquals("b", core.decide(state(selected = "c"), death()).state.pending!!.candidate.currentObject.id)
        assertEquals("c", core.decide(state(selected = "b", active = pack("old")), death()).state.pending!!.candidate.currentObject.id)
        assertEquals("a", core.decide(state(selected = null, active = ActiveVisual.Vanilla), death()).state.pending!!.candidate.currentObject.id)
        assertEquals("a", core.decide(state(selected = "b", active = pack("c")), death()).state.pending!!.candidate.currentObject.id)
        val r = ring(descriptor("a", eligible = false), descriptor("b"), descriptor("c"))
        assertEquals("c", core.decide(state(r, "b"), death()).state.pending!!.candidate.currentObject.id)
    }
    @Test fun zeroOrOneEligibleNeverRotatesEvenIfDifferentFromActive() {
        for (r in listOf(ring(), ring(descriptor("b")), ring(descriptor("a", eligible = false), descriptor("b")))) {
            val s = state(r); val d = core.decide(s, death())
            assertNull(d.state.pending); assertTrue(d.intents.isEmpty()); assertEquals(s.activation, d.state.activation)
        }
        assertEquals("b", core.decide(state(ring(descriptor("a"), descriptor("b"))), death()).state.pending!!.candidate.currentObject.id)
    }
    @Test fun offHistoryAndOnPinIgnoreDeathAndNeverChangeActivation() {
        for (mode in listOf(SkinMode.OFF, SkinMode.ON)) {
            val s = state(mode = mode); unchanged(s, death()); unchanged(s, stable())
        }
    }
    @Test fun pendingRejectsDuplicatesOldEpochAndFutureWithoutConsumingThem() {
        val s = core.decide(state(), death(7uL)).state
        for (n in listOf(0uL, 1uL, 7uL, 8uL, ULong.MAX_VALUE)) unchanged(s, death(n))
        for (n in listOf(0uL, 6uL, 8uL)) unchanged(s, stable(n))
        assertEquals(DeathEpoch(7uL), s.epochHighWater)
        unchanged(state(), stable()); unchanged(state(), death(0uL))
    }
    @Test fun rebindBeforeStableRetainsExactCandidateEpochAndInvalidatesOldTokens() {
        val pending = core.decide(state(), death(5uL)).state
        val h = HeroBindingToken("hero-2"); val k = SkinBindingToken("skin-2")
        val rebound = core.decide(pending, RotationEvent.Rebind(h, k)).state
        assertEquals(pending.pending!!.candidate, rebound.pending!!.candidate)
        assertEquals(pending.epochHighWater, rebound.epochHighWater)
        unchanged(rebound, stable(5uL)); unchanged(rebound, death(6uL)); unchanged(rebound, RotationEvent.Rebind(h, k))
        unchanged(rebound, death(6uL, h, k))
        val ready = core.decide(rebound, stable(5uL, h, k))
        assertEquals(h, ready.intents.single().hero); assertEquals(k, ready.intents.single().skin)
    }
    @Test fun rebindAfterIssueRetainsOldCorrelationWithoutReopeningDelivery() {
        val issued = core.decide(core.decide(state(), death()).state, stable()).state
        val h = HeroBindingToken("hero-2"); val k = SkinBindingToken("skin-2")
        val rebound = core.decide(issued, RotationEvent.Rebind(h, k)).state
        assertEquals(issued.pending!!.issuedIntent, rebound.pending!!.issuedIntent)
        assertEquals(hero, rebound.pending!!.issuedIntent!!.hero); assertEquals(h, rebound.pending!!.hero)
        assertEquals(RotationPendingPhase.INTENT_ISSUED, rebound.pending!!.phase)
        unchanged(rebound, stable()); unchanged(rebound, stable(h = h, s = k)); unchanged(rebound, death(2uL, h, k))
    }
    @Test fun authorityRequiresExplicitRebindAndValidNormalizedTokens() {
        val unbound = RotationState(ring(descriptor("a"), descriptor("b")), state().activation)
        unchanged(unbound, death()); unchanged(unbound, stable())
        for (token in listOf("", " a", "Ａ", "\ud800", "x".repeat(257))) {
            unchanged(unbound, RotationEvent.Rebind(HeroBindingToken(token), skin))
            unchanged(unbound, RotationEvent.Rebind(hero, SkinBindingToken(token)))
        }
        unchanged(state(), death(h = HeroBindingToken("other")))
        unchanged(state(), death(s = SkinBindingToken("other")))
    }
    private fun noncharacters(): List<String> = ((0xFDD0..0xFDEF).toList() +
        (0..16).flatMap { listOf((it shl 16) + 0xFFFE, (it shl 16) + 0xFFFF) })
        .map { String(Character.toChars(it)) }

    @Test fun normalizedNoncharactersRemainExactDescriptorNamesAndOrdering() {
        for (noncharacter in noncharacters()) {
            val name = "x$noncharacter"
            // CanonicalJson.requireDisplayName uses this same NFKC equality, with no noncharacter exclusion.
            assertEquals(name, java.text.Normalizer.normalize(name, java.text.Normalizer.Form.NFKC))
            val r = requireNotNull(RotationRing.tryCreate(listOf(descriptor("b", name), descriptor("a", "x"))))
            assertEquals(listOf("x", name), r.entries.map { it.name })
            assertEquals(name.toByteArray(Charsets.UTF_8).toList(), r.entries[1].name.toByteArray(Charsets.UTF_8).toList())
            assertNotNull(RotationRing.tryCreate(listOf(descriptor("a", noncharacter.repeat(80)))))
            assertNull(RotationRing.tryCreate(listOf(descriptor("a", noncharacter.repeat(81)))))
        }
    }
    @Test fun normalizedNoncharactersRemainValidHeroAndSkinAuthority() {
        for (noncharacter in noncharacters()) {
            val h = HeroBindingToken("hero$noncharacter"); val k = SkinBindingToken("skin$noncharacter")
            val rebound = core.decide(state(), RotationEvent.Rebind(h, k))
            assertEquals("rebound", rebound.diagnosis)
            assertEquals(h, rebound.state.currentHero); assertEquals(k, rebound.state.currentSkin)
            val pending = core.decide(rebound.state, death(h = h, s = k)).state
            val intent = core.decide(pending, stable(h = h, s = k)).intents.single()
            assertEquals(h, intent.hero); assertEquals(k, intent.skin)
            val maximum = HeroBindingToken(noncharacter.repeat(256))
            assertEquals("rebound", core.decide(state(), RotationEvent.Rebind(maximum, k)).diagnosis)
            unchanged(state(), RotationEvent.Rebind(HeroBindingToken(noncharacter.repeat(257)), k))
        }
    }
    @Test fun noncharacterBoundariesNeverHideUnnormalizedOrMalformedSegments() {
        for (noncharacter in noncharacters()) {
            // Noncharacters are CCC=0 boundaries: this must not compose A with the later ring mark.
            assertNotNull(RotationRing.tryCreate(listOf(descriptor("a", "A${noncharacter}̊"))))
            for (name in listOf("Å$noncharacter", "${noncharacter}Ａ", "${noncharacter}é", "é${noncharacter}z", "x${noncharacter}\ud800", "\udc00$noncharacter")) {
                assertNull(RotationRing.tryCreate(listOf(descriptor("a", name))))
                unchanged(state(), RotationEvent.Rebind(HeroBindingToken(name), skin))
                unchanged(state(), RotationEvent.Rebind(hero, SkinBindingToken(name)))
            }
        }
    }

    @Test fun historyUsesEligibleIdAnchorButCandidateKeepsExactCurrentObject() {
        val current = descriptor("b").copy(currentObject = pack("b").copy(importReceiptSha256 = "d".repeat(64)))
        val retained = pack("a").copy(treeSha256 = "e".repeat(64), contentSha256 = "f".repeat(64), importReceiptSha256 = "d".repeat(64))
        val r = ring(current, descriptor("a"), descriptor("c"))
        val initial = state(r, selected = "c", active = retained)
        val pending = core.decide(initial, death()).state
        assertEquals(current, pending.pending!!.candidate)
        assertEquals(initial.activation, pending.activation)
        assertEquals(current, core.decide(pending, stable()).intents.single().candidate)
        val otherReceipt = initial.copy(activation = initial.activation.copy(active = retained.copy(importReceiptSha256 = "a".repeat(64))))
        assertEquals(current, core.decide(otherReceipt, death()).state.pending!!.candidate)
    }
    @Test fun skinOnlyRebindAndForgedIssuedIdentityStayFailClosed() {
        val pending = core.decide(state(), death()).state
        val next = SkinBindingToken("skin-2")
        val rebound = core.decide(pending, RotationEvent.Rebind(hero, next)).state
        unchanged(rebound, death(2uL, hero, next)); unchanged(rebound, stable())
        val issued = core.decide(rebound, stable(s = next)).state
        val intent = issued.pending!!.issuedIntent!!
        for (bad in listOf(intent.copy(epoch = DeathEpoch(2uL)), intent.copy(candidate = descriptor("c")),
            intent.copy(prior = intent.prior.copy(skinStamp = 8)), intent.copy(hero = HeroBindingToken("")))) {
            val forged = issued.copy(pending = issued.pending!!.copy(issuedIntent = bad))
            assertEquals("invalid-state", core.decide(forged, stable(s = next)).diagnosis)
            unchanged(forged, RotationEvent.Rebind(hero, skin))
        }
        val last = core.decide(issued, RotationEvent.Rebind(hero, skin)).state
        unchanged(last, stable()); unchanged(last, death(2uL)); assertEquals(intent, last.pending!!.issuedIntent)
        val maximumStamp = state().copy(activation = state().activation.copy(skinStamp = Long.MAX_VALUE))
        assertEquals(Long.MAX_VALUE, core.decide(core.decide(maximumStamp, death()).state, stable()).intents.single().prior.skinStamp)
    }
    @Test fun highWaterNeverWrapsAndForgedSnapshotsFailClosed() {
        val s = state().copy(epochHighWater = DeathEpoch(ULong.MAX_VALUE))
        unchanged(s, death()); unchanged(s, death(ULong.MAX_VALUE)); unchanged(s, death(0uL))
        val max = core.decide(state(), death(ULong.MAX_VALUE)).state
        assertEquals(DeathEpoch(ULong.MAX_VALUE), max.pending!!.epoch)
        assertEquals(1, core.decide(max, stable(ULong.MAX_VALUE)).intents.size)
        val pending = core.decide(state(), death()).state
        for (bad in listOf(state().copy(currentSkin = null), state().copy(activation = state().activation.copy(skinStamp = -1)),
            pending.copy(epochHighWater = DeathEpoch(0uL)), pending.copy(pending = pending.pending!!.copy(candidate = descriptor("missing"))),
            pending.copy(pending = pending.pending!!.copy(phase = RotationPendingPhase.INTENT_ISSUED)),
            pending.copy(activation = pending.activation.copy(mode = SkinMode.ON)))) {
            unchanged(bad, stable()); unchanged(bad, RotationEvent.Rebind(hero, skin))
        }
    }
}
