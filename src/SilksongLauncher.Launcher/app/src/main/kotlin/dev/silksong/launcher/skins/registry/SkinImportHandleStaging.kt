package dev.silksong.launcher.skins.registry

import dev.silksong.launcher.skins.contracts.SkinLimits
import dev.silksong.launcher.skins.contracts.SkinNodeIdentity
import dev.silksong.launcher.skins.storage.SkinFileSystem
import dev.silksong.launcher.skins.storage.SkinPaths
import dev.silksong.launcher.skins.storage.exists
import dev.silksong.launcher.skins.storage.isDirectory
import dev.silksong.launcher.skins.storage.isRegularFile
import dev.silksong.launcher.skins.storage.isSymbolicLink
import dev.silksong.launcher.skins.storage.listBounded
import dev.silksong.launcher.skins.storage.requireContained
import java.io.File
import java.util.UUID

/** Bounded evidence and layered deletion for process-only import handle staging. */
internal class SkinImportHandleStaging(
    private val paths: SkinPaths,
    private val fs: SkinFileSystem,
    private val limits: SkinLimits = SkinLimits.V1,
) {
    fun recoverOrphans(): Int {
        ensureLayout()
        val rootIdentity = fs.identity(paths.importHandles)
        val children = fs.listBounded(paths.importHandles, limits.candidates).sortedBy(File::getName)
        val plans = children.map(::inspectHandle)
        val confirmedChildren = fs.listBounded(paths.importHandles, limits.candidates).sortedBy(File::getName)
        require(children.map(File::getAbsolutePath) == confirmedChildren.map(File::getAbsolutePath)) {
            "Import handle orphan membership changed"
        }
        val confirmedPlans = confirmedChildren.map(::inspectHandle)
        require(plans == confirmedPlans) { "Import handle orphan evidence changed" }
        require(fs.identity(paths.importHandles) == rootIdentity) { "Import handle orphan membership changed" }
        requireStable(plans)
        plans.forEach(::deletePlan)
        return plans.size
    }

    fun createOwner(handleId: UUID): File {
        ensureLayout()
        // Include failed-cleanup leftovers: every admitted disk owner must fit process recovery's bound.
        require(fs.listBounded(paths.importHandles, limits.candidates).size < limits.candidates) {
            "Import handle owner bound exceeded"
        }
        val owner = paths.importHandleOwner(handleId).absoluteFile.normalize()
        fs.requireContained(owner, paths.importHandles, allowMissingLeaf = true)
        require(!fs.exists(owner)) { "Random handle UUID already exists" }
        fs.createDirectory(owner)
        fs.requireContained(owner, paths.importHandles)
        requireDirectory(owner, paths.importHandles, "Import handle owner")
        fs.syncDirectory(owner)
        fs.syncDirectory(paths.importHandles)
        return owner
    }

    fun measureRegularFiles(owner: File): List<Long> {
        val plan = inspectHandle(owner)
        require(plan == inspectHandle(owner)) { "Prepared handle evidence changed while measured" }
        requireStable(listOf(plan))
        val archive = plan.quarantine?.archive?.file
        return plan.allEvidence.asSequence()
            .filter { it.identity.regularFile && it.file != archive }
            .map { it.identity.size }
            .toMutableList()
            .also { if (it.isEmpty()) it += 0L }
    }

    fun cleanup(owner: File) {
        ensureLayout()
        val normalized = canonicalOwner(owner, allowMissing = true)
        if (!fs.exists(normalized)) {
            fs.syncDirectory(paths.importHandles)
            return
        }
        val plan = inspectHandle(normalized)
        require(plan == inspectHandle(normalized)) { "Import handle cleanup evidence changed" }
        requireStable(listOf(plan))
        deletePlan(plan)
    }

    private fun inspectHandle(owner: File): HandlePlan {
        val normalized = canonicalOwner(owner, allowMissing = false)
        requireDirectory(normalized, paths.importHandles, "Import handle orphan")
        val ownerEvidence = evidence(normalized)
        val ownerChildren = directChildren(normalized, 1)
        if (ownerChildren.isEmpty()) return HandlePlan(ownerEvidence, null)

        val quarantine = ownerChildren.single()
        require(QUARANTINE_NAME.matches(quarantine.name)) { "Import handle content name is unknown" }
        requireDirectory(quarantine, normalized, "Import handle quarantine")
        val quarantineEvidence = evidence(quarantine)
        val quarantineChildren = directChildren(quarantine, 2)
        var archiveEvidence: NodeEvidence? = null
        var normalizedRoot: File? = null
        quarantineChildren.forEach { child ->
            when {
                child.name == ARCHIVE -> {
                    require(archiveEvidence == null) { "Import handle has duplicate archive evidence" }
                    requireRegular(child, quarantine, "Import handle archive")
                    archiveEvidence = evidence(child)
                }
                NORMALIZED_NAME.matches(child.name) -> {
                    require(normalizedRoot == null) { "Import handle has multiple normalization roots" }
                    requireDirectory(child, quarantine, "Import handle normalization root")
                    normalizedRoot = child
                }
                else -> error("Import handle quarantine content name is unknown")
            }
        }
        val normalization = normalizedRoot?.let(::inspectNormalization)
        return HandlePlan(
            ownerEvidence,
            QuarantinePlan(quarantineEvidence, archiveEvidence, normalization),
        )
    }

    private fun inspectNormalization(root: File): NormalizationPlan {
        val rootEvidence = evidence(root)
        val children = directChildren(root, limits.candidates + MAX_EPHEMERAL_ROOTS)
        val candidateIndexes = linkedSetOf<Int>()
        var objectRoots = 0
        var receiptRoots = 0
        val layers = children.map { child ->
            requireDirectory(child, root, "Normalization child")
            when {
                CANDIDATE_NAME.matches(child.name) -> {
                    val index = child.name.removePrefix("candidate-").toInt()
                    require(index in 0 until limits.candidates && candidateIndexes.add(index)) {
                        "Normalization candidate name is out of bounds or duplicated"
                    }
                }
                child.name.startsWith(OBJECT_PREFIX) && SAFE_EPHEMERAL_NAME.matches(child.name) -> {
                    objectRoots++
                    require(objectRoots <= 1) { "Normalization has multiple ephemeral object roots" }
                }
                child.name.startsWith(RECEIPT_PREFIX) && SAFE_EPHEMERAL_NAME.matches(child.name) -> {
                    receiptRoots++
                    require(receiptRoots <= 1) { "Normalization has multiple ephemeral receipt roots" }
                }
                else -> error("Normalization child name is unknown")
            }
            inspectLayer(child, root)
        }
        require(candidateIndexes.size <= limits.candidates) { "Normalization candidate bound exceeded" }
        return NormalizationPlan(rootEvidence, layers)
    }

    private fun inspectLayer(root: File, owner: File): LayerPlan {
        requireDirectory(root, owner, "Import handle candidate layer")
        val identities = linkedSetOf<String>()
        val evidence = mutableListOf<NodeEvidence>()
        val rootEvidence = evidence(root)
        identities += rootEvidence.identity.fileKey
        evidence += rootEvidence
        val queue = ArrayDeque<File>()
        queue.addLast(root)
        var observed = 0
        while (queue.isNotEmpty()) {
            val directory = queue.removeFirst()
            val remaining = limits.observedNodes - observed
            val children = directChildren(directory, remaining)
            for (child in children) {
                observed++
                require(observed <= limits.observedNodes) { "Import handle candidate layer exceeds deletion bound" }
                require(child.name != COMPLETE) { "Completed staging is not an import orphan" }
                require(!fs.isSymbolicLink(child)) { "Import handle candidate layer contains an alias" }
                val childEvidence = evidence(child)
                require(identities.add(childEvidence.identity.fileKey)) {
                    "Import handle candidate layer contains duplicate node identity"
                }
                evidence += childEvidence
                when {
                    fs.isDirectory(child) -> queue.addLast(child)
                    fs.isRegularFile(child) -> require(childEvidence.identity.regularFile) {
                        "Import handle candidate file type evidence disagrees"
                    }
                    else -> error("Import handle candidate layer contains an unsupported node")
                }
            }
        }
        return LayerPlan(rootEvidence, evidence)
    }

    private fun requireStable(plans: List<HandlePlan>) {
        val identities = linkedSetOf<String>()
        plans.flatMap(HandlePlan::allEvidence).forEach { node ->
            require(node.file.name != COMPLETE) { "Completed staging is not an import orphan" }
            require(!fs.isSymbolicLink(node.file)) { "Import handle evidence became aliased" }
            require(fs.identity(node.file) == node.identity) { "Import handle evidence identity changed" }
            require(identities.add(node.identity.fileKey)) { "Import handle evidence contains duplicate node identity" }
        }
    }

    private fun deletePlan(plan: HandlePlan) {
        val quarantine = plan.quarantine
        if (quarantine != null) {
            val normalization = quarantine.normalization
            if (normalization != null) {
                normalization.layers.forEach { layer ->
                    requireLayerStable(layer)
                    fs.deleteContained(layer.root.file, normalization.root.file)
                    fs.syncDirectory(normalization.root.file)
                }
                require(fs.listBounded(normalization.root.file, 0).isEmpty()) {
                    "Normalization root is not empty after layered deletion"
                }
                fs.deleteContained(normalization.root.file, quarantine.root.file)
                fs.syncDirectory(quarantine.root.file)
            }
            quarantine.archive?.let { archive ->
                require(fs.identity(archive.file) == archive.identity) { "Import archive identity changed before deletion" }
                fs.deleteContained(archive.file, quarantine.root.file)
                fs.syncDirectory(quarantine.root.file)
            }
            require(fs.listBounded(quarantine.root.file, 0).isEmpty()) {
                "Quarantine root is not empty after layered deletion"
            }
            fs.deleteContained(quarantine.root.file, plan.owner.file)
            fs.syncDirectory(plan.owner.file)
        }
        require(fs.listBounded(plan.owner.file, 0).isEmpty()) { "Import handle owner is not empty after cleanup" }
        fs.deleteContained(plan.owner.file, paths.importHandles)
        fs.syncDirectory(paths.importHandles)
    }

    private fun requireLayerStable(layer: LayerPlan) {
        layer.evidence.forEach { node ->
            require(fs.identity(node.file) == node.identity && !fs.isSymbolicLink(node.file)) {
                "Import handle candidate evidence changed before deletion"
            }
        }
    }

    private fun directChildren(directory: File, maximum: Int): List<File> {
        require(maximum >= 0) { "Negative import handle listing bound" }
        val normalizedParent = directory.absoluteFile.normalize()
        return fs.listBounded(normalizedParent, maximum).sortedBy(File::getName).also { children ->
            require(children.map(File::getName).toSet().size == children.size) {
                "Import handle directory contains duplicate child names"
            }
            children.forEach { child ->
                val normalized = child.absoluteFile.normalize()
                require(normalized.parentFile == normalizedParent) { "Import handle child escapes its exact parent" }
                fs.requireContained(normalized, paths.importHandles)
                require(child.name != COMPLETE) { "Completed staging is not an import orphan" }
                require(!fs.isSymbolicLink(normalized)) { "Import handle child is aliased" }
            }
        }
    }

    private fun canonicalOwner(owner: File, allowMissing: Boolean): File {
        val normalized = owner.absoluteFile.normalize()
        val parsed = runCatching { UUID.fromString(normalized.name) }.getOrNull()
        require(normalized.parentFile == paths.importHandles.absoluteFile.normalize() && parsed?.toString() == normalized.name) {
            "Import handle owner name is not canonical"
        }
        fs.requireContained(normalized, paths.importHandles, allowMissingLeaf = allowMissing)
        return normalized
    }

    private fun requireDirectory(directory: File, owner: File, label: String) {
        fs.requireContained(directory, owner)
        require(fs.isDirectory(directory) && !fs.isSymbolicLink(directory)) { "$label is not a no-alias directory" }
        require(!fs.identity(directory).regularFile) { "$label type evidence disagrees" }
    }

    private fun requireRegular(file: File, owner: File, label: String) {
        fs.requireContained(file, owner)
        require(fs.isRegularFile(file) && !fs.isSymbolicLink(file)) { "$label is not a no-alias regular file" }
        require(fs.identity(file).regularFile) { "$label type evidence disagrees" }
    }

    private fun evidence(file: File): NodeEvidence = NodeEvidence(file.absoluteFile.normalize(), fs.identity(file)).also {
        require(it.identity.fileKey.isNotBlank() && it.identity.size >= 0L) { "Import handle identity evidence is unavailable" }
    }

    private fun ensureLayout() {
        ensureDirectory(paths.staging, paths.root)
        ensureDirectory(paths.importHandles, paths.staging)
    }

    private fun ensureDirectory(directory: File, parent: File) {
        fs.requireContained(parent, paths.root)
        fs.requireContained(directory, parent, allowMissingLeaf = true)
        if (!fs.exists(directory)) {
            fs.createDirectory(directory)
            fs.syncDirectory(directory)
            fs.syncDirectory(parent)
        }
        fs.requireContained(directory, parent)
        require(fs.isDirectory(directory) && !fs.isSymbolicLink(directory)) { "Import handle layout is unsafe" }
    }

    private data class NodeEvidence(val file: File, val identity: SkinNodeIdentity)

    private data class LayerPlan(
        val root: NodeEvidence,
        val evidence: List<NodeEvidence>,
    )

    private data class NormalizationPlan(
        val root: NodeEvidence,
        val layers: List<LayerPlan>,
    ) {
        val allEvidence: List<NodeEvidence> get() = listOf(root) + layers.flatMap(LayerPlan::evidence)
    }

    private data class QuarantinePlan(
        val root: NodeEvidence,
        val archive: NodeEvidence?,
        val normalization: NormalizationPlan?,
    ) {
        val allEvidence: List<NodeEvidence>
            get() = listOf(root) + listOfNotNull(archive) + normalization?.allEvidence.orEmpty()
    }

    private data class HandlePlan(
        val owner: NodeEvidence,
        val quarantine: QuarantinePlan?,
    ) {
        val allEvidence: List<NodeEvidence> get() = listOf(owner) + quarantine?.allEvidence.orEmpty()
    }

    private companion object {
        const val ARCHIVE = "archive"
        const val COMPLETE = ".complete"
        const val OBJECT_PREFIX = "object-"
        const val RECEIPT_PREFIX = "receipt-"
        const val MAX_EPHEMERAL_ROOTS = 2
        val QUARANTINE_NAME = Regex("quarantine-[A-Za-z0-9-]{1,160}")
        val NORMALIZED_NAME = Regex("normalized-[0-9]{1,20}")
        val CANDIDATE_NAME = Regex("candidate-[0-9]{3}")
        val SAFE_EPHEMERAL_NAME = Regex("[A-Za-z0-9._-]{1,255}")
    }
}
