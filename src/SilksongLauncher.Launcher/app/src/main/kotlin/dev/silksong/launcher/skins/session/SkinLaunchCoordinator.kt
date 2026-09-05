package dev.silksong.launcher.skins.session

import dev.silksong.launcher.profiles.GameProfiles
import dev.silksong.launcher.profiles.ProfilePaths
import dev.silksong.launcher.skins.contracts.SkinImportCode
import dev.silksong.launcher.skins.contracts.SkinResult
import dev.silksong.launcher.skins.documents.SkinIdentity
import dev.silksong.launcher.skins.quota.SkinQuotaAdmission
import dev.silksong.launcher.skins.quota.SkinQuotaBudgets
import dev.silksong.launcher.skins.quota.SkinQuotaRequest
import dev.silksong.launcher.skins.registry.RegistryHead
import dev.silksong.launcher.skins.registry.SkinLockManager
import dev.silksong.launcher.skins.registry.SkinRegistryStore
import dev.silksong.launcher.skins.storage.DurableDirectoryPublisher
import dev.silksong.launcher.skins.storage.SkinFileSystem
import dev.silksong.launcher.skins.storage.exists
import dev.silksong.launcher.skins.storage.isDirectory
import dev.silksong.launcher.skins.storage.isRegularFile
import dev.silksong.launcher.skins.storage.isSymbolicLink
import dev.silksong.launcher.skins.storage.requireContained
import java.io.ByteArrayOutputStream
import java.io.File
import java.nio.ByteBuffer
import java.nio.charset.StandardCharsets
import java.security.SecureRandom
import java.util.UUID

internal data class SkinLaunchMaterial(
    val descriptorId: UUID,
    val leaseId: UUID,
    val rawLeaseToken: ByteArray,
)

internal fun interface SkinLaunchMaterialAuthority {
    fun create(): SkinLaunchMaterial
}

/** Cryptographically secure material source; construction alone never starts acquisition. */
internal class SecureSkinLaunchMaterialAuthority(
    private val random: SecureRandom = SecureRandom(),
) : SkinLaunchMaterialAuthority {
    override fun create(): SkinLaunchMaterial = SkinLaunchMaterial(
        descriptorId = secureUuid(),
        leaseId = secureUuid(),
        rawLeaseToken = ByteArray(RAW_TOKEN_BYTES).also(random::nextBytes),
    )

    private fun secureUuid(): UUID {
        val bytes = ByteArray(16).also(random::nextBytes)
        bytes[6] = ((bytes[6].toInt() and 0x0f) or 0x40).toByte()
        bytes[8] = ((bytes[8].toInt() and 0x3f) or 0x80).toByte()
        val buffer = ByteBuffer.wrap(bytes)
        return UUID(buffer.long, buffer.long)
    }

    private companion object {
        const val RAW_TOKEN_BYTES = 32
    }
}

/**
 * Non-production acquisition seam. No Activity, Intent, process start, or production caller is attached in this slice.
 */
internal class SkinLaunchCoordinator(
    private val profilePaths: ProfilePaths,
    private val fs: SkinFileSystem,
    private val lockManager: SkinLockManager,
    private val registryStore: SkinRegistryStore,
    private val sessionStore: SkinSessionStore,
    private val descriptorBuilder: SkinDescriptorBuilder,
    private val identities: ProcessIdentityAuthority,
    private val materials: SkinLaunchMaterialAuthority,
    private val quota: SkinQuotaAdmission,
) {
    private val profileRoot = profilePaths.root.absoluteFile.normalize()
    private val root = profilePaths.skinsRoot.absoluteFile.normalize()
    private val sessions = File(root, SESSIONS)
    private val staging = File(root, STAGING)
    private val stagingFs = SkinSessionStagingFileSystem(fs, root, staging)
    private val publisher = DurableDirectoryPublisher(stagingFs)

    init {
        require(profilePaths.profile == GameProfiles.require(PROFILE_ID)) {
            "Skin launch coordinator requires the exact Hollow Knight profile"
        }
        require(root == File(profileRoot, "skins").absoluteFile.normalize()) {
            "Skin launch coordinator root differs from its profile"
        }
        require(lockManager.root == root && registryStore.root == root && sessionStore.root == root) {
            "Skin launch coordinator authorities use different roots"
        }
        require(descriptorBuilder.paths.profileRoot == profileRoot && descriptorBuilder.paths.root == root) {
            "Skin launch descriptor authority uses a different profile"
        }
        require(quota.root.absoluteFile.normalize() == root) {
            "Skin launch quota authority uses a different profile"
        }
    }

    fun acquire(): SkinResult<SkinLaunchHandle> = withQuota(SkinQuotaBudgets.SESSION_ACQUISITION) {
        acquireAdmitted()
    }

    private fun acquireAdmitted(): SkinResult<SkinLaunchHandle> = locked {
        if (sessionStore.mutationGateForCoordinator(identities) != LeaseMutationGate.CLEAR) {
            return@locked blocked("Skin session acquisition is not clear")
        }
        val registryHead = when (val result = registryStore.recoverForCoordinator()) {
            is SkinResult.Error -> return@locked result
            is SkinResult.Ok -> result.value
        }
        val launcherOwner = when (val result = exactSelf()) {
            is SkinResult.Error -> return@locked result
            is SkinResult.Ok -> result.value
        }
        val sessionSequence = when (val result = sessionStore.reserveSequenceForCoordinator()) {
            is SkinResult.Error -> return@locked result
            is SkinResult.Ok -> result.value
        }
        val material = when (val result = createMaterial()) {
            is SkinResult.Error -> return@locked result
            is SkinResult.Ok -> result.value
        }
        val descriptor = when (
            val result = descriptorBuilder.build(
                material.descriptorId,
                sessionSequence,
                registryHead,
                material.leaseId,
                material.rawLeaseToken,
            )
        ) {
            is SkinResult.Error -> return@locked result
            is SkinResult.Ok -> result.value
        }
        val descriptorBytes = try {
            SkinLaunchDescriptorCodec.canonical(descriptor)
        } catch (_: Exception) {
            return@locked blocked("Launch descriptor is invalid")
        }
        val descriptorSha256 = SkinIdentity.sha256(descriptorBytes)
        val handle = SkinLaunchHandle(
            descriptorId = material.descriptorId,
            descriptorSha256 = descriptorSha256,
            descriptorPath = "$SESSIONS/${material.descriptorId}/$DESCRIPTOR_JSON",
            leaseId = material.leaseId,
            leaseToken = material.rawLeaseToken.toLowerHex(),
            sessionSequence = sessionSequence,
        )
        val preparedIntent = SkinAcquisitionIntent(
            phase = SkinAcquisitionPhase.PREPARED,
            descriptorId = handle.descriptorId,
            descriptorSha256 = handle.descriptorSha256,
            descriptorPath = handle.descriptorPath,
            leaseId = handle.leaseId,
            leaseTokenSha256 = descriptor.leaseTokenSha256,
            sessionSequence = handle.sessionSequence,
            registryGenerationId = registryHead.generationId,
            registrySha256 = registryHead.sha256,
            launcherOwner = launcherOwner,
        )
        when (val prepared = sessionStore.prepareAcquisitionForCoordinator(preparedIntent)) {
            is SkinResult.Error -> return@locked prepared
            is SkinResult.Ok -> Unit
        }
        when (val published = publishDescriptor(descriptor, descriptorBytes, descriptorSha256)) {
            is SkinResult.Error -> return@locked published
            is SkinResult.Ok -> Unit
        }
        val durableIntent = when (val advanced = sessionStore.advanceAcquisitionForCoordinator(preparedIntent)) {
            is SkinResult.Error -> return@locked advanced
            is SkinResult.Ok -> advanced.value
        }
        when (
            val pending = sessionStore.establishPendingForCoordinator(
                handle,
                launcherOwner,
                registryHead.generationId,
                registryHead.sha256,
            )
        ) {
            is SkinResult.Ok -> when (val completed = sessionStore.completeAcquisitionForCoordinator(durableIntent, pending.value)) {
                is SkinResult.Ok -> SkinResult.Ok(handle)
                is SkinResult.Error -> containFailedPending(handle, launcherOwner, registryHead, durableIntent) ?: completed
            }
            is SkinResult.Error -> containFailedPending(handle, launcherOwner, registryHead, durableIntent) ?: pending
        }
    }

    fun closeDefinitiveFailure(handle: SkinLaunchHandle, reason: String): SkinResult<LeaseHead> =
        sessionStore.close(handle.copy(leaseToken = handle.leaseToken.toCharArray().concatToString()), reason)

    private fun exactSelf(): SkinResult<ProcessIdentity> = try {
        when (val result = identities.self()) {
            is SelfIdentityResult.Known -> if (SkinLeaseStateCodec.validOwner(result.identity)) {
                SkinResult.Ok(result.identity.copy(processStartToken = result.identity.processStartToken.toCharArray().concatToString()))
            } else {
                blocked("Launcher process identity is not exact")
            }
            SelfIdentityResult.Unknown -> blocked("Launcher process identity is unavailable")
        }
    } catch (_: Exception) {
        blocked("Launcher process identity is unavailable")
    }

    private fun createMaterial(): SkinResult<SkinLaunchMaterial> = try {
        val supplied = materials.create()
        require(validGeneratedId(supplied.descriptorId) && validGeneratedId(supplied.leaseId))
        require(supplied.descriptorId != supplied.leaseId)
        require(supplied.rawLeaseToken.size == RAW_TOKEN_BYTES)
        SkinResult.Ok(
            SkinLaunchMaterial(
                supplied.descriptorId,
                supplied.leaseId,
                supplied.rawLeaseToken.copyOf(),
            ),
        )
    } catch (_: Exception) {
        blocked("Generated skin launch material is invalid")
    }

    private fun publishDescriptor(
        descriptor: SkinLaunchDescriptor,
        bytes: ByteArray,
        sha256: String,
    ): SkinResult<Unit> {
        val digestBytes = "$sha256\n".toByteArray(StandardCharsets.US_ASCII)
        val stage = File(staging, "descriptor-${descriptor.descriptorId}")
        val destination = File(sessions, descriptor.descriptorId.toString())
        try {
            stagingFs.requireContained(stage, root, allowMissingLeaf = true)
            if (stagingFs.exists(stage)) stagingFs.deleteContained(stage, root)
            stagingFs.createDirectory(stage)
            stagingFs.writeNew(File(stage, DESCRIPTOR_JSON), bytes.copyOf())
            stagingFs.writeNew(File(stage, DESCRIPTOR_SHA256), digestBytes)
        } catch (error: Exception) {
            return unavailable("Cannot stage launch descriptor: ${error.message}")
        }
        return when (
            val result = publisher.publishDetailed(stage, destination, root) { published ->
                verifyDescriptor(published, descriptor, bytes, sha256)
            }
        ) {
            is SkinResult.Error -> result
            is SkinResult.Ok -> SkinResult.Ok(Unit)
        }
    }

    private fun verifyDescriptor(
        directory: File,
        expected: SkinLaunchDescriptor,
        expectedBytes: ByteArray,
        expectedSha256: String,
    ): SkinResult<Unit> = try {
        fs.requireContained(directory, root)
        require(fs.isDirectory(directory) && !fs.isSymbolicLink(directory))
        val identity = fs.identity(directory)
        val children = fs.list(directory)
        require(children.map(File::getName).toSet() == DESCRIPTOR_FILES && children.size == DESCRIPTOR_FILES.size)
        val complete = File(directory, COMPLETE)
        require(fs.isRegularFile(complete) && !fs.isSymbolicLink(complete) && fs.identity(complete).size == 0L)
        val bytes = readStable(File(directory, DESCRIPTOR_JSON), MAX_DESCRIPTOR_BYTES)
        require(bytes.contentEquals(expectedBytes))
        require(readStable(File(directory, DESCRIPTOR_SHA256), 65).contentEquals("$expectedSha256\n".toByteArray(StandardCharsets.US_ASCII)))
        val parsed = SkinLaunchDescriptorCodec.parse(
            bytes,
            expectedSha256,
            DescriptorExpectations(
                expected.descriptorId,
                PROFILE_ID,
                SkinLaunchDescriptorCodec.GAME_VERSION,
                expected.catalogId,
                expected.catalogSha256,
                expected.leaseId,
            ),
        )
        require(parsed is SkinResult.Ok && parsed.value == expected)
        require(fs.identity(directory) == identity)
        SkinResult.Ok(Unit)
    } catch (_: Exception) {
        SkinResult.Error(SkinImportCode.DOCUMENT_INVALID, "Published launch descriptor is invalid")
    }

    private fun containFailedPending(
        handle: SkinLaunchHandle,
        launcherOwner: ProcessIdentity,
        registryHead: RegistryHead,
        intent: SkinAcquisitionIntent,
    ): SkinResult.Error? {
        val resumed = sessionStore.establishPendingForCoordinator(
            handle,
            launcherOwner,
            registryHead.generationId,
            registryHead.sha256,
        )
        if (resumed is SkinResult.Error) {
            return unavailable("Failed acquisition pending lease could not be exactly resumed")
        }
        val closed = when (val result = sessionStore.closeForCoordinator(handle, ACQUISITION_FAILED)) {
            is SkinResult.Ok -> result.value
            is SkinResult.Error -> return unavailable("Failed acquisition pending lease could not be durably closed")
        }
        return when (sessionStore.completeAcquisitionForCoordinator(intent, closed)) {
            is SkinResult.Ok -> null
            is SkinResult.Error -> unavailable("Failed acquisition intent could not be durably completed")
        }
    }

    private fun readStable(file: File, maximum: Int): ByteArray {
        fs.requireContained(file, root)
        require(fs.isRegularFile(file) && !fs.isSymbolicLink(file))
        val before = fs.identity(file)
        require(before.regularFile && before.size in 0..maximum.toLong())
        val output = ByteArrayOutputStream(before.size.toInt())
        fs.openNoFollow(file).use { input ->
            val buffer = ByteArray(8192)
            while (true) {
                val count = input.read(buffer)
                if (count < 0) break
                if (count == 0) continue
                require(output.size() + count <= maximum)
                output.write(buffer, 0, count)
            }
        }
        require(fs.identity(file) == before)
        return output.toByteArray()
    }

    private fun validGeneratedId(value: UUID): Boolean =
        value.variant() == 2 && value != NIL_UUID

    private fun ByteArray.toLowerHex(): String =
        joinToString(separator = "") { "%02x".format(it.toInt() and 0xff) }

    private fun <T> withQuota(
        request: SkinQuotaRequest,
        action: () -> SkinResult<T>,
    ): SkinResult<T> {
        val reservation = when (val result = quota.reserve(request)) {
            is SkinResult.Error -> return result
            is SkinResult.Ok -> result.value
        }
        return try {
            action()
        } finally {
            reservation.release()
        }
    }

    private fun <T> locked(action: () -> SkinResult<T>): SkinResult<T> = try {
        lockManager.withSessionThenRegistry(action)
    } catch (error: Exception) {
        unavailable("Cannot acquire ordered skin launch locks: ${error.message}")
    }

    private fun blocked(detail: String) = SkinResult.Error(SkinImportCode.LIFECYCLE_BLOCKED, detail)
    private fun unavailable(detail: String) = SkinResult.Error(SkinImportCode.DURABILITY_UNAVAILABLE, detail)

    private companion object {
        const val PROFILE_ID = "hollow-knight"
        const val SESSIONS = "sessions"
        const val STAGING = "staging"
        const val DESCRIPTOR_JSON = "descriptor.json"
        const val DESCRIPTOR_SHA256 = "descriptor.sha256"
        const val COMPLETE = ".complete"
        const val ACQUISITION_FAILED = "ACQUISITION_FAILED"
        const val RAW_TOKEN_BYTES = 32
        const val MAX_DESCRIPTOR_BYTES = 8 * 1024 * 1024
        val NIL_UUID: UUID = UUID(0L, 0L)
        val DESCRIPTOR_FILES = setOf(DESCRIPTOR_JSON, DESCRIPTOR_SHA256, COMPLETE)
    }
}
