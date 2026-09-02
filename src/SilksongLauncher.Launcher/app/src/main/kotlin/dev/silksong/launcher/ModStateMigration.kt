package dev.silksong.launcher

import java.io.File
import java.io.IOException
import java.nio.file.FileAlreadyExistsException
import java.nio.file.Files
import java.util.ArrayDeque

/** Copies legacy shared mutable mod state into one profile exactly once. */
internal object ModStateMigration {
    private const val MARKER = ".legacy-mod-state-migrated-v1"
    private const val WORK = ".legacy-mod-state-v1-work"

    private data class Work(val source: File, val relative: File, val depth: Int)

    @Synchronized
    fun migrate(mods: File, state: File) {
        val marker = File(state, MARKER)
        if (marker.isFile) return
        if (!state.exists() && !state.mkdirs()) throw IOException("could not create profile mod state")
        if (!state.isDirectory || Files.isSymbolicLink(state.toPath())) {
            throw IOException("profile mod state is not a regular directory")
        }

        val markerPart = File(state, "$MARKER.part")
        if (markerPart.exists() && !markerPart.delete()) {
            throw IOException("could not clear interrupted mod-state marker")
        }
        val work = File(state, WORK)
        if (!ModImport.deleteOwnedTree(work)) throw IOException("could not clear interrupted mod-state migration")
        if (!work.mkdir()) throw IOException("could not create mod-state migration storage")

        try {
            copyMissingTree(File(mods, "config"), File(state, "config"), File(work, "config"))
            copyMissingFile(File(mods, "disabled.txt"), File(state, "disabled.txt"), File(work, "disabled.txt"))
            copyMissingFile(
                File(mods, "disabled-assemblies.txt"),
                File(state, "disabled-assemblies.txt"),
                File(work, "disabled-assemblies.txt"),
            )
            if (!ModImport.deleteOwnedTree(work)) throw IOException("could not clean mod-state migration storage")
            markerPart.writeText("completed\n")
            moveNoReplace(markerPart, marker)
            if (!marker.isFile || markerPart.exists()) throw IOException("could not publish mod-state migration marker")
        } catch (failure: Throwable) {
            if (!ModImport.deleteOwnedTree(work)) {
                throw IOException("mod-state migration failed and cleanup could not be verified", failure)
            }
            throw failure
        }
    }

    private fun copyMissingTree(sourceRoot: File, targetRoot: File, workRoot: File) {
        if (!sourceRoot.exists()) return
        if (!sourceRoot.isDirectory || Files.isSymbolicLink(sourceRoot.toPath())) {
            throw IOException("legacy mod config is not a regular directory")
        }
        if (!targetRoot.exists() && !targetRoot.mkdirs()) {
            throw IOException("could not create profile config directory")
        }
        if (!targetRoot.isDirectory || Files.isSymbolicLink(targetRoot.toPath())) {
            throw IOException("profile config path is not a regular directory")
        }
        if (!workRoot.mkdirs() && !workRoot.isDirectory) throw IOException("could not stage legacy mod config")

        val canonicalSource = sourceRoot.canonicalFile
        val seen = linkedSetOf(canonicalSource.path)
        val budget = ModImport.StreamBudget()
        budget.visit(0)
        val pending = ArrayDeque<Work>()
        pending.add(Work(canonicalSource, File(""), 0))
        while (pending.isNotEmpty()) {
            val current = pending.removeFirst()
            val children = current.source.listFiles() ?: throw IOException("could not read legacy mod config")
            val names = HashSet<String>()
            for (child in children.sortedBy { it.name }) {
                val component = ModImport.validateComponent(child.name)
                if (!names.add(ModImport.collisionKey(component))) {
                    throw IOException("legacy mod config contains colliding names")
                }
                val depth = current.depth + 1
                budget.visit(depth)
                val source = ModImport.requireUniqueDirectPath(canonicalSource, child, seen)
                val relative = if (current.relative.path.isEmpty()) File(component) else File(current.relative, component)
                val target = File(targetRoot, relative.path).canonicalFile
                val staged = File(workRoot, relative.path).canonicalFile
                requireContained(target, targetRoot)
                requireContained(staged, workRoot)
                if (source.isDirectory) {
                    if (!target.exists() && !target.mkdirs()) {
                        throw IOException("could not create profile config directory")
                    }
                    if (!target.isDirectory || Files.isSymbolicLink(target.toPath())) {
                        throw IOException("legacy config directory collides with profile state: $relative")
                    }
                    if (!staged.mkdirs() && !staged.isDirectory) {
                        throw IOException("could not stage profile config directory")
                    }
                    pending.add(Work(source, relative, depth))
                } else if (source.isFile) {
                    copyMissingFile(source, target, staged, budget)
                } else {
                    throw IOException("legacy mod config contains an unsupported entry")
                }
            }
        }
    }

    private fun copyMissingFile(
        source: File,
        target: File,
        staged: File,
        budget: ModImport.StreamBudget = ModImport.StreamBudget(),
    ) {
        if (!source.exists()) return
        if (!source.isFile || Files.isSymbolicLink(source.toPath())) {
            throw IOException("legacy mod state is not a regular file: ${source.name}")
        }
        if (target.exists()) {
            if (!target.isFile || Files.isSymbolicLink(target.toPath())) {
                throw IOException("legacy mod-state file collides with profile state: ${target.name}")
            }
            return
        }
        staged.parentFile?.mkdirs()
        source.inputStream().use { input ->
            staged.outputStream().use { output -> ModImport.copyChecked(input, output, source.length(), budget) }
        }
        target.parentFile?.mkdirs()
        if (target.exists()) {
            if (!target.isFile || Files.isSymbolicLink(target.toPath())) {
                throw IOException("legacy mod-state file collides with profile state: ${target.name}")
            }
            if (!staged.delete()) throw IOException("could not discard superseded migrated state")
            return
        }
        moveNoReplace(staged, target)
        if (!target.isFile || staged.exists()) throw IOException("could not publish migrated state at $target")
    }

    private fun moveNoReplace(source: File, target: File) {
        try {
            // Both paths are profile-state siblings on one filesystem. The
            // no-replace move is atomic on the supported Android filesystems
            // and, unlike ATOMIC_MOVE, has defined behavior when target exists.
            Files.move(source.toPath(), target.toPath())
        } catch (_: FileAlreadyExistsException) {
            if (!source.delete()) throw IOException("could not discard migration staging")
        }
    }

    private fun requireContained(candidate: File, owner: File) {
        val root = owner.canonicalFile.path.trimEnd(File.separatorChar) + File.separator
        if (!candidate.canonicalPath.startsWith(root)) throw IOException("mod-state migration path escapes its owner")
    }
}
