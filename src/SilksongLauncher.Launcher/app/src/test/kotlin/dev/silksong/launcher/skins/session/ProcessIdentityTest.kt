package dev.silksong.launcher.skins.session

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class ProcessIdentityTest {
    @Test
    fun selfReturnsKnownOnlyWithExactUidPidAndStartToken() {
        val authority = AndroidProcessIdentityAuthority(
            FakePlatform(
                selfUid = 10_042,
                selfPid = 733,
                stats = listOf(ProcStatRead.Present(stat(733, "main process) name", "123456"))),
            ),
        )

        assertEquals(
            SelfIdentityResult.Known(ProcessIdentity(10_042, 733, "123456")),
            authority.self(),
        )
    }

    @Test
    fun selfReturnsUnknownForMissingInvalidOrMalformedEvidence() {
        val unavailable = listOf(
            FakePlatform(selfUid = null, selfPid = 733),
            FakePlatform(selfUid = 10_042, selfPid = 0),
        )
        unavailable.forEach { platform ->
            assertEquals(SelfIdentityResult.Unknown, AndroidProcessIdentityAuthority(platform).self())
            assertTrue(platform.readPids.isEmpty())
        }
        listOf(
            FakePlatform(selfUid = 10_042, selfPid = 733, stats = listOf(ProcStatRead.Absent)),
            FakePlatform(selfUid = 10_042, selfPid = 733, stats = listOf(ProcStatRead.Present("malformed".toByteArray()))),
            FakePlatform(selfUid = 10_042, selfPid = 733, stats = listOf(ProcStatRead.Present(stat(733, "main", "0001")))),
        ).forEach { platform ->
            assertEquals(SelfIdentityResult.Unknown, AndroidProcessIdentityAuthority(platform).self())
        }
    }

    @Test
    fun expectedOwnerClassifiesExactAliveDeadAndUnknownEvidence() {
        val owner = ProcessIdentity(10_042, 733, "123456")

        assertEquals(
            ExpectedOwnerLiveness.Alive(owner),
            AndroidProcessIdentityAuthority(
                FakePlatform(
                    uidReads = listOf(PidUidRead.Known(owner.uid), PidUidRead.Known(owner.uid)),
                    stats = listOf(ProcStatRead.Present(stat(owner.pid, "game", owner.processStartToken))),
                ),
            ).expectedOwner(owner),
        )
        assertEquals(
            ExpectedOwnerLiveness.DefinitivelyDead,
            AndroidProcessIdentityAuthority(
                FakePlatform(
                    uidReads = listOf(PidUidRead.Absent, PidUidRead.Absent),
                    stats = listOf(ProcStatRead.Absent),
                ),
            ).expectedOwner(owner),
        )
        assertEquals(
            ExpectedOwnerLiveness.Unknown,
            AndroidProcessIdentityAuthority(
                FakePlatform(
                    uidReads = listOf(PidUidRead.Known(owner.uid), PidUidRead.Known(owner.uid)),
                    stats = listOf(ProcStatRead.Present(stat(owner.pid, "game", "654321"))),
                ),
            ).expectedOwner(owner),
        )
    }

    @Test
    fun deniedMalformedPidReuseAndContradictionAreUnknown() {
        val owner = ProcessIdentity(10_042, 733, "123456")
        val cases = listOf(
            FakePlatform(uidReads = listOf(PidUidRead.Unknown), stats = listOf(ProcStatRead.Unknown)),
            FakePlatform(
                uidReads = listOf(PidUidRead.Known(owner.uid), PidUidRead.Known(owner.uid)),
                stats = listOf(ProcStatRead.Present("not a stat".toByteArray())),
            ),
            FakePlatform(
                uidReads = listOf(PidUidRead.Known(owner.uid), PidUidRead.Known(owner.uid)),
                stats = listOf(ProcStatRead.Present(stat(owner.pid, "game", "999999"))),
            ),
            FakePlatform(
                uidReads = listOf(PidUidRead.Known(owner.uid), PidUidRead.Known(owner.uid)),
                stats = listOf(ProcStatRead.Absent),
            ),
        )

        cases.forEach { authority ->
            assertEquals(ExpectedOwnerLiveness.Unknown, AndroidProcessIdentityAuthority(authority).expectedOwner(owner))
        }
        assertEquals(
            ExactProcessPresence.Unknown,
            AndroidProcessIdentityAuthority(
                FakePlatform(
                    exact = ExactProcessQuery.Present(
                        listOf(ExactProcessEvidence("com.other", "com.other:game", owner)),
                        truncated = false,
                    ),
                ),
            ).exactProcess("com.teamcherry.hollowknight", "com.teamcherry.hollowknight:game"),
        )
    }

    @Test
    fun exactProcessReturnsPresentOnlyForOneFullyVerifiedIdentity() {
        val packageName = "com.teamcherry.hollowknight"
        val processName = "$packageName:game"
        val identity = ProcessIdentity(10_042, 733, "123456")
        val platform = FakePlatform(
            uidReads = listOf(PidUidRead.Known(identity.uid), PidUidRead.Known(identity.uid)),
            stats = listOf(ProcStatRead.Present(stat(identity.pid, "game", identity.processStartToken))),
            exact = ExactProcessQuery.Present(
                listOf(ExactProcessEvidence(packageName, processName, identity)),
                truncated = false,
            ),
        )

        assertEquals(
            ExactProcessPresence.Present(identity),
            AndroidProcessIdentityAuthority(platform).exactProcess(packageName, processName),
        )
        assertEquals(listOf(identity.pid), platform.readPids)
        assertEquals(listOf(4 * 1024), platform.statBounds)
        assertEquals(listOf(Triple(packageName, processName, 2)), platform.exactQueries)
    }

    @Test
    fun exactProcessClassifiesAbsentDuplicateAndTruncatedEvidence() {
        val packageName = "com.teamcherry.hollowknight"
        val processName = "$packageName:game"
        val identity = ProcessIdentity(10_042, 733, "123456")
        val evidence = ExactProcessEvidence(packageName, processName, identity)

        assertEquals(
            ExactProcessPresence.Absent,
            AndroidProcessIdentityAuthority(FakePlatform(exact = ExactProcessQuery.Absent)).exactProcess(packageName, processName),
        )
        assertEquals(
            ExactProcessPresence.Unknown,
            AndroidProcessIdentityAuthority(
                FakePlatform(exact = ExactProcessQuery.Present(listOf(evidence, evidence), truncated = false)),
            ).exactProcess(packageName, processName),
        )
        assertEquals(
            ExactProcessPresence.Unknown,
            AndroidProcessIdentityAuthority(
                FakePlatform(exact = ExactProcessQuery.Present(listOf(evidence), truncated = true)),
            ).exactProcess(packageName, processName),
        )
    }

    @Test
    fun boundedProcStatParsingUsesTheFinalCommandParenthesis() {
        val platform = FakePlatform(
            stats = listOf(ProcStatRead.Present(stat(733, "worker ) with spaces )", "123456"))),
        )
        assertEquals(
            SelfIdentityResult.Known(ProcessIdentity(10_042, 733, "123456")),
            AndroidProcessIdentityAuthority(platform).self(),
        )
        assertEquals(listOf(4 * 1024), platform.statBounds)
        assertEquals(
            SelfIdentityResult.Unknown,
            AndroidProcessIdentityAuthority(
                FakePlatform(stats = listOf(ProcStatRead.Present(ByteArray(4 * 1024) { 'x'.code.toByte() }))),
            ).self(),
        )
    }

    @Test
    fun exactProcessRejectsUnboundedOrInvalidNamesBeforePlatformQuery() {
        val packageName = "com.${"a".repeat(251)}"
        val platform = FakePlatform(exact = ExactProcessQuery.Absent)
        val authority = AndroidProcessIdentityAuthority(platform)

        assertEquals(ExactProcessPresence.Absent, authority.exactProcess(packageName, packageName))
        listOf(
            "" to packageName,
            "com..example" to packageName,
            "com.example*" to packageName,
            packageName to "com.example:bad*",
            "com.${"a".repeat(252)}" to packageName,
        ).forEach { (invalidPackage, invalidProcess) ->
            assertEquals(ExactProcessPresence.Unknown, authority.exactProcess(invalidPackage, invalidProcess))
        }
        assertEquals(listOf(Triple(packageName, packageName, 2)), platform.exactQueries)
        assertTrue(platform.readPids.isEmpty())
    }

    @Test
    fun malformedLinuxStatFormattingFailsClosed() {
        val valid = stat(733, "worker", "123456").decodeToString()
        val malformed = listOf(
            valid.replace(") S", ")S"),
            valid.replace(") S 0", ") S x"),
            valid.replace(") S", ") ?"),
            valid.replace("\n", " malformed\n"),
        )

        malformed.forEach { value ->
            assertEquals(
                SelfIdentityResult.Unknown,
                AndroidProcessIdentityAuthority(
                    FakePlatform(stats = listOf(ProcStatRead.Present(value.toByteArray()))),
                ).self(),
            )
        }
    }

    @Test
    fun exactProcessRejectsNonLetterSegmentStartsBeforePlatformQuery() {
        val platform = FakePlatform(exact = ExactProcessQuery.Absent)
        val authority = AndroidProcessIdentityAuthority(platform)
        listOf(
            "com._invalid" to "com._invalid",
            "com.1invalid" to "com.1invalid",
            "com.example" to "com._invalid",
            "com.example" to "com.1invalid",
            "com.example" to "com.example:_invalid",
            "com.example" to "com.example:1invalid",
            "com.example" to "com.example:valid._invalid",
            "com.example" to "com.example:valid.1invalid",
        ).forEach { (packageName, processName) ->
            assertEquals(ExactProcessPresence.Unknown, authority.exactProcess(packageName, processName))
        }
        assertTrue(platform.exactQueries.isEmpty())
    }

    @Test
    fun exactProcessAllowsUnderscoresAfterLeadingLetters() {
        val packageName = "com.example.my_app"
        val processName = "com.example:worker_1"
        val platform = FakePlatform(exact = ExactProcessQuery.Absent)

        assertEquals(
            ExactProcessPresence.Absent,
            AndroidProcessIdentityAuthority(platform).exactProcess(packageName, processName),
        )
        assertEquals(listOf(Triple(packageName, processName, 2)), platform.exactQueries)
    }

    private class FakePlatform(
        private val selfUid: Int? = 10_042,
        private val selfPid: Int? = 733,
        uidReads: List<PidUidRead> = emptyList(),
        stats: List<ProcStatRead> = emptyList(),
        private val exact: ExactProcessQuery = ExactProcessQuery.Unknown,
    ) : ProcessIdentityPlatform {
        private val uidReads = ArrayDeque(uidReads)
        private val stats = ArrayDeque(stats)
        val readPids = mutableListOf<Int>()
        val statBounds = mutableListOf<Int>()
        val exactQueries = mutableListOf<Triple<String, String, Int>>()

        override fun selfUid(): Int? = selfUid
        override fun selfPid(): Int? = selfPid
        override fun uidForPid(pid: Int): PidUidRead = uidReads.removeFirstOrNull() ?: PidUidRead.Unknown

        override fun readProcStat(pid: Int, maximumBytes: Int): ProcStatRead {
            readPids += pid
            statBounds += maximumBytes
            return stats.removeFirstOrNull() ?: ProcStatRead.Unknown
        }

        override fun exactProcess(
            packageName: String,
            processName: String,
            maximumResults: Int,
        ): ExactProcessQuery {
            exactQueries += Triple(packageName, processName, maximumResults)
            return exact
        }
    }

    private fun stat(pid: Int, comm: String, startToken: String): ByteArray =
        "$pid ($comm) S ${List(18) { "0" }.joinToString(" ")} $startToken 0 0\n".toByteArray()
}
