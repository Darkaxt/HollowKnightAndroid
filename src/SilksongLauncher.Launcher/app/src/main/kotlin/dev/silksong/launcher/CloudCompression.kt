// CloudCompression — match the wire format Steam Cloud uses for save
// files. We mirror SilksongLauncher's .NET `CloudCompression` 1:1 so
// pushes from the Android launcher remain compatible with the desktop
// client and with Silksong's own writes on Windows/Linux/macOS.
//
// On the wire, Steam Cloud accepts either:
//   - raw bytes (small files where ZIP overhead would inflate them)
//   - a single-entry ZIP archive named exactly "data" with the raw
//     bytes inside that entry
//
// Steam decides which it served on the way out via a heuristic on the
// recipient side ("file starts with PK\x03\x04 magic"); we do the same
// on download in SteamCloudClient. On upload, we just pick whichever
// produces fewer bytes — anything else risks tripping a server-side
// size limit on free-tier accounts.

package dev.silksong.launcher

import java.io.ByteArrayOutputStream
import java.util.zip.ZipEntry
import java.util.zip.ZipOutputStream

object CloudCompression {

    /**
     * Returns the bytes to actually upload + whether they were compressed.
     * If ZIP overhead would make the archive ≥ the raw input we skip
     * compression — matching what the .NET launcher does so the same
     * file produced by either client lands on the cloud as the same
     * blob.
     */
    fun compress(raw: ByteArray): Pair<ByteArray, Boolean> {
        val buffer = ByteArrayOutputStream(raw.size + 256)
        ZipOutputStream(buffer).use { zip ->
            // setLevel(BEST_COMPRESSION) matches CompressionLevel.Optimal
            // on .NET; default level produces slightly larger archives.
            zip.setLevel(java.util.zip.Deflater.BEST_COMPRESSION)
            val entry = ZipEntry("data")
            zip.putNextEntry(entry)
            zip.write(raw)
            zip.closeEntry()
        }

        val zipped = buffer.toByteArray()
        return if (zipped.size >= raw.size) raw to false else zipped to true
    }
}
