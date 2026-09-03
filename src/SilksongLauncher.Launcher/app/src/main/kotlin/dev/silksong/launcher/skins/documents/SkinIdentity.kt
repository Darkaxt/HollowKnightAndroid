package dev.silksong.launcher.skins.documents

import dev.silksong.launcher.skins.contracts.StagedPayload
import java.io.File
import java.security.MessageDigest

object SkinIdentity {
    private val HEX = Regex("[0-9a-f]{64}")
    private const val BASE32 = "abcdefghijklmnopqrstuvwxyz234567"

    fun candidateKey(archiveSha256: String, rawCandidatePath: ByteArray, layoutCode: Int): String {
        require(HEX.matches(archiveSha256)) { "Invalid archive SHA-256" }
        require(rawCandidatePath.size <= 512) { "Candidate path is too long" }
        require(layoutCode in 0..3) { "Invalid layout code" }
        val digest = MessageDigest.getInstance("SHA-256")
        digest.update("HKS-CANDIDATE-V1\u0000".toByteArray(Charsets.US_ASCII))
        digest.update(archiveSha256.hexBytes())
        digest.update(u32(rawCandidatePath.size.toLong()))
        digest.update(rawCandidatePath)
        digest.update(layoutCode.toByte())
        return digest.digest().toHex()
    }

    fun derivedId(candidateKey: String): String {
        require(HEX.matches(candidateKey)) { "Invalid candidate key" }
        return "local-${candidateKey.take(58)}"
    }

    fun contentSha256(payloads: List<StagedPayload>): String = framedSha256(
        "HKS-PAYLOAD-V1\u0000",
        payloads.map { SkinFileDocument(it.relativePath, it.length, it.sha256) },
    )

    fun treeSha256(files: List<SkinFileDocument>): String = framedSha256("HKS-TREE-V1\u0000", files)

    fun sha256(bytes: ByteArray): String = MessageDigest.getInstance("SHA-256").digest(bytes).toHex()

    fun sha256(file: File): String {
        val digest = MessageDigest.getInstance("SHA-256")
        file.inputStream().use { input ->
            val buffer = ByteArray(64 * 1024)
            while (true) {
                val count = input.read(buffer)
                if (count < 0) break
                digest.update(buffer, 0, count)
            }
        }
        return digest.digest().toHex()
    }

    fun base32Sha256(bytes: ByteArray): String = base32(MessageDigest.getInstance("SHA-256").digest(bytes))

    fun base32DigestHex(sha256: String): String {
        require(HEX.matches(sha256)) { "Invalid SHA-256" }
        return base32(sha256.hexBytes())
    }

    private fun framedSha256(domain: String, files: List<SkinFileDocument>): String {
        val digest = MessageDigest.getInstance("SHA-256")
        digest.update(domain.toByteArray(Charsets.US_ASCII))
        val sorted = files.distinctBy { it.path }.sortedWith { left, right -> unsignedUtf8Compare(left.path, right.path) }
        require(sorted.size == files.size) { "Identity paths must be unique" }
        for (file in sorted) {
            require(file.length >= 0 && HEX.matches(file.sha256)) { "Invalid identity row" }
            val path = file.path.toByteArray(Charsets.UTF_8)
            digest.update(u32(path.size.toLong()))
            digest.update(path)
            digest.update(u64(file.length))
            digest.update(file.sha256.hexBytes())
        }
        return digest.digest().toHex()
    }

    fun unsignedUtf8Compare(left: String, right: String): Int = unsignedBytesCompare(
        left.toByteArray(Charsets.UTF_8),
        right.toByteArray(Charsets.UTF_8),
    )

    fun unsignedBytesCompare(left: ByteArray, right: ByteArray): Int {
        val common = minOf(left.size, right.size)
        for (index in 0 until common) {
            val comparison = (left[index].toInt() and 0xff).compareTo(right[index].toInt() and 0xff)
            if (comparison != 0) return comparison
        }
        return left.size.compareTo(right.size)
    }

    private fun base32(bytes: ByteArray): String {
        val output = StringBuilder((bytes.size * 8 + 4) / 5)
        var accumulator = 0
        var bits = 0
        for (byte in bytes) {
            accumulator = (accumulator shl 8) or (byte.toInt() and 0xff)
            bits += 8
            while (bits >= 5) {
                bits -= 5
                output.append(BASE32[(accumulator ushr bits) and 31])
            }
        }
        if (bits > 0) output.append(BASE32[(accumulator shl (5 - bits)) and 31])
        return output.toString()
    }

    private fun String.hexBytes(): ByteArray = ByteArray(length / 2) { index ->
        substring(index * 2, index * 2 + 2).toInt(16).toByte()
    }

    private fun ByteArray.toHex(): String = joinToString("") { "%02x".format(it.toInt() and 0xff) }

    private fun u32(value: Long): ByteArray = byteArrayOf(
        (value ushr 24).toByte(),
        (value ushr 16).toByte(),
        (value ushr 8).toByte(),
        value.toByte(),
    )

    private fun u64(value: Long): ByteArray = ByteArray(8) { index ->
        (value ushr (56 - index * 8)).toByte()
    }
}
