package dev.silksong.launcher.skins.session

import dev.silksong.launcher.skins.catalog.CatalogPathSet
import dev.silksong.launcher.skins.catalog.HollowKnightCatalogPaths
import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinLimits
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.documents.SkinIdentity
import dev.silksong.launcher.skins.registry.ActiveVisual
import dev.silksong.launcher.skins.registry.ActivationSnapshot
import dev.silksong.launcher.skins.registry.InterlockState
import dev.silksong.launcher.skins.registry.RotationInterlock
import dev.silksong.launcher.skins.registry.SkinActivation
import dev.silksong.launcher.skins.registry.SkinBindingToken
import dev.silksong.launcher.skins.registry.SkinMode
import dev.silksong.launcher.skins.registry.SkinOperationKind
import java.nio.ByteBuffer
import java.nio.charset.CodingErrorAction
import java.nio.charset.StandardCharsets
import java.text.Normalizer
import java.util.TreeMap
import java.util.UUID

data class DescriptorTextureEnvelope(
    val ordinal: Int,
    val target: String,
    val sourceRelativePath: String,
    val sourceSha256: String,
    val length: Long,
)

data class DescriptorObjectEnvelope(
    val objectRoot: String,
    val receiptPath: String,
    val treeSha256: String,
    val contentSha256: String,
    val manifestSha256: String,
    val importReceiptSha256: String,
    val textures: List<DescriptorTextureEnvelope>,
)

data class DescriptorPackEnvelope(
    val id: String,
    val name: String,
    val author: String,
    val candidateKey: String,
    val rotationEligible: Boolean,
    val currentObject: DescriptorObjectEnvelope,
    val retainedActiveObject: DescriptorObjectEnvelope?,
)

data class SkinLaunchDescriptor(
    val schemaVersion: Int,
    val descriptorId: UUID,
    val sessionSequence: Long,
    val profileId: String,
    val gameVersion: String,
    val catalogId: String,
    val catalogSha256: String,
    val registryGenerationId: String,
    val registryGenerationSha256: String,
    val activation: SkinActivation,
    val packs: List<DescriptorPackEnvelope>,
    val leaseId: UUID,
    val leaseTokenSha256: String,
)

data class DescriptorExpectations(
    val descriptorId: UUID,
    val profileId: String,
    val gameVersion: String,
    val catalogId: String,
    val catalogSha256: String,
    val leaseId: UUID,
)

/** Strict canonical descriptor authority for one immutable Hollow Knight launch. */
object SkinLaunchDescriptorCodec {
    private const val SCHEMA_VERSION = 1
    private const val MAX_BYTES = 8 * 1024 * 1024
    private const val MAX_JSON_DEPTH = 32
    private const val MAX_JSON_NODES = 400_000
    private const val MAX_JSON_CONTAINER_ITEMS = 256
    internal const val PROFILE_ID = "hollow-knight"
    internal const val GAME_VERSION = "1.5.12620"
    private val SHA256 = Regex("[0-9a-f]{64}")
    private val UUID_TEXT = Regex("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}")
    private val ID = Regex("[a-z0-9](?:[a-z0-9._-]{0,62}[a-z0-9])?")
    private val DECIMAL = Regex("0|[1-9][0-9]*")
    private val FAILURE_CODE = Regex("[A-Z][A-Z0-9_]{0,127}")
    private val BIDI_CONTROLS = setOf(
        '؜', '‎', '‏', '‪', '‫', '‬', '‭', '‮',
        '⁦', '⁧', '⁨', '⁩',
    )

    fun canonical(value: SkinLaunchDescriptor): ByteArray {
        validate(value)
        return render(descriptorValue(value)).also {
            require(it.size <= MAX_BYTES) { "Launch descriptor exceeds 8 MiB" }
        }
    }

    fun parse(
        bytes: ByteArray,
        expectedSha256: String,
        expected: DescriptorExpectations,
    ): SkinResult<SkinLaunchDescriptor> = parse(bytes, expectedSha256, expected, null)

    internal fun parseAfterSnapshotForTest(
        bytes: ByteArray,
        expectedSha256: String,
        expected: DescriptorExpectations,
        afterSnapshot: () -> Unit,
    ): SkinResult<SkinLaunchDescriptor> = parse(bytes, expectedSha256, expected, afterSnapshot)

    private fun parse(
        bytes: ByteArray,
        expectedSha256: String,
        expected: DescriptorExpectations,
        afterSnapshot: (() -> Unit)?,
    ): SkinResult<SkinLaunchDescriptor> = result {
        require(bytes.size <= MAX_BYTES) { "Launch descriptor exceeds 8 MiB" }
        val snapshot = bytes.copyOf()
        afterSnapshot?.invoke()
        requireDigest(expectedSha256, "expected descriptor digest")
        require(SkinIdentity.sha256(snapshot) == expectedSha256) { "Launch descriptor digest mismatches expectation" }
        val text = StandardCharsets.UTF_8.newDecoder()
            .onMalformedInput(CodingErrorAction.REPORT)
            .onUnmappableCharacter(CodingErrorAction.REPORT)
            .decode(ByteBuffer.wrap(snapshot))
            .toString()
        require(!text.startsWith('﻿')) { "Launch descriptor BOM is forbidden" }
        val json = Parser(text).parse()
        require(!json.containsNull()) { "Launch descriptor null fields are forbidden" }
        val decoded = decodeDescriptor(json)
        validate(decoded)
        require(canonical(decoded).contentEquals(snapshot)) { "Launch descriptor is not canonical JSON" }
        require(decoded.descriptorId == expected.descriptorId) { "Launch descriptor ID mismatches expectation" }
        require(decoded.profileId == expected.profileId) { "Launch descriptor profile mismatches expectation" }
        require(decoded.gameVersion == expected.gameVersion) { "Launch descriptor game mismatches expectation" }
        require(decoded.catalogId == expected.catalogId) { "Launch descriptor catalog mismatches expectation" }
        require(decoded.catalogSha256 == expected.catalogSha256) { "Launch descriptor catalog digest mismatches expectation" }
        require(decoded.leaseId == expected.leaseId) { "Launch descriptor lease mismatches expectation" }
        decoded
    }

    private inline fun <T> result(action: () -> T): SkinResult<T> = try {
        SkinResult.Ok(action())
    } catch (error: Exception) {
        SkinResult.Error(SkinImportCode.DOCUMENT_INVALID, error.message ?: "Invalid launch descriptor")
    }

    private fun validate(value: SkinLaunchDescriptor) {
        val catalog = CatalogPathSet.requirePinned().revalidate()
        require(value.schemaVersion == SCHEMA_VERSION) { "Unknown launch descriptor schema" }
        requireUuid(value.descriptorId, "descriptor ID")
        require(value.sessionSequence >= 0L) { "Negative session sequence" }
        require(value.profileId == PROFILE_ID) { "Wrong launch descriptor profile" }
        require(value.gameVersion == GAME_VERSION) { "Wrong launch descriptor game" }
        require(value.catalogId == HollowKnightCatalogPaths.CATALOG_ID) { "Wrong launch descriptor catalog" }
        require(value.catalogSha256 == catalog.sha256) { "Wrong launch descriptor catalog digest" }
        requireUuidText(value.registryGenerationId, "registry generation ID")
        requireDigest(value.registryGenerationSha256, "registry generation digest")
        requireUuid(value.leaseId, "lease ID")
        requireDigest(value.leaseTokenSha256, "lease token digest")
        require(value.packs.size <= 64) { "Launch descriptor pack bound exceeded" }
        require(value.packs == value.packs.sortedWith(packComparator())) { "Launch descriptor packs are not in normalized name order" }

        val packById = linkedMapOf<String, DescriptorPackEnvelope>()
        val candidateOwners = linkedSetOf<String>()
        value.packs.forEach { pack ->
            require(ID.matches(pack.id) && packById.put(pack.id, pack) == null) { "Invalid or duplicate descriptor pack ID" }
            requireDisplay(pack.name, 80, "descriptor pack name")
            requireDisplay(pack.author, 80, "descriptor pack author")
            requireDigest(pack.candidateKey, "descriptor candidate key")
            require(candidateOwners.add(pack.candidateKey)) { "Duplicate descriptor candidate ownership" }
            validateObject(pack.currentObject, catalog)
            pack.retainedActiveObject?.let { retained ->
                validateObject(retained, catalog)
                require(
                    VisualIdentity(retained.treeSha256, retained.contentSha256, retained.importReceiptSha256) !=
                        VisualIdentity(
                            pack.currentObject.treeSha256,
                            pack.currentObject.contentSha256,
                            pack.currentObject.importReceiptSha256,
                        ),
                ) { "Retained active object duplicates current immutable object" }
            }
        }

        val referencedVisuals = linkedMapOf<String, MutableSet<VisualIdentity>>()
        val referencedSelections = linkedSetOf<String>()
        fun selection(id: String?) {
            if (id == null) return
            require(id in packById) { "Selected pack is absent from descriptor union" }
            referencedSelections += id
        }
        fun reference(visual: ActiveVisual) {
            if (visual !is ActiveVisual.Pack) return
            require(visual.id in packById) { "Referenced active pack is absent from descriptor union" }
            requireDigest(visual.treeSha256, "active tree digest")
            requireDigest(visual.contentSha256, "active content digest")
            requireDigest(visual.importReceiptSha256, "active receipt digest")
            referencedVisuals.getOrPut(visual.id, ::linkedSetOf) += VisualIdentity(
                visual.treeSha256,
                visual.contentSha256,
                visual.importReceiptSha256,
            )
        }

        validateActivation(value.activation, ::reference, ::selection)
        val requiredUnion = linkedSetOf<String>()
        value.packs.filter(DescriptorPackEnvelope::rotationEligible).forEach { requiredUnion += it.id }
        requiredUnion += referencedSelections
        referencedVisuals.keys.forEach(requiredUnion::add)
        require(packById.keys == requiredUnion) { "Descriptor pack union has omitted or unreferenced authority" }

        packById.forEach { (id, pack) ->
            val current = VisualIdentity(
                pack.currentObject.treeSha256,
                pack.currentObject.contentSha256,
                pack.currentObject.importReceiptSha256,
            )
            val oldReferences = referencedVisuals[id].orEmpty().filter { it != current }.toSet()
            if (oldReferences.isEmpty()) {
                require(pack.retainedActiveObject == null) { "Unreferenced retained active object" }
            } else {
                require(oldReferences.size == 1) { "More than one retained active object is required" }
                val retained = requireNotNull(pack.retainedActiveObject) { "Missing retained active object" }
                val retainedIdentity = VisualIdentity(retained.treeSha256, retained.contentSha256, retained.importReceiptSha256)
                require(retainedIdentity == oldReferences.single()) { "Retained active object mismatches active reference" }
            }
        }
    }

    private fun validateObject(value: DescriptorObjectEnvelope, catalog: CatalogPathSet) {
        requireDigest(value.treeSha256, "object tree digest")
        requireDigest(value.contentSha256, "object content digest")
        requireDigest(value.manifestSha256, "object manifest digest")
        requireDigest(value.importReceiptSha256, "object receipt digest")
        requireImmutablePath(value.objectRoot, "objects", value.treeSha256)
        requireImmutablePath(value.receiptPath, "import-receipts", value.importReceiptSha256)
        require(value.textures.isNotEmpty() && value.textures.size <= catalog.paths.size) { "Object textures are empty or exceed catalog" }
        var previousCatalogIndex = -1
        value.textures.forEach { texture ->
            val catalogIndex = catalog.paths.indexOf(texture.target)
            require(catalogIndex >= 0 && catalogIndex > previousCatalogIndex) { "Descriptor textures are not in catalog order" }
            require(texture.ordinal == catalogIndex) { "Descriptor texture ordinal differs from pinned catalog" }
            previousCatalogIndex = catalogIndex
            requireDigest(texture.sourceSha256, "descriptor texture source digest")
            require(texture.length in 1..SkinLimits.V1.textureBytes) { "Descriptor texture length is outside its bound" }
            require(texture.sourceRelativePath == "pack/assets/${SkinIdentity.base32DigestHex(texture.sourceSha256)}") {
                "Descriptor texture source path is inconsistent"
            }
        }
    }

    private fun validateActivation(
        value: SkinActivation,
        reference: (ActiveVisual) -> Unit,
        selection: (String?) -> Unit,
    ) {
        require(value.skinStamp >= 0L) { "Negative activation stamp" }
        selection(value.selectedPackId)
        reference(value.active)
        val lock = value.rotationInterlock
        val fields = listOf(
            lock.transactionId,
            lock.operation,
            lock.baseGenerationId,
            lock.baseGenerationSha256,
            lock.prior,
            lock.target,
            lock.bindingToken,
            lock.priorEstablishedOnBinding,
            lock.originalFailure,
            lock.rollbackFailure,
        )
        if (lock.state == InterlockState.CLEAR) {
            require(fields.all { it == null }) { "Clear interlock carries transaction authority" }
            return
        }
        requireUuidText(requireNotNull(lock.transactionId), "interlock transaction ID")
        require(lock.operation != null) { "Missing interlock operation" }
        requireUuidText(requireNotNull(lock.baseGenerationId), "interlock base generation ID")
        requireDigest(requireNotNull(lock.baseGenerationSha256), "interlock base generation digest")
        val prior = requireNotNull(lock.prior) { "Missing interlock prior snapshot" }
        val target = requireNotNull(lock.target) { "Missing interlock target snapshot" }
        validateSnapshot(prior, reference, selection)
        validateSnapshot(target, reference, selection)
        require(target.skinStamp == Math.addExact(prior.skinStamp, 1L)) { "Interlock target stamp is not next" }
        require(value.snapshotEquals(prior)) { "Activation differs from recorded interlock prior" }
        requireBinding(requireNotNull(lock.bindingToken).value)
        require(lock.priorEstablishedOnBinding != null) { "Missing interlock prior-binding proof" }
        when (lock.state) {
            InterlockState.ARMED -> require(lock.originalFailure == null && lock.rollbackFailure == null) {
                "Armed interlock carries failures"
            }
            InterlockState.ROLLBACK_FAILED -> {
                requireFailure(requireNotNull(lock.originalFailure))
                requireFailure(requireNotNull(lock.rollbackFailure))
            }
            InterlockState.CLEAR -> error("unreachable")
        }
    }

    private fun validateSnapshot(
        value: ActivationSnapshot,
        reference: (ActiveVisual) -> Unit,
        selection: (String?) -> Unit,
    ) {
        require(value.skinStamp >= 0L) { "Negative interlock snapshot stamp" }
        selection(value.selectedPackId)
        reference(value.active)
    }

    private fun SkinActivation.snapshotEquals(snapshot: ActivationSnapshot): Boolean =
        mode == snapshot.mode && selectedPackId == snapshot.selectedPackId && active == snapshot.active && skinStamp == snapshot.skinStamp

    private fun requireImmutablePath(path: String, owner: String, digest: String) {
        require(path.length in 1..256 && 0.toChar() !in path && '\\' !in path) { "Invalid immutable path" }
        val parts = path.split('/')
        require(parts.none { it.isEmpty() || it == "." || it == ".." || it.any(Char::isISOControl) }) { "Immutable path is unsafe" }
        require(parts == listOf(owner, "sha256", digest.take(2), digest)) { "Immutable path is outside its digest owner" }
    }

    private fun requireUuid(value: UUID, label: String) = requireUuidText(value.toString(), label)

    private fun requireUuidText(value: String, label: String) {
        require(UUID_TEXT.matches(value) && UUID.fromString(value).toString() == value) { "Invalid $label" }
    }

    private fun requireDigest(value: String, label: String) {
        require(SHA256.matches(value)) { "Invalid $label" }
    }

    private fun requireDisplay(value: String, maximum: Int, label: String) {
        require(value.codePointCount(0, value.length) in 1..maximum && value == value.trim()) { "Invalid $label" }
        require(Normalizer.normalize(value, Normalizer.Form.NFKC) == value) { "Non-normalized $label" }
        require(!hasUnpairedSurrogate(value) && value.none { it.isISOControl() || it in BIDI_CONTROLS }) { "Invalid $label characters" }
        require('/' !in value && '\\' !in value) { "Invalid $label separator" }
    }

    private fun requireBinding(value: String) {
        require(value.codePointCount(0, value.length) in 1..256 && value == value.trim()) { "Invalid binding token" }
        require(Normalizer.normalize(value, Normalizer.Form.NFKC) == value) { "Non-normalized binding token" }
        require(!hasUnpairedSurrogate(value) && value.none { it.isISOControl() || it in BIDI_CONTROLS }) { "Invalid binding token" }
    }

    private fun requireFailure(value: String) {
        require(FAILURE_CODE.matches(value)) { "Invalid stable interlock failure code" }
    }

    private fun packComparator(): Comparator<DescriptorPackEnvelope> = Comparator { left, right ->
        val name = SkinIdentity.unsignedUtf8Compare(
            Normalizer.normalize(left.name, Normalizer.Form.NFKC),
            Normalizer.normalize(right.name, Normalizer.Form.NFKC),
        )
        if (name != 0) name else SkinIdentity.unsignedUtf8Compare(left.id, right.id)
    }

    private fun descriptorValue(value: SkinLaunchDescriptor): JValue = JObject(
        mapOf(
            "schemaVersion" to JNumber(value.schemaVersion.toString()),
            "descriptorId" to JString(value.descriptorId.toString()),
            "sessionSequence" to JString(value.sessionSequence.toString()),
            "profileId" to JString(value.profileId),
            "gameVersion" to JString(value.gameVersion),
            "catalogId" to JString(value.catalogId),
            "catalogSha256" to JString(value.catalogSha256),
            "registryGenerationId" to JString(value.registryGenerationId),
            "registryGenerationSha256" to JString(value.registryGenerationSha256),
            "activation" to activationValue(value.activation),
            "packs" to JArray(value.packs.map(::packValue)),
            "leaseId" to JString(value.leaseId.toString()),
            "leaseTokenSha256" to JString(value.leaseTokenSha256),
        ),
    )

    private fun packValue(value: DescriptorPackEnvelope): JValue {
        val fields = linkedMapOf<String, JValue>(
            "id" to JString(value.id),
            "name" to JString(value.name),
            "author" to JString(value.author),
            "candidateKey" to JString(value.candidateKey),
            "rotationEligible" to JBoolean(value.rotationEligible),
            "currentObject" to objectValue(value.currentObject),
        )
        value.retainedActiveObject?.let { fields["retainedActiveObject"] = objectValue(it) }
        return JObject(fields)
    }

    private fun objectValue(value: DescriptorObjectEnvelope): JValue = JObject(
        mapOf(
            "objectRoot" to JString(value.objectRoot),
            "receiptPath" to JString(value.receiptPath),
            "treeSha256" to JString(value.treeSha256),
            "contentSha256" to JString(value.contentSha256),
            "manifestSha256" to JString(value.manifestSha256),
            "importReceiptSha256" to JString(value.importReceiptSha256),
            "textures" to JArray(value.textures.map(::textureValue)),
        ),
    )

    private fun textureValue(value: DescriptorTextureEnvelope): JValue = JObject(
        mapOf(
            "ordinal" to JNumber(value.ordinal.toString()),
            "target" to JString(value.target),
            "sourceRelativePath" to JString(value.sourceRelativePath),
            "sourceSha256" to JString(value.sourceSha256),
            "length" to JString(value.length.toString()),
        ),
    )

    private fun activationValue(value: SkinActivation): JValue {
        val fields = linkedMapOf<String, JValue>(
            "mode" to JString(value.mode.name),
            "active" to visualValue(value.active),
            "skinStamp" to JString(value.skinStamp.toString()),
            "rotationInterlock" to interlockValue(value.rotationInterlock),
        )
        value.selectedPackId?.let { fields["selectedPackId"] = JString(it) }
        return JObject(fields)
    }

    private fun snapshotValue(value: ActivationSnapshot): JValue {
        val fields = linkedMapOf<String, JValue>(
            "mode" to JString(value.mode.name),
            "active" to visualValue(value.active),
            "skinStamp" to JString(value.skinStamp.toString()),
        )
        value.selectedPackId?.let { fields["selectedPackId"] = JString(it) }
        return JObject(fields)
    }

    private fun visualValue(value: ActiveVisual): JValue = when (value) {
        ActiveVisual.Vanilla -> JObject(mapOf("kind" to JString("VANILLA")))
        is ActiveVisual.Pack -> JObject(
            mapOf(
                "kind" to JString("PACK"),
                "id" to JString(value.id),
                "treeSha256" to JString(value.treeSha256),
                "contentSha256" to JString(value.contentSha256),
                "importReceiptSha256" to JString(value.importReceiptSha256),
            ),
        )
    }

    private fun interlockValue(value: RotationInterlock): JValue {
        if (value.state == InterlockState.CLEAR) return JObject(mapOf("state" to JString("CLEAR")))
        val fields = linkedMapOf<String, JValue>(
            "state" to JString(value.state.name),
            "transactionId" to JString(requireNotNull(value.transactionId)),
            "operation" to JString(requireNotNull(value.operation).name),
            "baseGenerationId" to JString(requireNotNull(value.baseGenerationId)),
            "baseGenerationSha256" to JString(requireNotNull(value.baseGenerationSha256)),
            "prior" to snapshotValue(requireNotNull(value.prior)),
            "target" to snapshotValue(requireNotNull(value.target)),
            "bindingToken" to JString(requireNotNull(value.bindingToken).value),
            "priorEstablishedOnBinding" to JBoolean(requireNotNull(value.priorEstablishedOnBinding)),
        )
        value.originalFailure?.let { fields["originalFailure"] = JString(it) }
        value.rollbackFailure?.let { fields["rollbackFailure"] = JString(it) }
        return JObject(fields)
    }

    private fun decodeDescriptor(value: JValue): SkinLaunchDescriptor {
        val fields = value.objectWithKeys(
            setOf(
                "schemaVersion", "descriptorId", "sessionSequence", "profileId", "gameVersion", "catalogId", "catalogSha256",
                "registryGenerationId", "registryGenerationSha256", "activation", "packs", "leaseId", "leaseTokenSha256",
            ),
            emptySet(),
        )
        return SkinLaunchDescriptor(
            fields.int("schemaVersion"),
            UUID.fromString(fields.string("descriptorId")),
            fields.unsignedLong("sessionSequence"),
            fields.string("profileId"),
            fields.string("gameVersion"),
            fields.string("catalogId"),
            fields.string("catalogSha256"),
            fields.string("registryGenerationId"),
            fields.string("registryGenerationSha256"),
            decodeActivation(fields.required("activation")),
            fields.required("packs").array().map(::decodePack),
            UUID.fromString(fields.string("leaseId")),
            fields.string("leaseTokenSha256"),
        )
    }

    private fun decodePack(value: JValue): DescriptorPackEnvelope {
        val fields = value.objectWithKeys(
            setOf("id", "name", "author", "candidateKey", "rotationEligible", "currentObject"),
            setOf("retainedActiveObject"),
        )
        return DescriptorPackEnvelope(
            fields.string("id"),
            fields.string("name"),
            fields.string("author"),
            fields.string("candidateKey"),
            fields.boolean("rotationEligible"),
            decodeObject(fields.required("currentObject")),
            fields.optional("retainedActiveObject")?.let(::decodeObject),
        )
    }

    private fun decodeObject(value: JValue): DescriptorObjectEnvelope {
        val fields = value.objectWithKeys(
            setOf("objectRoot", "receiptPath", "treeSha256", "contentSha256", "manifestSha256", "importReceiptSha256", "textures"),
            emptySet(),
        )
        return DescriptorObjectEnvelope(
            fields.string("objectRoot"),
            fields.string("receiptPath"),
            fields.string("treeSha256"),
            fields.string("contentSha256"),
            fields.string("manifestSha256"),
            fields.string("importReceiptSha256"),
            fields.required("textures").array().map(::decodeTexture),
        )
    }

    private fun decodeTexture(value: JValue): DescriptorTextureEnvelope {
        val fields = value.objectWithKeys(setOf("ordinal", "target", "sourceRelativePath", "sourceSha256", "length"), emptySet())
        return DescriptorTextureEnvelope(
            fields.int("ordinal"),
            fields.string("target"),
            fields.string("sourceRelativePath"),
            fields.string("sourceSha256"),
            fields.unsignedLong("length"),
        )
    }

    private fun decodeActivation(value: JValue): SkinActivation {
        val fields = value.objectWithKeys(setOf("mode", "active", "skinStamp", "rotationInterlock"), setOf("selectedPackId"))
        return SkinActivation(
            enumValue(fields.string("mode")),
            fields.optionalString("selectedPackId"),
            decodeVisual(fields.required("active")),
            fields.unsignedLong("skinStamp"),
            decodeInterlock(fields.required("rotationInterlock")),
        )
    }

    private fun decodeSnapshot(value: JValue): ActivationSnapshot {
        val fields = value.objectWithKeys(setOf("mode", "active", "skinStamp"), setOf("selectedPackId"))
        return ActivationSnapshot(
            enumValue(fields.string("mode")),
            fields.optionalString("selectedPackId"),
            decodeVisual(fields.required("active")),
            fields.unsignedLong("skinStamp"),
        )
    }

    private fun decodeVisual(value: JValue): ActiveVisual {
        val fields = value.objectValue()
        return when (fields.string("kind")) {
            "VANILLA" -> {
                fields.requireKeys(setOf("kind"), emptySet())
                ActiveVisual.Vanilla
            }
            "PACK" -> {
                fields.requireKeys(setOf("kind", "id", "treeSha256", "contentSha256", "importReceiptSha256"), emptySet())
                ActiveVisual.Pack(
                    fields.string("id"),
                    fields.string("treeSha256"),
                    fields.string("contentSha256"),
                    fields.string("importReceiptSha256"),
                )
            }
            else -> error("Unknown active visual kind")
        }
    }

    private fun decodeInterlock(value: JValue): RotationInterlock {
        val fields = value.objectValue()
        return when (val state = enumValue<InterlockState>(fields.string("state"))) {
            InterlockState.CLEAR -> {
                fields.requireKeys(setOf("state"), emptySet())
                RotationInterlock.clear()
            }
            InterlockState.ARMED, InterlockState.ROLLBACK_FAILED -> {
                val required = mutableSetOf(
                    "state", "transactionId", "operation", "baseGenerationId", "baseGenerationSha256", "prior", "target",
                    "bindingToken", "priorEstablishedOnBinding",
                )
                if (state == InterlockState.ROLLBACK_FAILED) required += setOf("originalFailure", "rollbackFailure")
                fields.requireKeys(required, emptySet())
                RotationInterlock(
                    state,
                    fields.string("transactionId"),
                    enumValue<SkinOperationKind>(fields.string("operation")),
                    fields.string("baseGenerationId"),
                    fields.string("baseGenerationSha256"),
                    decodeSnapshot(fields.required("prior")),
                    decodeSnapshot(fields.required("target")),
                    SkinBindingToken(fields.string("bindingToken")),
                    fields.boolean("priorEstablishedOnBinding"),
                    fields.optionalString("originalFailure"),
                    fields.optionalString("rollbackFailure"),
                )
            }
        }
    }

    private inline fun <reified T : Enum<T>> enumValue(value: String): T =
        enumValues<T>().singleOrNull { it.name == value } ?: error("Unknown ${T::class.java.simpleName}")

    private fun render(value: JValue): ByteArray = buildString { appendValue(value) }.toByteArray(StandardCharsets.UTF_8)

    private fun StringBuilder.appendValue(value: JValue) {
        when (value) {
            is JObject -> {
                append('{')
                TreeMap(value.values).entries.forEachIndexed { index, (key, item) ->
                    if (index > 0) append(',')
                    appendString(key)
                    append(':')
                    appendValue(item)
                }
                append('}')
            }
            is JArray -> {
                append('[')
                value.values.forEachIndexed { index, item ->
                    if (index > 0) append(',')
                    appendValue(item)
                }
                append(']')
            }
            is JString -> appendString(value.value)
            is JNumber -> append(value.raw)
            is JBoolean -> append(if (value.value) "true" else "false")
            JNull -> append("null")
        }
    }

    private fun StringBuilder.appendString(value: String) {
        append('"')
        value.forEach { character ->
            when (character) {
                '"' -> append("\\\"")
                '\\' -> append("\\\\")
                '\b' -> append("\\b")
                '\t' -> append("\\t")
                '\n' -> append("\\n")
                '' -> append("\\f")
                '\r' -> append("\\r")
                else -> if (character.code < 0x20) append("\\u%04x".format(character.code)) else append(character)
            }
        }
        append('"')
    }

    private sealed interface JValue
    private data class JObject(val values: Map<String, JValue>) : JValue
    private data class JArray(val values: List<JValue>) : JValue
    private data class JString(val value: String) : JValue
    private data class JNumber(val raw: String) : JValue
    private data class JBoolean(val value: Boolean) : JValue
    private data object JNull : JValue

    private class Parser(private val source: String) {
        private var index = 0
        private var nodes = 0

        fun parse(): JValue {
            val value = value(0)
            require(index == source.length) { "Trailing launch descriptor JSON data" }
            return value
        }

        private fun value(depth: Int): JValue {
            require(depth <= MAX_JSON_DEPTH) { "Launch descriptor nesting exceeds its bound" }
            reserveNode()
            require(index < source.length) { "Unexpected end of launch descriptor" }
            return when (source[index]) {
                '{' -> objectValue(depth + 1)
                '[' -> arrayValue(depth + 1)
                '"' -> JString(string())
                't' -> literal("true", JBoolean(true))
                'f' -> literal("false", JBoolean(false))
                'n' -> literal("null", JNull)
                '-', in '0'..'9' -> number()
                else -> error("Invalid launch descriptor JSON token")
            }
        }

        private fun objectValue(childDepth: Int): JObject {
            index++
            val values = linkedMapOf<String, JValue>()
            if (take('}')) return JObject(values)
            while (true) {
                require(values.size < MAX_JSON_CONTAINER_ITEMS) { "Launch descriptor object exceeds its field bound" }
                reserveNode()
                require(source.getOrNull(index) == '"') { "Launch descriptor object key must be a string" }
                val key = string()
                require(key !in values) { "Duplicate launch descriptor key: $key" }
                require(take(':')) { "Missing launch descriptor object colon" }
                values[key] = value(childDepth)
                if (take('}')) return JObject(values)
                require(take(',')) { "Missing launch descriptor object comma" }
            }
        }

        private fun arrayValue(childDepth: Int): JArray {
            index++
            val values = mutableListOf<JValue>()
            if (take(']')) return JArray(values)
            while (true) {
                require(values.size < MAX_JSON_CONTAINER_ITEMS) { "Launch descriptor array exceeds its item bound" }
                values += value(childDepth)
                if (take(']')) return JArray(values)
                require(take(',')) { "Missing launch descriptor array comma" }
            }
        }

        private fun string(): String {
            require(take('"'))
            val output = StringBuilder()
            while (true) {
                require(index < source.length) { "Unterminated launch descriptor string" }
                when (val character = source[index++]) {
                    '"' -> return output.toString()
                    '\\' -> {
                        require(index < source.length) { "Incomplete launch descriptor escape" }
                        when (val escaped = source[index++]) {
                            '"', '\\', '/' -> output.append(escaped)
                            'b' -> output.append('\b')
                            'f' -> output.append('')
                            'n' -> output.append('\n')
                            'r' -> output.append('\r')
                            't' -> output.append('\t')
                            'u' -> {
                                require(index + 4 <= source.length) { "Incomplete launch descriptor unicode escape" }
                                output.append(source.substring(index, index + 4).toInt(16).toChar())
                                index += 4
                            }
                            else -> error("Invalid launch descriptor escape")
                        }
                    }
                    else -> {
                        require(character.code >= 0x20) { "Unescaped launch descriptor control" }
                        output.append(character)
                    }
                }
            }
        }

        private fun number(): JNumber {
            val start = index
            if (take('-')) Unit
            require(index < source.length && source[index].isDigit()) { "Invalid launch descriptor number" }
            if (source[index] == '0') index++ else while (source.getOrNull(index)?.isDigit() == true) index++
            require(source.getOrNull(index) !in listOf('.', 'e', 'E')) { "Only integer launch descriptor numbers are supported" }
            return JNumber(source.substring(start, index))
        }

        private fun <T : JValue> literal(text: String, value: T): T {
            require(source.startsWith(text, index)) { "Invalid launch descriptor literal" }
            index += text.length
            return value
        }

        private fun reserveNode() {
            require(nodes < MAX_JSON_NODES) { "Launch descriptor JSON node bound exceeded" }
            nodes++
        }

        private fun take(character: Char): Boolean {
            if (source.getOrNull(index) != character) return false
            index++
            return true
        }
    }

    private fun JValue.containsNull(): Boolean = when (this) {
        JNull -> true
        is JObject -> values.values.any { it.containsNull() }
        is JArray -> values.any { it.containsNull() }
        is JString, is JNumber, is JBoolean -> false
    }

    private fun JValue.objectValue(): Map<String, JValue> = (this as? JObject)?.values ?: error("Expected launch descriptor object")
    private fun JValue.objectWithKeys(required: Set<String>, optional: Set<String>): Map<String, JValue> =
        objectValue().also { it.requireKeys(required, optional) }

    private fun Map<String, JValue>.requireKeys(required: Set<String>, optional: Set<String>) {
        require(keys.containsAll(required) && keys.all { it in required || it in optional }) { "Unknown or missing launch descriptor field" }
    }

    private fun JValue.array(): List<JValue> = (this as? JArray)?.values ?: error("Expected launch descriptor array")
    private fun JValue.string(): String = (this as? JString)?.value ?: error("Expected launch descriptor string")
    private fun JValue.boolean(): Boolean = (this as? JBoolean)?.value ?: error("Expected launch descriptor boolean")
    private fun Map<String, JValue>.required(name: String): JValue = get(name) ?: error("Missing launch descriptor field: $name")
    private fun Map<String, JValue>.optional(name: String): JValue? = get(name)
    private fun Map<String, JValue>.string(name: String): String = required(name).string()
    private fun Map<String, JValue>.boolean(name: String): Boolean = required(name).boolean()
    private fun Map<String, JValue>.optionalString(name: String): String? = optional(name)?.string()

    private fun Map<String, JValue>.int(name: String): Int {
        val raw = (required(name) as? JNumber)?.raw ?: error("Expected launch descriptor integer")
        require(DECIMAL.matches(raw)) { "Non-canonical launch descriptor integer" }
        return raw.toInt()
    }

    private fun Map<String, JValue>.unsignedLong(name: String): Long {
        val raw = string(name)
        require(DECIMAL.matches(raw)) { "Non-canonical unsigned decimal" }
        return raw.toLong()
    }

    private fun hasUnpairedSurrogate(value: String): Boolean {
        var index = 0
        while (index < value.length) {
            when {
                value[index].isHighSurrogate() -> {
                    if (index + 1 >= value.length || !value[index + 1].isLowSurrogate()) return true
                    index += 2
                }
                value[index].isLowSurrogate() -> return true
                else -> index++
            }
        }
        return false
    }

    private data class VisualIdentity(
        val treeSha256: String,
        val contentSha256: String,
        val importReceiptSha256: String,
    )
}
