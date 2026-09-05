package dev.silksong.launcher.skins.session

import android.os.Process
import java.io.FileInputStream
import java.io.FileNotFoundException
import java.io.IOException
import java.nio.charset.StandardCharsets

data class ProcessIdentity(val uid: Int, val pid: Int, val processStartToken: String)

sealed interface SelfIdentityResult {
    data class Known(val identity: ProcessIdentity) : SelfIdentityResult
    data object Unknown : SelfIdentityResult
}

sealed interface ExpectedOwnerLiveness {
    data class Alive(val identity: ProcessIdentity) : ExpectedOwnerLiveness
    data object DefinitivelyDead : ExpectedOwnerLiveness
    data object Unknown : ExpectedOwnerLiveness
}

sealed interface ExactProcessPresence {
    data class Present(val identity: ProcessIdentity) : ExactProcessPresence
    data object Absent : ExactProcessPresence
    data object Unknown : ExactProcessPresence
}

interface ProcessIdentityAuthority {
    fun self(): SelfIdentityResult
    fun expectedOwner(expected: ProcessIdentity): ExpectedOwnerLiveness
    fun exactProcess(packageName: String, processName: String): ExactProcessPresence
}

internal sealed interface PidUidRead {
    data class Known(val uid: Int) : PidUidRead
    data object Absent : PidUidRead
    data object Unknown : PidUidRead
}

internal sealed interface ProcStatRead {
    data class Present(val bytes: ByteArray) : ProcStatRead
    data object Absent : ProcStatRead
    data object Unknown : ProcStatRead
}

internal data class ExactProcessEvidence(
    val packageName: String,
    val processName: String,
    val identity: ProcessIdentity,
)

internal sealed interface ExactProcessQuery {
    data class Present(
        val processes: List<ExactProcessEvidence>,
        val truncated: Boolean,
    ) : ExactProcessQuery

    data object Absent : ExactProcessQuery
    data object Unknown : ExactProcessQuery
}

internal interface ProcessIdentityPlatform {
    fun selfUid(): Int?
    fun selfPid(): Int?
    fun uidForPid(pid: Int): PidUidRead
    fun readProcStat(pid: Int, maximumBytes: Int): ProcStatRead
    fun exactProcess(packageName: String, processName: String, maximumResults: Int): ExactProcessQuery
}

/** Supplies only bounded, exact package/process-name evidence to the Android adapter. */
internal fun interface ExactProcessQueryPort {
    fun exactProcess(packageName: String, processName: String, maximumResults: Int): ExactProcessQuery
}

/** Supplies exact-PID UID evidence without granting the authority process-search access. */
internal fun interface PidUidQueryPort {
    fun uidForPid(pid: Int): PidUidRead
}

class AndroidProcessIdentityAuthority internal constructor(
    private val platform: ProcessIdentityPlatform,
) : ProcessIdentityAuthority {
    internal constructor(
        exactProcessQuery: ExactProcessQueryPort,
        pidUidQuery: PidUidQueryPort,
    ) : this(AndroidProcessIdentityPlatform(exactProcessQuery, pidUidQuery))

    override fun self(): SelfIdentityResult = try {
        val uid = platform.selfUid()
        val pid = platform.selfPid()
        if (uid == null || pid == null || uid < 0 || pid <= 0) {
            SelfIdentityResult.Unknown
        } else {
            val startToken = processStartToken(pid)
            if (startToken != null && validIdentity(uid, pid, startToken)) {
                SelfIdentityResult.Known(ProcessIdentity(uid, pid, startToken))
            } else {
                SelfIdentityResult.Unknown
            }
        }
    } catch (_: Exception) {
        SelfIdentityResult.Unknown
    }

    override fun expectedOwner(expected: ProcessIdentity): ExpectedOwnerLiveness = try {
        if (!validIdentity(expected.uid, expected.pid, expected.processStartToken)) {
            ExpectedOwnerLiveness.Unknown
        } else {
            val uidBefore = platform.uidForPid(expected.pid)
            val stat = platform.readProcStat(expected.pid, MAX_PROC_STAT_BYTES)
            val uidAfter = platform.uidForPid(expected.pid)
            when {
                uidBefore == PidUidRead.Absent && stat == ProcStatRead.Absent && uidAfter == PidUidRead.Absent -> {
                    ExpectedOwnerLiveness.DefinitivelyDead
                }
                uidBefore is PidUidRead.Known && stat is ProcStatRead.Present && uidAfter is PidUidRead.Known -> {
                    val token = parseStartToken(expected.pid, stat.bytes)
                    if (uidBefore.uid == expected.uid && uidAfter.uid == expected.uid && token == expected.processStartToken) {
                        ExpectedOwnerLiveness.Alive(expected)
                    } else {
                        ExpectedOwnerLiveness.Unknown
                    }
                }
                else -> ExpectedOwnerLiveness.Unknown
            }
        }
    } catch (_: Exception) {
        ExpectedOwnerLiveness.Unknown
    }

    override fun exactProcess(packageName: String, processName: String): ExactProcessPresence = try {
        if (!validPackageName(packageName) || !validProcessName(processName)) {
            ExactProcessPresence.Unknown
        } else {
            when (val query = platform.exactProcess(packageName, processName, MAX_EXACT_PROCESS_RESULTS)) {
                ExactProcessQuery.Absent -> ExactProcessPresence.Absent
                ExactProcessQuery.Unknown -> ExactProcessPresence.Unknown
                is ExactProcessQuery.Present -> {
                    val evidence = query.processes.singleOrNull()
                    if (
                        query.truncated ||
                        evidence == null ||
                        evidence.packageName != packageName ||
                        evidence.processName != processName ||
                        !validIdentity(evidence.identity.uid, evidence.identity.pid, evidence.identity.processStartToken)
                    ) {
                        ExactProcessPresence.Unknown
                    } else {
                        when (expectedOwner(evidence.identity)) {
                            is ExpectedOwnerLiveness.Alive -> ExactProcessPresence.Present(evidence.identity)
                            ExpectedOwnerLiveness.DefinitivelyDead, ExpectedOwnerLiveness.Unknown -> ExactProcessPresence.Unknown
                        }
                    }
                }
            }
        }
    } catch (_: Exception) {
        ExactProcessPresence.Unknown
    }

    private fun processStartToken(pid: Int): String? = when (val read = platform.readProcStat(pid, MAX_PROC_STAT_BYTES)) {
        is ProcStatRead.Present -> parseStartToken(pid, read.bytes)
        ProcStatRead.Absent, ProcStatRead.Unknown -> null
    }

    private fun parseStartToken(pid: Int, bytes: ByteArray): String? {
        if (
            bytes.isEmpty() ||
            bytes.size >= MAX_PROC_STAT_BYTES ||
            bytes.any { it.toInt() != '\n'.code && it !in ASCII_STAT_BYTES }
        ) return null
        val lineBytes = if (bytes.last().toInt() == '\n'.code) bytes.copyOf(bytes.size - 1) else bytes
        if (lineBytes.isEmpty() || lineBytes.any { it.toInt() == '\n'.code }) return null
        val line = lineBytes.toString(StandardCharsets.US_ASCII)
        val opening = line.indexOf('(')
        val closing = line.lastIndexOf(')')
        if (opening <= 0 || closing <= opening || line.substring(0, opening) != "$pid ") return null
        val suffix = line.substring(closing + 1)
        if (!suffix.startsWith(' ')) return null
        val fields = suffix.drop(1).split(' ')
        if (
            fields.size <= START_TIME_INDEX ||
            fields.any(String::isEmpty) ||
            !validLinuxState(fields[0]) ||
            !fields.drop(1).all(::validSignedDecimal)
        ) return null
        return fields[START_TIME_INDEX].takeIf(::validStartToken)
    }

    private fun validLinuxState(value: String): Boolean = value.length == 1 && value[0] in LINUX_STATES

    private fun validSignedDecimal(value: String): Boolean =
        if (value.startsWith('-')) value.length > 1 && validUnsignedDecimal(value.substring(1)) else validUnsignedDecimal(value)

    private fun validUnsignedDecimal(value: String): Boolean =
        value.isNotEmpty() && value.all { it in '0'..'9' } && (value == "0" || value[0] != '0')

    private fun validPackageName(value: String): Boolean =
        value.length in 3..MAX_ANDROID_NAME_CHARS && value.split('.').let { parts ->
            parts.size >= 2 && parts.all(::validNameSegment)
        }

    private fun validProcessName(value: String): Boolean {
        if (value.length !in 3..MAX_ANDROID_NAME_CHARS) return false
        val parts = value.split(':')
        return parts.size in 1..2 && validPackageName(parts[0]) && (parts.size == 1 || validProcessSuffix(parts[1]))
    }

    private fun validProcessSuffix(value: String): Boolean =
        value.isNotEmpty() && value.split('.').all(::validNameSegment)

    private fun validNameSegment(value: String): Boolean =
        value.isNotEmpty() && value[0].isAsciiNameStart() && value.drop(1).all { it.isAsciiNamePart() }

    private fun Char.isAsciiNameStart(): Boolean = this in 'a'..'z' || this in 'A'..'Z'

    private fun Char.isAsciiNamePart(): Boolean = isAsciiNameStart() || this in '0'..'9' || this == '_'

    private fun validIdentity(uid: Int, pid: Int, startToken: String): Boolean =
        uid >= 0 && pid > 0 && validStartToken(startToken)

    private fun validStartToken(value: String): Boolean =
        value.length <= MAX_START_TOKEN_CHARS && validUnsignedDecimal(value)

    private companion object {
        const val MAX_PROC_STAT_BYTES = 4 * 1024
        const val MAX_EXACT_PROCESS_RESULTS = 2
        const val MAX_ANDROID_NAME_CHARS = 255
        const val MAX_START_TOKEN_CHARS = 20
        const val START_TIME_INDEX = 19
        val ASCII_STAT_BYTES: IntRange = 0x20..0x7e
        val LINUX_STATES = setOf('R', 'S', 'D', 'Z', 'T', 't', 'W', 'X', 'x', 'K', 'P', 'I')
    }
}

private class AndroidProcessIdentityPlatform(
    private val exactProcessQuery: ExactProcessQueryPort,
    private val pidUidQuery: PidUidQueryPort,
) : ProcessIdentityPlatform {
    override fun selfUid(): Int? = Process.myUid()

    override fun selfPid(): Int? = Process.myPid()

    override fun uidForPid(pid: Int): PidUidRead = try {
        if (pid > 0) pidUidQuery.uidForPid(pid) else PidUidRead.Unknown
    } catch (_: Exception) {
        PidUidRead.Unknown
    }

    override fun readProcStat(pid: Int, maximumBytes: Int): ProcStatRead {
        if (pid <= 0 || maximumBytes !in 1..MAX_PROC_STAT_BYTES) return ProcStatRead.Unknown
        return try {
            val bytes = FileInputStream("/proc/$pid/stat").use { input ->
                val buffer = ByteArray(maximumBytes)
                var size = 0
                while (size < buffer.size) {
                    val read = input.read(buffer, size, buffer.size - size)
                    if (read < 0) break
                    if (read == 0) return@use ByteArray(0)
                    size += read
                }
                buffer.copyOf(size)
            }
            ProcStatRead.Present(bytes)
        } catch (error: FileNotFoundException) {
            if (error.message.isPositiveAbsence()) ProcStatRead.Absent else ProcStatRead.Unknown
        } catch (_: IOException) {
            ProcStatRead.Unknown
        } catch (_: SecurityException) {
            ProcStatRead.Unknown
        }
    }

    override fun exactProcess(packageName: String, processName: String, maximumResults: Int): ExactProcessQuery = try {
        exactProcessQuery.exactProcess(packageName, processName, maximumResults)
    } catch (_: Exception) {
        ExactProcessQuery.Unknown
    }

    private fun String?.isPositiveAbsence(): Boolean =
        this?.contains("ENOENT", ignoreCase = true) == true || this?.contains("No such file", ignoreCase = true) == true

    private companion object {
        const val MAX_PROC_STAT_BYTES = 4 * 1024
    }
}
