package dev.silksong.launcher.profiles

import dev.silksong.launcher.DepotFetcher
import java.io.File
import java.io.IOException

data class LegacySilksongAdoptionReport(
    val moved: Set<String>,
    val conflicts: Set<String>,
)

/** Moves only generated legacy Silksong state into its profile namespace. */
object LegacySilksongAdopter {
    private const val ADOPTION_MARKER = ".legacy-adopted-v1"

    fun adopt(
        filesDir: File,
        externalFilesDir: File,
        paths: ProfileBuildPaths,
    ): LegacySilksongAdoptionReport {
        require(paths.profile.id == "silksong") {
            "Legacy state belongs only to the Silksong profile"
        }
        val adoptionMarker = File(paths.profilePaths.root, ADOPTION_MARKER)
        if (adoptionMarker.isFile) {
            return LegacySilksongAdoptionReport(emptySet(), emptySet())
        }

        val moved = linkedSetOf<String>()
        val conflicts = linkedSetOf<String>()
        val generated = listOf(
            Triple("pkg", File(filesDir, "pkg"), paths.packageDir),
            Triple("build", File(externalFilesDir, "build"), paths.buildRoot),
            Triple("staging", File(externalFilesDir, "staging"), paths.installStaging),
            Triple("depot-staging", File(externalFilesDir, "depot-staging"), paths.depotStaging),
        )

        for ((name, source, target) in generated) {
            if (!source.exists()) continue
            if (target.exists()) {
                conflicts += name
                continue
            }
            target.parentFile?.mkdirs()
            if (!source.renameTo(target)) {
                throw IOException("Could not adopt legacy $name from $source to $target")
            }
            moved += name
        }

        importPointer(
            legacy = File(externalFilesDir, "depot-path.txt"),
            target = paths.profilePaths.sourcePointer,
        )
        if (!paths.profilePaths.sourcePointer.isFile) {
            val legacyDepot = File(externalFilesDir, "depot")
            if (DepotFetcher.isPresent(paths.profile, legacyDepot)) {
                writePointer(paths.profilePaths.sourcePointer, legacyDepot.absolutePath)
            }
        }
        importPointer(
            legacy = File(externalFilesDir, "content-path.txt"),
            target = paths.contentPointer,
        )

        writePointer(adoptionMarker, "completed")

        return LegacySilksongAdoptionReport(moved, conflicts)
    }

    private fun importPointer(legacy: File, target: File) {
        if (target.isFile || !legacy.isFile) return
        val value = runCatching { legacy.readText().trim() }.getOrNull().orEmpty()
        if (value.isEmpty()) return
        writePointer(target, value)
    }

    private fun writePointer(target: File, value: String) {
        target.parentFile?.mkdirs()
        val part = File(target.parentFile, "${target.name}.part")
        part.writeText(value)
        if (!part.renameTo(target)) {
            part.delete()
            throw IOException("Could not publish adopted state at $target")
        }
    }
}
