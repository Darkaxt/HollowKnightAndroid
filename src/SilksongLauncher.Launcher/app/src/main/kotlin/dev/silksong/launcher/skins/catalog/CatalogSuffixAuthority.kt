package dev.silksong.launcher.skins.catalog

internal object CatalogSuffixAuthority {
    fun shortestUnique(paths: List<String>): Map<String, String> {
        val normalized = paths.associateWith(::components)
        val suffixes = linkedMapOf<String, String>()
        paths.forEach { target ->
            val targetComponents = normalized.getValue(target)
            for (length in 1..targetComponents.size) {
                val suffix = targetComponents.takeLast(length)
                if (normalized.values.count { it.endsWith(suffix) } == 1) {
                    suffixes[suffix.joinToString("/")] = target
                    break
                }
            }
        }
        return suffixes
    }

    fun normalizedPath(path: String): String = components(path).joinToString("/")

    private fun components(path: String): List<String> =
        path.split('/').mapIndexed { index, component ->
            val folded = asciiFold(component)
            if (index < path.count { it == '/' } && folded.endsWith(" data")) {
                folded.removeSuffix(" data")
            } else {
                folded
            }
        }

    private fun List<String>.endsWith(suffix: List<String>): Boolean =
        size >= suffix.size && suffix.indices.all { index -> this[size - suffix.size + index] == suffix[index] }

    private fun asciiFold(value: String): String = buildString(value.length) {
        value.forEach { append(if (it in 'A'..'Z') it + 32 else it) }
    }
}
