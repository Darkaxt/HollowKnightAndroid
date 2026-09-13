package dev.silksong.launcher.builtinmods

import java.io.File
import java.nio.file.Files
import java.nio.file.StandardCopyOption.ATOMIC_MOVE
import java.nio.file.StandardCopyOption.REPLACE_EXISTING

/** Shared line format: sorted UTF-8 key=value lines, atomically replaced as one file. */
class LineModStateStore(private val file: File) {
    fun read(): Map<String, String> {
        if (!file.isFile) return emptyMap()
        return runCatching {
            val result = linkedMapOf<String, String>()
            file.readLines(Charsets.UTF_8).forEach { line ->
                val separator = line.indexOf('=')
                require(separator > 0 && separator < line.lastIndex)
                val key = line.substring(0, separator)
                val value = line.substring(separator + 1)
                require(key.none { it == '\n' || it == '\r' || it == '=' })
                require(value.none { it == '\n' || it == '\r' || it == '=' })
                require(result.put(key, value) == null)
            }
            result
        }.getOrDefault(emptyMap())
    }

    fun update(changes: Map<String, String>) {
        val values = read().toMutableMap().apply { putAll(changes) }
        file.parentFile?.mkdirs()
        val temp = File(file.parentFile, file.name + ".tmp")
        temp.writeText(values.toSortedMap().entries.joinToString("") { "${it.key}=${it.value}\n" }, Charsets.UTF_8)
        runCatching { Files.move(temp.toPath(), file.toPath(), ATOMIC_MOVE, REPLACE_EXISTING) }
            .recoverCatching { Files.move(temp.toPath(), file.toPath(), REPLACE_EXISTING) }
            .getOrElse {
                temp.delete()
                throw IllegalStateException("Could not publish Mods state", it)
            }
    }
}
