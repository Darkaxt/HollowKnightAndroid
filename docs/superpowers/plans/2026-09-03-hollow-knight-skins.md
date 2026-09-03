# Hollow Knight H4 Skins Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the complete host-verified Hollow Knight H4 private skin library, deterministic death-rotation cores, launcher management surface, and fail-closed exact-build runtime seam.

**Architecture:** Android/Kotlin owns untrusted archive ingestion, canonical immutable storage, registry generations, session leases, launch descriptors, and launcher UI. Independent Kotlin, C#, and Python implementations share checked-in JSON goldens; an HK-only process runtime consumes one immutable descriptor through a narrow JVM bridge, while production texture writes and death rotation remain disarmed until stronger live proof exists.

**Tech Stack:** Kotlin/JVM 17, Android API 26+, Robolectric/JUnit 4, raw Store/Deflate ZIP parsing, RFC 8785 canonical JSON, SHA-256/CRC-32, C# 9/netstandard2.1, xUnit/net8.0 host tests, Python 3 `unittest`, Gradle 8.11 direct launcher, PowerShell compile checks.

---

## Execution contract

- Work only on `feature/h4-skins-host`. Commit and push this reviewed plan separately as the clean baseline before execution; that planning commit is not an implementation checkpoint.
- This plan adds exactly five implementation commits after that baseline, one per batch. TDD iterations remain uncommitted inside their batch, and a checkpoint is pushed only after every listed focused and regression command exits 0 with zero failures.
- Run commands from `D:\Temp\HollowKnightAndroid-h1` in Git Bash. There is no wrapper and `make` is unavailable; use the pinned `D:\Temp\dualsouls-unity-player\android\Tools\gradle\lib\gradle-launcher-8.11.jar` directly.
- No command in this plan launches Android, a device, an emulator, signing, packaging, installation, or release work. The combined H3/H4 first-level input-disabled gate is separate future work after explicit approval.
- Do not add native addresses, IL2CPP offsets, process-memory access, process scans, injected `PlayerData`, save edits, dynamic texture-target discovery, RAR extraction, or a skin browser.
- `GenerationPublisher`, `ModImport`, Gson, and `java.util.zip.ZipFile` are examples of repository style only. They are not extended or called as H4 ZIP, canonical JSON, or durability authority.
- `<profiles>/<id>/generations`, build `staging`, and build `current` stay independent of `<profiles>/hollow-knight/skins`.
- After Batch 5 creates the corpus test, ordinary runs skip it only when `HOLLOW_KNIGHT_SKIN_CORPUS` is absent; pre-close reconciliation supplies an explicit root and forbids a skip.
- After each batch, remove only the listed Gradle/.NET/temp outputs. Never remove source, signing material, the read-only corpus, or retained evidence.

## File map and ownership

- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/contracts/`: closed result codes, limits, hashes, bounded strings, and shared import values.
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/importing/`: one-copy quarantine, raw ZIP grammar, raw path authority, finite candidate discovery/mapping, PNG validation/decode, and normalization orchestration.
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/documents/`: strict documents and canonical JSON; no Gson authority.
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/storage/`: no-follow filesystem operations, tree verification, immutable repositories, and the single durable directory publication primitive.
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/registry/`: activation/interlock documents, lock order, generation CAS/recovery, mutations, import/replace coordination, and read-only library views.
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/session/`: complete launch descriptor, monotonic lease transitions, process identity, recovery, launch coordination, exact one-use HK Intent transfer, and bridge protocol.
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/quota/`: accounting, pins, last-eight policies, and contained garbage collection.
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/ui/`: profile-aware launcher library model/controller/activity and SAF source adapter.
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/core/`: Kotlin transaction, rotation, lifecycle, resource, and session semantics driven only by values and commands.
- `tools/hollow-knight-patches/src/skins/core/`: HK-only C# 9 strict JSON/catalog, Unity-free reducers, API abstractions, plan/transaction orchestration, and runtime coordinator; the wildcard is linked only into host tests and HK patches.
- `tools/hollow-knight-patches/src/skins/`: MonoBehaviour/JVM/lifecycle-hook/resource shells and typed bindings; none outside `core/` is linked into host tests or Silksong output.
- `tools/hollow-knight-patches/src/dualsouls/HollowKnightSkinPresenter.cs`: the only H4 in-game presenter partial; it borrows the process runtime.
- `tools/skin-goldens/v1/`: independently readable canonical cases and expected command logs.
- `tools/skins/` and `tools/ci/tests/`: Python oracle, corpus runner, reconciliation, and H4-scoped policy tests.
- `docs/verification/hollow-knight-skins-host.md`: Batch 5 command evidence and final status; the design ledger changes only in Batch 5.

### Task 1: Quarantine, raw ZIP authority, normalization, canonical documents, and immutable publication
**Files — Create:**
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/contracts/SkinImportCode.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/contracts/SkinLimits.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/contracts/SkinValues.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/catalog/HollowKnightCatalogPaths.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/importing/SkinImportInput.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/importing/SkinQuarantine.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/importing/BoundedZipReader.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/importing/ZipPathAuthority.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/importing/SkinCandidateDiscovery.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/importing/SkinCatalogMapper.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/importing/PngStructureValidator.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/importing/AndroidPngDecoder.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/importing/SkinNormalizer.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/importing/SkinObjectBuilder.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/storage/SkinObjectPublisher.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/documents/CanonicalJson.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/documents/SkinManifestDocument.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/documents/SkinObjectDocument.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/documents/SkinImportReceiptDocument.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/documents/SkinIdentity.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/storage/SkinPaths.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/storage/SkinFileSystem.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/storage/AndroidSkinFileSystem.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/storage/DurableDirectoryPublisher.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/storage/SkinTreeVerifier.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/storage/SkinObjectRepository.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/storage/SkinImportReceiptRepository.kt`
**Files — Modify:**
- `src/SilksongLauncher.Launcher/app/build.gradle.kts`: inside existing `android.sourceSets`, add `getByName("main") { assets.srcDir(rootProject.file("../../docs/superpowers/specs/data")) }`; do not copy the catalog.
**Files — Test:**
- `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/fixtures/TinyPngFixture.kt`
- `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/fixtures/RawZipFixture.kt`
- `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/fixtures/FaultingSkinFileSystem.kt`
- `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/importing/SkinQuarantineTest.kt`
- `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/importing/BoundedZipReaderTest.kt`
- `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/importing/ZipPathAuthorityTest.kt`
- `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/importing/SkinCandidateDiscoveryTest.kt`
- `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/importing/SkinCatalogMapperTest.kt`
- `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/importing/PngValidationTest.kt`
- `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/importing/SkinNormalizerTest.kt`
- `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/importing/SkinObjectBuilderTest.kt`
- `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/storage/SkinObjectPublisherTest.kt`
- `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/catalog/HollowKnightCatalogPathsTest.kt`
- `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/documents/CanonicalSkinDocumentsTest.kt`
- `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/storage/DurableDirectoryPublisherTest.kt`
- `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/storage/SkinTreeVerifierTest.kt`
**Contract/API outline:**
```kotlin
enum class SkinImportCode {
    OK, INVALID_INPUT, UNSUPPORTED_RAR, LIMIT_EXCEEDED, UNSUPPORTED_ZIP,
    ZIP_CORRUPT, PATH_REJECTED, PATH_COLLISION, AMBIGUOUS_LAYOUT, NO_CANDIDATE,
    TARGET_COLLISION, PNG_INVALID, DOCUMENT_INVALID, ID_COLLISION,
    DURABILITY_UNAVAILABLE, OBJECT_CORRUPT, IMPORT_RECEIPT_CORRUPT,
    REGISTRY_CORRUPT, REIMPORT_CHANGED, CANDIDATE_ALREADY_INSTALLED,
    REGISTRY_CONFLICT, REGISTRY_GENESIS_CORRUPT, REGISTRY_RECOVERY_AMBIGUOUS,
    REGISTRY_UNRECOVERABLE, SESSION_RECOVERY_AMBIGUOUS, PROFILE_QUOTA_EXCEEDED,
    NO_SELECTED_SKIN, ADAPTER_BLOCKED, LIFECYCLE_BLOCKED, ROLLBACK_FAILED, INDETERMINATE
}
sealed interface SkinResult<out T> {
    data class Ok<T>(val value: T) : SkinResult<T>
    data class Error(val code: SkinImportCode, val detail: String) : SkinResult<Nothing>
}
data class SkinLimits(
    val quarantineBytes: Long, val entries: Int, val directories: Int, val sourcePathBytes: Int,
    val sourceDepth: Int, val uncompressedBytes: Long, val expansionRatio: Int, val candidates: Int,
    val installedPacks: Int, val mappings: Int, val regularFiles: Int, val observedNodes: Int,
    val candidateDirectories: Int, val providerRows: Int, val textureBytes: Long,
    val previewBytes: Long, val payloadBytes: Long, val dimension: Int, val decodedPixels: Long
) {
    companion object {
        val V1 = SkinLimits(268435456, 4096, 512, 512, 16, 536870912, 100, 128,
            64, 205, 207, 512, 64, 1024, 16777216, 4194304, 268435456, 8192, 33554432)
    }
}
data class QuarantinedArchive(val file: File, val archiveSha256: String, val byteCount: Long, val archiveName: String)
data class StagedPayload(val relativePath: String, val sha256: String, val length: Long, val file: File)
data class SkinAlias(val sourceRawPathHex: String, val target: String, val rule: String)
data class SkinWarning(val code: String, val sourceRawPathHex: String)
data class ZipArchive(val file: File, val entries: List<RawZipEntry>)
data class AuthorizedZip(val archive: ZipArchive, val canonicalPaths: Map<Int, List<ByteArray>>)
data class CandidateSet(val candidates: List<SkinCandidate>, val warnings: List<SkinWarning>)
data class CatalogMapping(val textures: Map<String, RawZipEntry>, val aliases: List<SkinAlias>, val warnings: List<SkinWarning>)
data class PngInfo(val width: Int, val height: Int, val byteCount: Long)
data class DecodeResult(val width: Int, val height: Int, val pixelCount: Long)
data class SkinNodeIdentity(val fileKey: String, val size: Long, val regularFile: Boolean)
sealed interface SkinImportInput {
    val displayName: String?
    fun openOnce(): InputStream
    data class SelectedFile(
        override val displayName: String?, val opener: () -> InputStream
    ) : SkinImportInput { override fun openOnce(): InputStream = opener() }
    data class ImmediateFolderFile(
        override val displayName: String?, val documentId: String, val opener: () -> InputStream
    ) : SkinImportInput { override fun openOnce(): InputStream = opener() }
}
data class RawZipEntry(
    val centralIndex: Int, val rawName: ByteArray, val flags: Int, val method: Int,
    val crc32: Long, val compressedSize: Long, val uncompressedSize: Long,
    val localOffset: Long, val dataOffset: Long, val dataEnd: Long, val directory: Boolean
)
data class SkinCandidate(val rawPrefix: ByteArray, val layoutCode: Int, val entries: List<RawZipEntry>)
data class PreparedSkinCandidate(
    val candidateKey: String, val rawPrefix: ByteArray, val layoutCode: Int,
    val name: String, val contentSha256: String,
    val importReceiptBytes: ByteArray, val importReceiptSha256: String,
    val payloads: List<StagedPayload>, val mappings: Map<String, String>, val stagingRoot: File
)
sealed interface CandidatePreparationResult {
    data class Ready(val candidate: PreparedSkinCandidate) : CandidatePreparationResult
    data class Rejected(val rawPrefix: ByteArray, val code: SkinImportCode, val detail: String) : CandidatePreparationResult
}
data class BuiltSkin(
    val id: String, val candidateKey: String, val name: String, val contentSha256: String,
    val treeSha256: String, val manifestSha256: String, val importReceiptSha256: String,
    val manifestBytes: ByteArray, val objectBytes: ByteArray, val importReceiptBytes: ByteArray,
    val ephemeralRoot: File
)
data class PublishedSkin(
    val id: String, val candidateKey: String, val name: String,
    val contentSha256: String, val treeSha256: String, val manifestSha256: String,
    val importReceiptSha256: String, val objectRoot: File, val newlyCreatedRoots: List<File>
)
```
```kotlin
data class SkinGameDocument(
    val gameVersion: String, val catalogId: String, val assetRoot: String,
    val textures: Map<String, String>
)
data class SkinManifestDocument(
    val schemaVersion: Int, val id: String, val name: String, val author: String,
    val contentSha256: String, val games: Map<String, SkinGameDocument>,
    val license: String? = null, val source: String? = null, val homepage: String? = null,
    val attribution: String? = null, val preview: String? = null
)
data class SkinFileDocument(val path: String, val length: Long, val sha256: String)
data class SkinObjectDocument(
    val schemaVersion: Int, val treeSha256: String, val contentSha256: String,
    val manifestSha256: String, val fileCount: Int, val payloadBytes: Long,
    val files: List<SkinFileDocument>
)
data class SkinImportReceiptDocument(
    val schemaVersion: Int, val normalizerVersion: String, val candidateKey: String,
    val archiveSha256: String, val archiveName: String, val candidateRawPathHex: String,
    val layoutCode: Int, val signatureStatus: String, val source: String? = null,
    val homepage: String? = null, val aliases: List<SkinAlias>, val warnings: List<SkinWarning>
)
```
```kotlin
data class CatalogPathSet(val exactBytes: ByteArray, val sha256: String, val paths: List<String>)
class HollowKnightCatalogPaths(private val assets: AssetManager) {
    fun load(): SkinResult<CatalogPathSet>
}
object BoundedZipReader { fun read(file: File, limits: SkinLimits = SkinLimits.V1): SkinResult<ZipArchive> }
object ZipPathAuthority { fun validate(archive: ZipArchive): SkinResult<AuthorizedZip> }
object SkinCandidateDiscovery { fun discover(paths: AuthorizedZip): SkinResult<CandidateSet> }
object SkinCatalogMapper { fun map(candidate: SkinCandidate, paths: List<String>): SkinResult<CatalogMapping> }
object PngStructureValidator { fun inspect(input: InputStream, length: Long): SkinResult<PngInfo> }
interface PngDecoder { fun decodeAndRelease(file: File, expected: PngInfo): SkinResult<DecodeResult> }
class SkinNormalizer(
    private val catalog: CatalogPathSet,
    private val decoder: PngDecoder,
    private val fs: SkinFileSystem,
) {
    fun prepare(archive: QuarantinedArchive): SkinResult<List<CandidatePreparationResult>>
}
class SkinObjectBuilder(private val fs: SkinFileSystem) {
    fun build(prepared: PreparedSkinCandidate, id: String): SkinResult<BuiltSkin>
    fun discard(built: BuiltSkin)
}
class SkinObjectPublisher(
    private val objectRepository: SkinObjectRepository,
    private val receiptRepository: SkinImportReceiptRepository,
    private val fs: SkinFileSystem,
) {
    fun publish(built: BuiltSkin): SkinResult<PublishedSkin>
    fun discardUnreferenced(published: PublishedSkin, referencedDigests: Set<String>): SkinResult<Unit>
}
object CanonicalJson {
    fun manifest(document: SkinManifestDocument): ByteArray
    fun objectDocument(document: SkinObjectDocument): ByteArray
    fun importReceipt(document: SkinImportReceiptDocument): ByteArray
    fun parseManifest(bytes: ByteArray): SkinManifestDocument
    fun parseObject(bytes: ByteArray): SkinObjectDocument
    fun parseImportReceipt(bytes: ByteArray): SkinImportReceiptDocument
}
interface SkinFileSystem {
    fun createDirectory(path: File); fun writeNew(path: File, bytes: ByteArray)
    fun syncFile(path: File); fun syncDirectory(path: File); fun atomicMove(source: File, target: File)
    fun openNoFollow(path: File): InputStream; fun identity(path: File): SkinNodeIdentity
    fun list(path: File): List<File>; fun deleteContained(path: File, owner: File)
}
class DurableDirectoryPublisher(private val fs: SkinFileSystem) {
    fun publish(staging: File, destination: File, profileAncestor: File, verify: (File) -> Unit): SkinResult<File>
}
```
**Invariants to encode:**
- Quarantine copies each provider stream once, reserves 256 MiB before copying, streams SHA-256/count, syncs the private copy, then classifies ZIP/RAR/invalid by magic only.
- `BoundedZipReader` parses EOCD, central and local headers itself. It accepts only single-disk non-ZIP64 Store/Deflate and enforces bit-3 zero/full local tuples plus exactly one 12- or 16-byte descriptor, CRC, range non-overlap, checked per-entry/aggregate 100:1 ratios, 4,096 entries, 512 directories, 512 MiB output, and all other v1 limits.
- Raw central bytes are path authority. `/` is the only separator; UTF-8-flagged paths get strict decode, NFKC, and ASCII-fold collision checks; unflagged paths retain bytes and receive ASCII fold only. Reject `0x7075`; other bounded extras can only warn.
- Candidate precedence is full-install, root, unique wrapper, then immediate multi-pack children; discovery is nonrecursive and ordered by unsigned raw prefix. Mapping is exact, unique ASCII-fold, then the seven finite alias families; root `orbicon`, alternates, nested archives, unsupported trees, and unknown files only warn.
- `HollowKnightCatalogPaths` opens packaged asset `hollow-knight-skin-catalog-v1.txt` from the exact main asset directory `docs/superpowers/specs/data`, rejects CR/BOM/non-LF endings, and verifies 205 unique `.png` rows, pinned order, and SHA-256 `258a7fa2b3a1a94d114eb73c39259dfa6853139017afced53ca3afa668a1372a` before mapping; no copied catalog exists.
- `SkinNormalizer.prepare` computes candidateKey, deterministic metadata/content, canonical receipt, validated deduplicated staged payloads, and mappings before any ID choice. One candidate-local mapping/PNG/bound failure becomes an ordered `Rejected` while valid siblings remain `Ready`; an archive-wide structural/security failure is the outer `Error`.
- `PreparedSkinCandidate` carries canonical name but no ID, author, attribution, manifest/object/tree, or durable location. `SkinObjectBuilder.build(prepared,id)` uses owned ephemeral staging, always writes exact `author="Unknown"` and `attribution="Unknown"`, constructs canonical ID-qualified manifest/object/tree, and writes no immutable repository, pointer, registry, or other durable authority.
- `SkinObjectPublisher.publish(built)` alone publishes receipt then object through the durable primitive. Builder discard and every failure/cancel path remove only the owned ephemeral root; a rejected/idempotent/changed-owner build can never leave a published orphan.
- PNG validation/decode enforces signature/chunk CRC/IHDR/IEND/no APNG, dimensions/pixels/bytes; payloads use lowercase 52-character base32 digests. Canonical parsers reject duplicate/unknown/null fields and enforce all document/receipt bounds, ordering, warning assignment, and identity framing.
- Publication writes/syncs files/directories and `.complete`, renames atomically, then syncs destination/new ancestors before success; an existing immutable destination is fully reverified and receives the same barrier.
- [ ] **Red — write focused fixtures and tests.** Define test methods `loadsPinnedCatalogFromPackagedAsset`, `copiesProviderExactlyOnceAndClassifiesMagic`, `acceptsStoreAndDeflateDescriptorMatrix`, `rejectsDescriptorTupleAndZipStructureMatrix`, `enforcesDeclaredAndStreamedRatioWithoutOverflow`, `usesRawCentralPathAuthorityAcrossFlagDomains`, `derivesImplicitDirectoriesWithoutChangingCandidateKey`, `appliesFiniteLayoutPrecedenceAndRawOrdering`, `mapsOnlyExactCaseFoldAndFiniteAliases`, `rejectsTwoSourcesForOneTarget`, `validatesPngStructureAndAndroidDecode`, `preparesBeforeIdWithoutManifestObjectOrTree`, `preservesReadySiblingsWhenCandidateIsRejected`, `buildsExplicitDeterministicTestIdWithoutPublishing`, `alwaysWritesExactUnknownAuthorAndAttribution`, `publishesReceiptThenObjectOnlyForAcceptedBuild`, `emitsByteIdenticalCanonicalDocuments`, `fitsMaximumReceiptWithoutTruncation`, `publishesOnlyAfterAncestorBarrier`, and `revalidatesExistingImmutableDestination`.
- [ ] **Run the red tests.** Execute:
```bash
java -classpath 'D:\Temp\dualsouls-unity-player\android\Tools\gradle\lib\gradle-launcher-8.11.jar' org.gradle.launcher.GradleMain -p src/SilksongLauncher.Launcher :app:testDebugUnitTest --tests 'dev.silksong.launcher.skins.catalog.HollowKnightCatalogPathsTest' --tests 'dev.silksong.launcher.skins.importing.*Test' --tests 'dev.silksong.launcher.skins.documents.*Test' --tests 'dev.silksong.launcher.skins.storage.*Test'
```
Expected: nonzero exit because the new authority types/functions do not compile; no production file is added merely to make fixture construction bypass the contracts.
- [ ] **Green — implement quarantine and raw parsing.** Add checked arithmetic first, parse central declarations before extraction, validate local headers and descriptors against central rows, stream CRC/actual sizes into contained staging, and make archive-wide structural/security errors invalidate all siblings.
- [ ] **Green — implement path, discovery, mapping, and warning assignment.** Preserve raw component arrays, derive implicit directories, enforce exact collision domains, classify the four layout codes, map only catalog PNGs, and assign each ignored entry once to the longest candidate prefix with fixed warning priority.
- [ ] **Green — package catalog authority and prepare candidates.** Add the exact spec-data directory to main assets, load/verify its LF bytes, decode only staged files, assign candidate-local `Ready`/`Rejected` in raw-prefix order, deduplicate digest payloads, and emit canonical receipt/content identity without choosing an ID or constructing manifest/object/tree.
- [ ] **Green — build ID-qualified objects ephemerally.** Given each test ID, write exact Unknown metadata, recompute payload/content, construct canonical manifest/object/tree under an owned ephemeral root, prove no repository/pointer/registry write, and discard rejected builds without touching sibling preparations.
- [ ] **Green — publish only accepted test builds.** Publish receipt then object through `SkinObjectPublisher`, fully verify existing destinations, and clean the ephemeral build in `finally`.
- [ ] **Run focused verification.** Repeat the red command. Expected: exit 0 and zero failures.
- [ ] **Run the batch-wide launcher regression.** Execute:
```bash
java -classpath 'D:\Temp\dualsouls-unity-player\android\Tools\gradle\lib\gradle-launcher-8.11.jar' org.gradle.launcher.GradleMain -p src/SilksongLauncher.Launcher :app:testDebugUnitTest
```
Expected: exit 0 and zero failures.

- [ ] **Remove disposable outputs, inspect the patch, commit once, and push.** Execute:
```bash
rm -rf src/SilksongLauncher.Launcher/.gradle src/SilksongLauncher.Launcher/app/build
git diff --check
git status --short
git add -- src/SilksongLauncher.Launcher/app/build.gradle.kts src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/contracts src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/catalog src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/importing src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/documents src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/storage src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins
git commit -m 'feat: add bounded Hollow Knight skin import store' -m 'Co-Authored-By: Claude <noreply@anthropic.com>'
git push fork HEAD:feature/h4-skins-host
```
Expected: diff check is silent, status contains only Batch 1 paths relative to the separately pushed plan baseline before staging, commit succeeds once, and push updates `fork/feature/h4-skins-host`.

### Task 2: Registry, activation, session seams, isolation/quota, and launcher library
**Files — Create:**
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/registry/SkinActivation.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/registry/SkinRegistryDocument.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/registry/SkinLockManager.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/registry/SkinRegistryStore.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/registry/SkinRegistryMutations.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/registry/SkinImportCoordinator.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/registry/SkinLibraryReader.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/session/SkinLaunchDescriptor.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/session/SkinDescriptorBuilder.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/session/SkinSessionLease.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/session/SkinSessionStore.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/session/SkinSessionRecovery.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/session/ProcessIdentity.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/session/SkinLaunchCoordinator.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/session/SkinSessionBridgeProtocol.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/quota/SkinQuota.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/quota/SkinRetention.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/quota/SkinGarbageCollector.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/ui/SkinLibraryModel.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/ui/SkinLibraryRow.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/ui/SkinLibraryController.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/ui/SkinSafSource.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/ui/SkinsActivity.kt`
- `src/SilksongLauncher.Launcher/app/src/main/res/layout/activity_skins.xml`
**Files — Modify:**
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/profiles/ProfilePaths.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/SettingsActivity.kt`
- `src/SilksongLauncher.Launcher/app/src/main/res/layout/activity_settings.xml`
- `src/SilksongLauncher.Launcher/app/src/main/res/values/strings.xml`
- `src/SilksongLauncher.Launcher/app/src/main/AndroidManifest.xml`
**Files — Test:**
- `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/profiles/ProfilePathsTest.kt`
- `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/registry/SkinRegistryStoreTest.kt`
- `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/registry/SkinRegistryRecoveryTest.kt`
- `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/registry/SkinRegistryMutationsTest.kt`
- `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/registry/SkinImportCoordinatorTest.kt`
- `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/session/SkinDescriptorBuilderTest.kt`
- `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/session/SkinLaunchDescriptorTest.kt`
- `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/session/SkinSessionStoreTest.kt`
- `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/session/SkinSessionRecoveryTest.kt`
- `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/session/ProcessIdentityTest.kt`
- `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/quota/SkinQuotaRetentionTest.kt`
- `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/ui/SkinLibraryControllerTest.kt`
- `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/ui/SkinsActivityTest.kt`
- `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/ProfileIsolationTest.kt`
**Contract/API outline:**
```kotlin
enum class SkinMode { OFF, ON, ROTATE }
@JvmInline value class SkinBindingToken(val value: String)
sealed interface ActiveVisual {
    data object Vanilla : ActiveVisual
    data class Pack(val id: String, val treeSha256: String, val contentSha256: String, val importReceiptSha256: String) : ActiveVisual
}
sealed interface VerifiedLiveVisualProof {
    val binding: SkinBindingToken
    data class Vanilla(override val binding: SkinBindingToken) : VerifiedLiveVisualProof
    data class Pack(override val binding: SkinBindingToken, val visual: ActiveVisual.Pack) : VerifiedLiveVisualProof
}
enum class InterlockState { CLEAR, ARMED, ROLLBACK_FAILED }
enum class SkinOperationKind { STARTUP_APPLY, MODE_ON, MODE_OFF, DEATH_ROTATION, REBIND_APPLY }
data class ActivationSnapshot(
    val mode: SkinMode, val selectedPackId: String?, val active: ActiveVisual, val skinStamp: Long
)
data class RotationInterlock(
    val state: InterlockState, val transactionId: String?, val operation: SkinOperationKind?,
    val baseGenerationId: String?, val baseGenerationSha256: String?,
    val prior: ActivationSnapshot?, val target: ActivationSnapshot?,
    val bindingToken: SkinBindingToken?, val priorEstablishedOnBinding: Boolean?,
    val originalFailure: String?, val rollbackFailure: String?
)
data class SkinActivation(
    val mode: SkinMode, val selectedPackId: String?, val active: ActiveVisual,
    val skinStamp: Long, val rotationInterlock: RotationInterlock
)
data class RegistryPack(
    val id: String, val name: String, val author: String, val candidateKey: String,
    val treeSha256: String, val contentSha256: String, val importReceiptSha256: String,
    val rotationEligible: Boolean
)
data class SkinRegistryDocument(
    val schemaVersion: Int, val generationId: String, val sequence: Long,
    val parentGenerationId: String?, val operationId: String, val writer: String,
    val profileId: String, val gameVersion: String, val catalogId: String,
    val catalogSha256: String, val packs: List<RegistryPack>, val activation: SkinActivation
)
fun interface RegistryMutation { fun apply(current: SkinRegistryDocument): SkinResult<SkinRegistryDocument> }
data class RegistryHead(val generationId: String, val sequence: Long, val sha256: String, val document: SkinRegistryDocument)
class SkinLockManager { fun <T> withSessionThenRegistry(action: () -> T): T }
class SkinRegistryStore {
    fun recover(): SkinResult<RegistryHead>
    fun commit(expected: RegistryHead, operationId: UUID, writer: String, mutation: RegistryMutation): SkinResult<RegistryHead>
}
internal sealed interface RegistryActivationClosure {
    data class OnToRotate(val expected: ActivationSnapshot) : RegistryActivationClosure
    data class VerifiedVanillaToOff(val expected: ActivationSnapshot, val proof: VerifiedLiveVisualProof.Vanilla) : RegistryActivationClosure
    data class VerifiedTransaction(val expectedArmed: RotationInterlock, val closure: ActivationSnapshot) : RegistryActivationClosure
}
class SkinRegistryMutations {
    fun select(id: String?): RegistryMutation; fun setEligibility(id: String, eligible: Boolean): RegistryMutation
    internal fun closeActivation(closure: RegistryActivationClosure): RegistryMutation; fun install(published: PublishedSkin): RegistryMutation
    fun replace(targetId: String, expectedTree: String, expectedReceipt: String, published: PublishedSkin): RegistryMutation
}
internal fun interface SkinModeAdvancePort { fun advance(): SkinResult<Unit> }
class SkinLibraryController internal constructor(private val modeAdvance: SkinModeAdvancePort) { fun advanceMode(): SkinResult<Unit> = modeAdvance.advance() }
data class CandidatePreparationSummary(val rawPrefixHex: String, val candidateKey: String?, val name: String?, val code: SkinImportCode, val detail: String)
internal enum class SkinPreparationHandleState { OPEN, CLAIMED, CLOSED }
data class SkinPreparationHandle(val handleId: UUID, val candidates: List<CandidatePreparationSummary>)
internal class SkinPreparationRecord(val handle: SkinPreparationHandle, val stagingOwner: File, private var state: SkinPreparationHandleState, private val retained: List<CandidatePreparationResult>) { fun claim(): SkinResult<List<CandidatePreparationResult>>; fun cancel(): SkinResult<File> }
data class CandidateImportResult(val rawPrefix: ByteArray, val code: SkinImportCode, val published: PublishedSkin?, val detail: String)
object SkinImportCoordinator {
    fun recoverOrphansOnProcessStart(): SkinResult<Int>
    fun prepare(input: SkinImportInput): SkinResult<SkinPreparationHandle>
    fun commitImport(handleId: UUID): SkinResult<List<CandidateImportResult>>
    fun commitReplace(handleId: UUID, sourceCandidateKey: String, targetId: String, expectedGenerationSha256: String, expectedTree: String, expectedReceipt: String): SkinResult<CandidateImportResult>
    fun cancel(handleId: UUID): SkinResult<Unit>
}
```
```kotlin
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
enum class LeaseState { LAUNCH_PENDING, GAME_OWNED, CLOSED }
enum class LeaseMutationGate { CLEAR, ACTIVE, UNKNOWN }
data class DescriptorTextureEnvelope(val ordinal: Int, val target: String, val sourceRelativePath: String, val sourceSha256: String, val length: Long)
data class DescriptorObjectEnvelope(val objectRoot: String, val receiptPath: String, val treeSha256: String, val contentSha256: String, val manifestSha256: String, val importReceiptSha256: String, val textures: List<DescriptorTextureEnvelope>)
data class DescriptorPackEnvelope(val id: String, val name: String, val author: String, val candidateKey: String, val rotationEligible: Boolean, val currentObject: DescriptorObjectEnvelope, val retainedActiveObject: DescriptorObjectEnvelope?)
data class SkinLaunchDescriptor(val schemaVersion: Int, val descriptorId: UUID, val sessionSequence: Long, val profileId: String, val gameVersion: String, val catalogId: String, val catalogSha256: String, val registryGenerationId: String, val registryGenerationSha256: String, val activation: SkinActivation, val packs: List<DescriptorPackEnvelope>, val leaseId: UUID, val leaseTokenSha256: String)
data class DescriptorExpectations(val descriptorId: UUID, val profileId: String, val gameVersion: String, val catalogId: String, val catalogSha256: String, val leaseId: UUID)
object SkinLaunchDescriptorCodec { fun canonical(value: SkinLaunchDescriptor): ByteArray; fun parse(bytes: ByteArray, expectedSha256: String, expected: DescriptorExpectations): SkinResult<SkinLaunchDescriptor> }
data class SkinLaunchHandle(
    val descriptorId: UUID, val descriptorSha256: String, val descriptorPath: String,
    val leaseId: UUID, val leaseToken: String, val sessionSequence: Long
)
data class LeaseHead(
    val descriptorId: UUID, val leaseId: UUID, val transitionSequence: Long,
    val state: LeaseState, val sha256: String
)
class SkinLaunchCoordinator {
    fun acquire(profileId: String): SkinResult<SkinLaunchHandle>
    fun closeDefinitiveFailure(handle: SkinLaunchHandle, reason: String): SkinResult<LeaseHead>
}
class SkinSessionStore {
    fun mutationGate(liveness: ProcessIdentityAuthority): LeaseMutationGate
    fun claim(handle: SkinLaunchHandle, gameOwner: ProcessIdentity): SkinResult<LeaseHead>
    fun close(handle: SkinLaunchHandle, reason: String): SkinResult<LeaseHead>
    fun recover(liveness: ProcessIdentityAuthority): SkinResult<LeaseHead?>
}
```

- Add `ProfilePaths.skinsRoot: File = child("skins")`; `SkinPaths` derives every H4 child beneath that root and never aliases build generations/staging/current.
- Registry genesis is one sequence-0 canonical generation. Normal commits lock `session.lock` then `registry.lock`, CAS exact generation/digest, publish child, then durably update `next`, `previous`, `current`, and remove `next`. Recovery uses bounded pointer names/content and sequence/parent evidence, never mtime.
- `SkinImportCoordinator` is the sole process `object`. `prepare`, `commitImport`, `commitReplace`, `cancel`, and `recoverOrphansOnProcessStart` each hold `session.lock -> registry.lock` and fail before mutation when lease state is active or unknown. `prepare` alone opens/quarantines provider input once, creates contained incomplete staging with no `.complete` or durable pointer, returns a handle containing summaries only, and registers one in-memory `OPEN` record privately retaining the ordered `List<CandidatePreparationResult>`—full Ready candidates with payloads/mappings/receipt/staging plus Rejected records; record/state/results exist only in process memory.
- A commit atomically claims exactly `OPEN -> CLAIMED` before any build; unknown, `CLAIMED`, or `CLOSED` attempts cannot touch staging. After claim it reads only the private retained results, revalidates each Ready candidate's bounded containment/no alias, payloads/mappings/receipt/staging, and recomputed candidateKey, then builds/consumes without provider reopen; `commitReplace` then requires exactly one Ready `sourceCandidateKey` before target CAS/build. `cancel` alone changes `OPEN -> CLOSED`, revalidates bounded containment/no alias, deletes only that staging, and cannot touch `CLAIMED`; every claimed exit cleans staging and becomes `CLOSED`. Before exposing handles or operations on next process start, orphan recovery scans at most `SkinLimits.V1.candidates` (128) immediate UUID children beneath the fixed handle-staging root and deletes only contained, no-alias, incomplete handle staging because no prior in-memory claimant survives process death; an indeterminate gate/scan exposes no handle operation. Exact owners remain idempotent, changed owners reject, accepted new/Replace candidates alone publish, and post-publication CAS failure removes only newly created roots still unreferenced by any head.
- Explicit Replace is the only target-ID/new-key path, requires expected generation/target/tree/receipt CAS, preserves activation/stamp/eligibility, and retains an active old object/receipt through descriptors.
- `SkinLaunchDescriptor` is the complete strict ≤8 MiB canonical authority: schema/descriptor/session identity, profile/game/catalog IDs+hash, registry head ID+hash, full activation/interlock, lease ID/token SHA-256, and normalized-name+ID ordered pack union of selection/eligibility/active/rollback references. Each pack has its current object and only an older-digest active `retainedActiveObject`; each object has verified roots/hashes and ordinal catalog-ordered textures. Kotlin rejects duplicate/unknown/null/mismatched IDs, hashes, order, containment/path, union, retained-object, or bound violations. Raw 256-bit lease token exists only in `SkinLaunchHandle` delivery, never descriptor/disk; descriptor stores its lowercase SHA-256 binding.
- Session acquisition reserves monotonic sequence, publishes descriptor and transition zero, then durably publishes `sessions/active`. Claim/close publish child head before active-pointer CAS. Recover at most 64 descriptors and three states each using exact pointer tuples; no Task 2 production path acquires a lease.
- Identity authority reads self exactly and performs only bounded exact-package/process-name queries. Pending recovery closes only when launcher owner is `DefinitivelyDead` and exact game presence is `Absent`; GAME_OWNED closes only when its expected owner is `DefinitivelyDead`. `Alive`, `Present`, or `Unknown` blocks mutation/launch; denied access, malformed `/proc`, PID reuse, or ActivityManager contradiction returns `Unknown`.
- Quota uses allocated bytes or 4 KiB-rounded logical size: 1 GiB profile, 96 MiB sessions, 8 MiB descriptor, 64 KiB lease state. Retain current/previous/next, explicit interlock/visual/session pins, last eight registry generations, active lease, and eight highest closed sessions; explicit generation pins are non-transitive across older parent edges.
- `SkinLibraryController` exposes only payload-free `advanceMode()`: UI/presenter has no `SkinMode` payload, direct setter, skip, reverse, separate rotation toggle, random, or shuffle path. It delegates unchanged to injected payload-free `SkinModeAdvancePort` and never constructs `RegistryActivationClosure`; only the later core command executor may call internal `closeActivation`. `OnToRotate` validates expected `ON`/`CLEAR` and computes `ROTATE` with all other fields unchanged. `VerifiedVanillaToOff` is accepted only after the executor matches `proof.binding` to current authoritative binding, validates expected `ROTATE`/`CLEAR`, and uses normal exact-head CAS to compute `OFF` while preserving selected/eligibility/active receipt/stamp; registry `active` never proves live vanilla; `VerifiedLiveVisualProof` exists only in process memory and in no canonical document. `VerifiedTransaction` requires current ARMED byte-equal `expectedArmed` and closure equal its recorded prior/target chosen by the correlated reducer. `SkinsActivity` exists only for `hollow-knight`, shows import results/warnings, identity/provenance, selection, eligibility, confirmed Replace, mode/active/order/interlock/error with labeled 48 dp controls, font-scale-safe scrolling, visual focus order, text announcements, and disables mutation—but not status—for active or unknown lease.

- [ ] **Red — write registry/session/UI tests.** Define `publishesDeterministicGenesisOnce`, `recoversCurrentThenQualifiedNextThenPrevious`, `rejectsDuplicateCandidateOwnershipReadOnly`, `enforcesLastEightHistoryFloorAndExplicitPins`, `resolvesPreparedOwnerBeforeBuild`, `prepareOpensProviderOnceAndCommitNeverReopens`, `commitConsumesRetainedCandidateResultsWithoutProviderReopen`, `handleExposesOnlyIdAndSummaries`, `preparedHandleHasNoCompleteOrDurablePointer`, `commitVsCancelRaceAllowsExactlyOneOpenTransition`, `doubleCommitRejectsClaimedAndClosedHandle`, `cancelClaimedHandleCannotTouchStaging`, `processStartOrphanRecoveryDeletesOnlyBoundedContainedIncompleteStaging`, `everyHandleOperationHonorsActiveOrUnknownLeaseGate`, `claimedHandleReverifiesContainmentAndCandidateKeyBeforeBuild`, `threeCandidateReplaceSelectsExactSourceKey`, `absentOrDuplicateReplaceKeyRejectsBeforeBuild`, `failedCommitRemovesOnlyUnreferencedNewPublications`, `reimportsOwnerIdempotentlyWithoutReceiptRename`, `replacesOnlyConfirmedCasTarget`, `buildsCompleteDescriptorUnionInCatalogOrder`, `roundTripsCompleteStrictLaunchDescriptor`, `rejectsDescriptorDuplicateUnknownMismatchOrderHashAndPath`, `persistsOnlyLeaseTokenSha256AndDeliversRawTokenInHandle`, `enforcesDescriptorEightMiBBound`, `claimsAndClosesOnlyAfterDurableActiveBarrier`, `reconcilesCrashAtEveryLeasePointerBoundary`, `pendingCloseRequiresDeadLauncherAndAbsentGame`, `ownedCloseRequiresExpectedOwnerDefinitivelyDead`, `deniedMalformedPidReuseAndContradictionAreUnknown`, `neverCrossesProfileRoots`, `disablesMutationForActiveOrUnknownLease`, `uiExposesOnlyPayloadFreeAdvanceMode`, `registryHasNoPublicModeSetterOrSkipPath`, `offAdvanceWithoutSelectionReturnsNoSelectedWithoutWrite`, `onToRotateClosureKeepsVisualSelectionEligibilityStamp`, `verifiedVanillaRotateOffModeOnlyPreservesReceiptStampAndWritesNothing`, `modeOffTransactionClosureCannotPersistOffBeforeVerifiedCompletion`, `registryActiveReceiptNeverProvesLiveVanilla`, `uiCannotConstructOrReuseBindingProof`, and `meetsLauncherAccessibilityContract`.
- [ ] **Run the red tests.** Execute:
```bash
java -classpath 'D:\Temp\dualsouls-unity-player\android\Tools\gradle\lib\gradle-launcher-8.11.jar' org.gradle.launcher.GradleMain -p src/SilksongLauncher.Launcher :app:testDebugUnitTest --tests 'dev.silksong.launcher.skins.registry.*Test' --tests 'dev.silksong.launcher.skins.session.*Test' --tests 'dev.silksong.launcher.skins.quota.*Test' --tests 'dev.silksong.launcher.skins.ui.*Test' --tests 'dev.silksong.launcher.skins.ProfileIsolationTest'
```
Expected: nonzero exit for absent registry/session/UI contracts, pre-ID coordinator/builder/publisher flow, and exact liveness result types.

- [ ] **Green — implement genesis, CAS, and one-copy two-step import/replace.** Make the coordinator a process singleton; gate and lock all five operations, create summary-only handles plus private in-memory `OPEN` records retaining full ordered candidate results over incomplete staging, atomically claim and consume those records without provider reopen, reverify contained/no-alias candidate identity after claim, enforce exact source/effective ID and owner rules, close/clean every exit, and run bounded process-start orphan cleanup before exposing operations. Publish only accepted new/Replace candidates, remove new unreferenced publication on commit failure, set new eligibility false, and never alter visual activation.
- [ ] **Green — implement complete descriptor and lease seams/recovery.** Canonicalize/strict-parse the ≤8 MiB model with unsigned decimals/lowercase UUIDs, full union/current+retained objects/catalog textures, token hash and raw-handle delivery; validate all dependencies/order/paths. Publish canonical lease pointer tuples with barriers and implement exact pending/owned liveness plus `LeaseMutationGate`, without production acquisition wiring.
- [ ] **Green — implement quota/retention/GC.** Reserve before quarantine/descriptor/generation work, collect only contained verified unpinned state with parent syncs and no active/unknown lease, and return `PROFILE_QUOTA_EXCEEDED` without evicting pins.
- [ ] **Green — integrate only the launcher library surface.** Add profile-aware Skins beside Mods/logs in `SettingsActivity`, SAF file/folder prepare with immediate-child cap, ordered candidate results, mandatory source-candidate choice when a handle has more than one Ready candidate, confirmed Replace, cancel, manifest registration, strings/layout, and one payload-free `advanceMode()` wired only to an injected port plus internal snapshot/transaction and proof-qualified mode-only closure seams; expose no direct mode setter/toggle, never treat registry `active` as live proof, make no live visual claim before Tasks 3–4, and leave game-launch wiring unchanged.
- [ ] **Run focused verification.** Repeat the red command. Expected: exit 0 and zero failures.
- [ ] **Run the batch-wide launcher regression.** Execute:
```bash
java -classpath 'D:\Temp\dualsouls-unity-player\android\Tools\gradle\lib\gradle-launcher-8.11.jar' org.gradle.launcher.GradleMain -p src/SilksongLauncher.Launcher :app:testDebugUnitTest
```
Expected: exit 0 and zero failures.

- [ ] **Remove disposable outputs, inspect the patch, commit once, and push.** Execute:
```bash
rm -rf src/SilksongLauncher.Launcher/.gradle src/SilksongLauncher.Launcher/app/build
git diff --check
git status --short
git add -- src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/registry src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/session src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/quota src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/ui src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/profiles/ProfilePaths.kt src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/SettingsActivity.kt src/SilksongLauncher.Launcher/app/src/main/res/layout/activity_skins.xml src/SilksongLauncher.Launcher/app/src/main/res/layout/activity_settings.xml src/SilksongLauncher.Launcher/app/src/main/res/values/strings.xml src/SilksongLauncher.Launcher/app/src/main/AndroidManifest.xml src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/profiles/ProfilePathsTest.kt src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins
git commit -m 'feat: add Hollow Knight skin registry and launcher library' -m 'Co-Authored-By: Claude <noreply@anthropic.com>'
git push fork HEAD:feature/h4-skins-host
```
Expected: diff check is silent, only Batch 2 paths remain, one commit is created, and the fork branch advances.

### Task 3: Fixed catalog, Unity-free Kotlin/C# cores, and independent goldens
**Files — Create:**
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/catalog/HollowKnightSkinCatalog.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/core/SkinCoreModels.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/core/SkinTransactionCore.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/core/SkinRotationCore.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/core/SkinLifecycleCore.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/core/SkinResourceCore.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/core/SkinSessionCore.kt`
- `tools/hollow-knight-patches/src/skins/core/SkinCoreModels.cs`
- `tools/hollow-knight-patches/src/skins/core/StrictJson.cs`
- `tools/hollow-knight-patches/src/skins/core/SkinLaunchDescriptor.cs`
- `tools/hollow-knight-patches/src/skins/core/SkinLaunchDescriptorParser.cs`
- `tools/hollow-knight-patches/src/skins/core/HollowKnightSkinCatalog.cs`
- `tools/hollow-knight-patches/src/skins/core/SkinTransactionCore.cs`
- `tools/hollow-knight-patches/src/skins/core/SkinRotationCore.cs`
- `tools/hollow-knight-patches/src/skins/core/SkinLifecycleCore.cs`
- `tools/hollow-knight-patches/src/skins/core/SkinResourceCore.cs`
- `tools/hollow-knight-patches/src/skins/core/SkinSessionCore.cs`
- `tools/hollow-knight-skin-core-check/HollowKnightSkinCore.Check.csproj`
**Files — Test:**
- `tools/shared-patches-tests/HollowKnightSkinCatalogTests.cs`
- `tools/shared-patches-tests/HollowKnightSkinTransactionCoreTests.cs`
- `tools/shared-patches-tests/HollowKnightSkinRotationCoreTests.cs`
- `tools/shared-patches-tests/HollowKnightSkinLifecycleCoreTests.cs`
- `tools/shared-patches-tests/HollowKnightSkinResourceCoreTests.cs`
- `tools/shared-patches-tests/HollowKnightSkinGoldenTests.cs`
- `tools/shared-patches-tests/HollowKnightSkinLaunchDescriptorTests.cs`
- `tools/skin-goldens/v1/launch-descriptors.json`
- `tools/skin-goldens/v1/catalog.json`
- `tools/skin-goldens/v1/canonical-documents.json`
- `tools/skin-goldens/v1/transactions.json`
- `tools/skin-goldens/v1/rotation.json`
- `tools/skin-goldens/v1/lifecycle.json`
- `tools/skin-goldens/v1/resources.json`
- `tools/skins/skin_golden_oracle.py`
- `tools/ci/tests/test_hollow_knight_skin_goldens.py`
- `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/catalog/HollowKnightSkinCatalogTest.kt`
- `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/core/SkinTransactionCoreTest.kt`
- `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/core/SkinRotationCoreTest.kt`
- `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/core/SkinLifecycleCoreTest.kt`
- `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/core/SkinResourceCoreTest.kt`
- `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/core/SkinGoldenTest.kt`
- `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/session/SkinLaunchDescriptorTest.kt`
**Files — Modify/Delete:**
- Modify `tools/shared-patches-tests/SharedPatches.Tests.csproj` only to link `../hollow-knight-patches/src/skins/core/**/*.cs` as `HollowKnightSkins/Core/%(RecursiveDir)%(Filename)%(Extension)` and copy `../skin-goldens/v1/*.json`; retain its current language version/global usings and link no non-core H4 file.
- Modify `src/SilksongLauncher.Launcher/emulator-test-app/src/main/kotlin/io/github/darkaxt/dualsouls/lab/LabGameActivity.kt` only to compile against `SkinRotationCore` and typed mode/tokens.
- Delete `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/SkinRotationStateMachine.kt`.
- Delete `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/SkinRotationStateMachineTest.kt`.
**Contract/API outline:**
```kotlin
enum class CatalogStrategyKind { EXACT_SPRITE, TK2D_MATERIAL_ONE, TK2D_MATERIAL_MANY, BOUNDED_MATERIAL_ONE, BOUNDED_MATERIAL_MANY, HOOK, NO_CACHE_REBIND }
data class SkinCatalogDescriptor(val ordinal: Int, val target: String, val strategy: CatalogStrategyKind, val bindingKey: String, val maximumTargets: Int)
@JvmInline value class DeathEpoch(val value: ULong)
@JvmInline value class HeroBindingToken(val value: String)
data class StableRespawnToken(
    val deathEpoch: DeathEpoch, val hero: HeroBindingToken, val skin: SkinBindingToken
)
data class TransactionEnvelope(
    val transactionId: String, val operation: SkinOperationKind,
    val baseGenerationId: String, val baseGenerationSha256: String,
    val prior: ActivationSnapshot, val target: ActivationSnapshot,
    val binding: SkinBindingToken, val priorEstablishedOnBinding: Boolean
)
data class TransactionCorrelation(val transactionId: String, val binding: SkinBindingToken)
data class RegistryCommitReceipt(val expectedGenerationId: String, val expectedGenerationSha256: String, val newGenerationId: String, val newGenerationSha256: String)
sealed interface SkinCommand {
    data class Prepare(val envelope: TransactionEnvelope, val desired: ActiveVisual) : SkinCommand
    data class Arm(val envelope: TransactionEnvelope) : SkinCommand
    data class Apply(val correlation: TransactionCorrelation) : SkinCommand
    data class Rollback(val correlation: TransactionCorrelation) : SkinCommand
    data class CommitOnToRotate(val expected: ActivationSnapshot) : SkinCommand
    data class CommitVerifiedVanillaOff(val expected: ActivationSnapshot, val proof: VerifiedLiveVisualProof.Vanilla) : SkinCommand
    data class Commit(val correlation: TransactionCorrelation, val expectedGenerationId: String, val expectedGenerationSha256: String, val closure: ActivationSnapshot) : SkinCommand
    data class Cancel(val deathEpoch: DeathEpoch?) : SkinCommand
}
data class OrderedPack(val id: String, val orderKeyHex: String, val rotationEligible: Boolean)
data class PendingRotation(
    val candidateId: String, val deathEpoch: DeathEpoch,
    val hero: HeroBindingToken, val skin: SkinBindingToken
)
data class RotationState(val activation: ActivationSnapshot, val binding: SkinBindingToken?, val liveVisualProof: VerifiedLiveVisualProof?, val pending: PendingRotation?, val lastConfirmedEpoch: DeathEpoch?)
sealed interface RotationEvent {
    data class ConfirmDeath(val epoch: DeathEpoch, val hero: HeroBindingToken, val skin: SkinBindingToken) : RotationEvent
    data class StableRespawn(val token: StableRespawnToken) : RotationEvent
    data object AdvanceMode : RotationEvent
    data class Rebind(val hero: HeroBindingToken, val skin: SkinBindingToken) : RotationEvent
}
data class CoreDecision(val state: RotationState, val commands: List<SkinCommand>, val diagnosis: String)
class SkinTransactionCore { fun decide(state: TransactionState, event: TransactionEvent): TransactionDecision }
class SkinRotationCore {
    fun transition(state: RotationState, eligible: List<OrderedPack>, event: RotationEvent): CoreDecision
}
data class HeroObservation(
    val hero: HeroBindingToken, val skin: SkinBindingToken, val acceptingInput: Boolean,
    val fullDamageMode: Boolean, val health: Int, val canTakeDamage: Boolean,
    val playable: Boolean, val paused: Boolean, val cutscene: Boolean, val sceneTransition: Boolean
)
data class LifecycleState(
    val armedHero: HeroBindingToken?, val pendingEpoch: DeathEpoch?, val stableCount: Int,
    val currentHero: HeroBindingToken?, val currentSkin: SkinBindingToken?
)
sealed interface LifecycleSignal {
    data class BeforeDeath(val hero: HeroBindingToken) : LifecycleSignal
    data class AfterDeath(val hero: HeroBindingToken) : LifecycleSignal
    data class Update(val observation: HeroObservation) : LifecycleSignal
}
data class LifecycleDecision(val state: LifecycleState, val confirmedEpoch: DeathEpoch?, val stableToken: StableRespawnToken?, val diagnosis: String)
class SkinLifecycleCore { fun observe(state: LifecycleState, signal: LifecycleSignal): LifecycleDecision }
data class ResourceState(val residentBytes: Long, val allocations: Int, val scratchBytes: Long, val retirementPending: Boolean)
data class ResourceRequest(val decodedBytes: Long, val planBytes: Long, val allocations: Int, val sharesVerifiedIdentity: Boolean)
data class ResourceDecision(val accepted: Boolean, val state: ResourceState, val diagnosis: String)
class SkinResourceCore { fun reserve(state: ResourceState, request: ResourceRequest): ResourceDecision }
enum class TransactionPhase { IDLE, PREPARED, ARMED, APPLIED, ROLLED_BACK, COMMITTED, BLOCKED }
data class TransactionState(val phase: TransactionPhase, val interlock: RotationInterlock, val binding: SkinBindingToken?, val armCommitReceipt: RegistryCommitReceipt?, val pendingClosure: ActivationSnapshot?)
sealed interface TransactionEvent {
    data class Begin(val envelope: TransactionEnvelope) : TransactionEvent
    data class Prepared(val correlation: TransactionCorrelation) : TransactionEvent
    data class ArmCommitted(val correlation: TransactionCorrelation, val commitReceipt: RegistryCommitReceipt) : TransactionEvent
    data class ApplyVerified(val correlation: TransactionCorrelation) : TransactionEvent
    data class ApplyFailed(val correlation: TransactionCorrelation, val code: String) : TransactionEvent
    data class RollbackVerified(val correlation: TransactionCorrelation, val freshBinding: Boolean) : TransactionEvent
    data class RollbackFailed(val correlation: TransactionCorrelation, val code: String, val persistedFailureReceipt: RegistryCommitReceipt?) : TransactionEvent
    data class CompletionCommitted(val correlation: TransactionCorrelation, val commitReceipt: RegistryCommitReceipt) : TransactionEvent
    data class CompletionRejected(val correlation: TransactionCorrelation, val code: String) : TransactionEvent
    data class CompletionIndeterminate(val correlation: TransactionCorrelation) : TransactionEvent
}
data class TransactionDecision(val state: TransactionState, val commands: List<SkinCommand>, val diagnosis: String)
data class CoreSessionState(val leaseState: LeaseState, val writesBlocked: Boolean, val recoveryAttempted: Boolean)
sealed interface CoreSessionEvent {
    data object Claim : CoreSessionEvent
    data object Close : CoreSessionEvent
    data class Recovery(val determinate: Boolean) : CoreSessionEvent
}
class SkinSessionCore { fun transition(state: CoreSessionState, event: CoreSessionEvent): CoreSessionState }
```
- C# mirrors all names/fields and payload-free `AdvanceMode` under `DualSouls.Skins.HollowKnight.Core`, using `ulong` epochs and `long` stamps; `SkinLaunchDescriptorParser.Parse(byte[] bytes, string expectedSha256, DescriptorExpectations expected)` returns `DescriptorParseResult { Success, Descriptor, Code }`, mirrors the complete Kotlin model, rejects duplicate/unknown/null/mismatched identity/order/hash/path/token-binding/8 MiB cases; `StrictJson` is self-contained, duplicate/unknown rejecting, and uses no `System.Text.Json`. `HollowKnightSkinCore.Check.csproj` targets `netstandard2.1`, sets `LangVersion=9.0`, nullable/implicit usings disabled, default compile items false, links only `../hollow-knight-patches/src/skins/core/**/*.cs`, and has a `BeforeTargets="CoreCompile"` error when `@(Compile)` is empty so red fails before core exists.
- Both catalogs contain exactly 205 explicit descriptors corresponding byte-for-byte to the pinned LF catalog digest `258a7fa2b3a1a94d114eb73c39259dfa6853139017afced53ca3afa668a1372a`: ordinals 0–161 Skinable, 162–170 SaveHud, 171–204 AreaBackgrounds. Every row has one finite strategy, binding key, and positive bound; no runtime-discovered row exists.
- `RotationState.activation` and current `binding` are sole authorities; `liveVisualProof` is accepted only when its typed proof binding equals that current authoritative binding, and binding change clears it before any decision. Registry `active` is retention/history, never live proof. Payload-free `AdvanceMode` computes only `OFF -> ON -> ROTATE -> OFF`; no event/API can set, skip, reverse, separately toggle, randomize, or shuffle. OFF means verified-live vanilla while preserving selectedPackId/eligibility; OFF→ON without selection returns `NO_SELECTED_SKIN` unchanged/write-free, otherwise transactionally targets selected and publishes ON only after completion. ON death/respawn never rotates. ON→ROTATE emits `CommitOnToRotate`, leaves state ON until exact CAS succeeds, and retains selected/current visual, eligibility, pending-null, and stamp. ROTATE uses only deterministic NFKC-name unsigned UTF-8 then ASCII-ID eligible order; zero/one is a no-op, and advancement requires confirmed death plus exact stable token. ROTATE→OFF always cancels pending first: matching `VerifiedLiveVisualProof.Vanilla` emits `CommitVerifiedVanillaOff`, leaves state ROTATE until normal exact CAS succeeds, performs no visual write, and preserves selected/eligibility/active receipt/stamp; absent, pack, or stale-binding proof starts `MODE_OFF` write-ahead vanilla restoration and leaves disarmed ROTATE until verified durable completion publishes OFF. Failure/indeterminate never persists OFF.
- A binding change invalidates prior `liveVisualProof` before mode/death decisions. Rebind retains exactly one pending candidate/epoch while replacing hero/skin tokens; only a newly verified proof may qualify the new binding, the ring never advances, and every old-token signal/proof is stale.
- Each Begin/Arm envelope carries transaction ID, base generation ID/digest, complete prior/target snapshots, binding, and prior-established flag. Every post-Begin event carries `TransactionCorrelation`; `ArmCommitted` must match the base and stores its exact old/new `RegistryCommitReceipt`, while `RollbackFailed` carries the exact failure receipt or null only when persistence was unavailable. `ApplyVerified` emits a closure `Commit` for target. Correlated `RollbackVerified` is visual verification only: it enters `ROLLED_BACK` and emits a closure `Commit` for exact prior/stamp against the stored ARMED head, but cannot clear persisted ARMED, alter persisted activation/stamp, publish in-memory visual/stamp, invalidate clones, or retire resources. Only a later correlated `CompletionCommitted` whose exact old/new receipt has the command's expected head and a canonically verified new head containing the pending closure may enter `COMMITTED`, clear the interlock, and publish that closure/stamp. Any correlation/binding/parent/receipt mismatch is a stale no-op retaining ARMED/write blocking; target-completion rejection enters reverse rollback without publication, while rejected or indeterminate rollback closure enters `BLOCKED` with ARMED retained.
- Lifecycle confirms one matching monotonic death epoch and emits the structured stable-respawn token only after three consecutive true observations on its current hero/skin; false or stale observations reset/ignore deterministically.
- Resource accounting uses checked 64-bit RGBA32 bytes and enforces 32/96/224 MiB, 205/410 allocations, dedup identity, reference counts, and one acknowledged retirement set.
- Goldens are data, not serialized output copied from Kotlin or C#. Python manually parses inputs, computes canonical ordering/state transitions, and compares both host logs. Include payload-free sole-cycle advances, every OFF/ON/ROTATE effect, verified-live-vanilla no-write ROTATE→OFF, non-vanilla transactional ROTATE→OFF, stale proof on binding change, no registry-active inference, and forbidden skip/reverse/direct/random/shuffle path, ordering ties, zero/one/two/three rings, duplicate/stale/death/rebind tokens, correlation/parent/receipt mismatches, visual-only rollback then exact closure, rejection/indeterminate, limits/overflow/refcounts, canonical bytes, and complete descriptors with duplicate/unknown/mismatch/order/hash/path/token-binding/8 MiB negatives.

- [ ] **Red — write explicit catalog/core/golden tests.** Name host methods `hasExactlyPinned205FiniteDescriptors`, `beginAndArmCarryCompleteTransactionEnvelope`, `postBeginEventsRequireExactCorrelation`, `durableCasEventsCarryExactCommitReceipts`, `mismatchedCorrelationOrReceiptIsStaleNoOp`, `armsBeforeFirstWriteAndCommitsStampOnce`, `rollbackVerificationAloneCannotClearArmedOrPublishStamp`, `rollsBackReverseAndLeavesIndeterminateBlocked`, `advanceModeCyclesOnlyOffOnRotateOff`, `noRequestedModePayloadSetterSkipReverseToggleRandomOrShuffleExists`, `offIsVerifiedLiveVanillaAndPreservesSelectionAndEligibility`, `offWithoutSelectionStaysOffNoSelectedAndWriteFree`, `onPinsSelectedAcrossDeathAndRespawn`, `onToRotateIsModeOnlyAndKeepsCurrentVisual`, `rotateAdvancesOnlyAfterConfirmedDeathAndExactStableToken`, `verifiedVanillaRotateOffIsModeOnlyWithUnchangedStampAndNoWrites`, `nonVanillaRotateOffCancelsPendingAndPublishesOffOnlyAfterCompletion`, `durableActiveReceiptCannotSubstituteForLiveVanillaProof`, `bindingChangeInvalidatesLiveVisualProof`, `matchesRespawnEpochHeroAndSkin`, `requiresThreeStableMatchingUpdates`, `rebindRetainsCandidateEpochAndRejectsOldTokens`, `enforcesEveryResourceCeiling`, `kotlinCsharpDescriptorModelsAndStrictFailuresMatchGoldens`, and `kotlinCsharpPythonLogsMatchIndependentGoldens`; use the rollback-verification and every mode-transition name in Kotlin/C# with same-named golden cases.
- [ ] **Run all four red gates.** Execute:
```bash
java -classpath 'D:\Temp\dualsouls-unity-player\android\Tools\gradle\lib\gradle-launcher-8.11.jar' org.gradle.launcher.GradleMain -p src/SilksongLauncher.Launcher :app:testDebugUnitTest --tests 'dev.silksong.launcher.skins.catalog.*Test' --tests 'dev.silksong.launcher.skins.core.*Test' --tests 'dev.silksong.launcher.skins.session.SkinLaunchDescriptorTest' :emulator-test-app:compileDebugKotlin
dotnet build tools/hollow-knight-skin-core-check/HollowKnightSkinCore.Check.csproj -c Release
dotnet test tools/shared-patches-tests/SharedPatches.Tests.csproj -c Release --filter 'FullyQualifiedName~HollowKnightSkin'
python -m unittest tools.ci.tests.test_hollow_knight_skin_goldens -v
```
Expected: each command exits nonzero because explicit descriptors, typed cores, linked C# sources, and independent golden expectations do not yet exist.

- [ ] **Green — write explicit catalog and descriptor parsers.** Translate each pinned row into checked Kotlin/C# descriptors and validate catalog count/order/hash/strategy bounds; mirror complete `SkinLaunchDescriptor`, hand-parse strict ≤8 MiB JSON, validate identities/order/hashes/paths/token binding, and keep C# core free of `UnityEngine` and reflection.
- [ ] **Green — implement transaction/rotation/lifecycle/session/resource reducers.** Make reducers total/immutable; derive sole cycle from activation/current binding on payload-free `AdvanceMode`; accept typed live proof only for the same binding, invalidate it on change, never infer from registry active, and branch ROTATE→OFF into verified-Vanilla no-write exact CAS or `MODE_OFF` restoration after canceling pending. Enforce all mode/death/token effects. Retain ARMED receipt/closure, require exact correlations/receipts, keep rollback verification unpublished until completion, block stale/failed closure, and reject overflow, stale tokens, second transaction, or retirement set.
- [ ] **Green — replace the Boolean proof.** Update `LabGameActivity` to drive payload-free `RotationEvent.AdvanceMode` from `RotationState.activation` and current binding/proof, plus typed `DeathEpoch`, hero/skin tokens, and exact `StableRespawnToken`; never pass a requested `SkinMode`. Compile only with `:emulator-test-app:compileDebugKotlin`; run no Android test/emulator.
- [ ] **Green — generate expected goldens independently.** Hand-author case inputs/expected semantic logs; have Python independently validate canonical bytes, strict descriptor positives/negatives, catalog digest, and reducer transitions without importing Kotlin/C#, then make Kotlin and C# readers independently reproduce those logs.
- [ ] **Run focused verification.** Repeat all four red commands. Expected: each exits 0 with zero failures.
- [ ] **Run batch regressions.** Execute:
```bash
java -classpath 'D:\Temp\dualsouls-unity-player\android\Tools\gradle\lib\gradle-launcher-8.11.jar' org.gradle.launcher.GradleMain -p src/SilksongLauncher.Launcher :app:testDebugUnitTest :emulator-test-app:compileDebugKotlin
dotnet build tools/hollow-knight-skin-core-check/HollowKnightSkinCore.Check.csproj -c Release
dotnet test tools/shared-patches-tests/SharedPatches.Tests.csproj -c Release
python -m unittest discover -s tools/ci/tests -p 'test_hollow_knight_skin_*.py' -v
```
Expected: every command exits 0 with zero failures; `:emulator-test-app:compileDebugKotlin` is host compilation only and starts no emulator.

- [ ] **Remove disposable outputs, inspect the patch, commit once, and push.** Execute:
```bash
rm -rf src/SilksongLauncher.Launcher/.gradle src/SilksongLauncher.Launcher/app/build src/SilksongLauncher.Launcher/emulator-test-app/build tools/shared-patches-tests/bin tools/shared-patches-tests/obj tools/hollow-knight-skin-core-check/bin tools/hollow-knight-skin-core-check/obj tools/ci/tests/__pycache__ tools/skins/__pycache__
git diff --check
git status --short
git add -- src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/catalog/HollowKnightSkinCatalog.kt src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/core src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/SkinRotationStateMachine.kt src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins src/SilksongLauncher.Launcher/emulator-test-app/src/main/kotlin/io/github/darkaxt/dualsouls/lab/LabGameActivity.kt tools/hollow-knight-patches/src/skins/core tools/hollow-knight-skin-core-check/HollowKnightSkinCore.Check.csproj tools/shared-patches-tests/SharedPatches.Tests.csproj tools/shared-patches-tests/HollowKnightSkinCatalogTests.cs tools/shared-patches-tests/HollowKnightSkinTransactionCoreTests.cs tools/shared-patches-tests/HollowKnightSkinRotationCoreTests.cs tools/shared-patches-tests/HollowKnightSkinLifecycleCoreTests.cs tools/shared-patches-tests/HollowKnightSkinResourceCoreTests.cs tools/shared-patches-tests/HollowKnightSkinGoldenTests.cs tools/shared-patches-tests/HollowKnightSkinLaunchDescriptorTests.cs tools/skin-goldens/v1 tools/skins/skin_golden_oracle.py tools/ci/tests/test_hollow_knight_skin_goldens.py
git commit -m 'feat: add deterministic Hollow Knight skin cores' -m 'Co-Authored-By: Claude <noreply@anthropic.com>'
git push fork HEAD:feature/h4-skins-host
```
Expected: one coherent core checkpoint is pushed; deleted legacy files are staged by their exact pathspecs, and no emulator command ran.

### Task 4: Typed HK API/runtime/lifecycle/bridge/presenter and fail-closed production integration
**Files — Create:**
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/session/SkinLaunchIntentContract.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/session/SkinSessionBridge.kt`
- `tools/hollow-knight-patches/src/skins/core/IHollowKnightSkinApi.cs`
- `tools/hollow-knight-patches/src/skins/core/IHollowKnightSkinSessionPort.cs`
- `tools/hollow-knight-patches/src/skins/core/HollowKnightSkinPlanPreparer.cs`
- `tools/hollow-knight-patches/src/skins/core/HollowKnightSkinTransactionOrchestrator.cs`
- `tools/hollow-knight-patches/src/skins/core/HollowKnightSkinRuntimeCoordinator.cs`
- `tools/hollow-knight-patches/src/skins/HollowKnightSkinResourceArena.cs`
- `tools/hollow-knight-patches/src/skins/HollowKnightSkinLifecycleAdapter.cs`
- `tools/hollow-knight-patches/src/skins/HollowKnightSkinJvmBridge.cs`
- `tools/hollow-knight-patches/src/skins/HollowKnightSkinRuntime.cs`
- `tools/hollow-knight-patches/src/skins/bindings/SkinBindingAuthority.cs`
- `tools/hollow-knight-patches/src/skins/bindings/HollowKnightSkinApi.cs`
- `tools/hollow-knight-patches/src/skins/bindings/HeroSpriteBinding.cs`
- `tools/hollow-knight-patches/src/skins/bindings/Tk2dMaterialBinding.cs`
- `tools/hollow-knight-patches/src/skins/bindings/BoundedMaterialBinding.cs`
- `tools/hollow-knight-patches/src/skins/bindings/HookTextureBinding.cs`
- `tools/hollow-knight-patches/src/skins/bindings/NoCacheRebindBinding.cs`
- `tools/hollow-knight-patches/src/dualsouls/HollowKnightSkinPresenter.cs`
**Files — Test:**
- `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/session/SkinLaunchIntentContractTest.kt`
- `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/session/SkinSessionBridgeTest.kt`
- `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/session/SkinLaunchWiringTest.kt`
- `tools/shared-patches-tests/HollowKnightSkinTransactionOrchestratorTests.cs`
- `tools/shared-patches-tests/HollowKnightSkinRuntimeCoordinatorTests.cs`
- `tools/shared-patches-tests/HollowKnightSkinSessionPortTests.cs`
- `tools/ci/tests/test_hollow_knight_skin_policy.py`
**Files — Modify:**
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/LauncherActivity.kt`
- `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/runtime/GameProcessStartup.kt`
- `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/runtime/GameProcessStartupTest.kt`
- `tools/hollow-knight-patches/src/dualsouls/HkStageHooks.cs`
- `tools/hollow-knight-patches/src/dualsouls/HKDualScreen.cs`
- `tools/hollow-knight-patches/src/dualsouls/HKDualScreen.Bottom.Frame.cs`
- `tools/hollow-knight-patches/src/dualsouls/HollowKnightModsPresenter.cs`
- `tools/hollow-knight-patches/entrypoints.json`
**Contract/API outline:**
```csharp
public enum ResultDisposition { Invalid = 0, Success = 1, Failure = 2, Blocked = 3 }
public readonly struct AdapterReadiness
{
    public ResultDisposition Disposition { get; } public bool IsValid { get; } public bool Authoritative { get; } public string BlockingCode { get; }
    private AdapterReadiness(ResultDisposition disposition, string code);
    public static AdapterReadiness Success(); public static AdapterReadiness Failure(string code); public static AdapterReadiness Blocked(string code);
}
public readonly struct SkinResourceBudget { public readonly long ScratchBytes, PlanBytes, ProcessBytes; public readonly int PlanAllocations, ProcessAllocations; public SkinResourceBudget(long scratch, long plan, long process, int planAllocations, int processAllocations); }
public interface IPreparedTexture : IDisposable { int Width { get; } int Height { get; } long ResidentBytes { get; } }
public interface IPreparedRow { SkinCatalogDescriptor Descriptor { get; } IPreparedTexture Texture { get; } }
public interface IRollbackRow { SkinCatalogDescriptor Descriptor { get; } object BorrowedDefault { get; } }
public interface IPreparedPlan : IDisposable { IReadOnlyList<IPreparedRow> ForwardRows { get; } IReadOnlyList<IRollbackRow> ReverseRollbackRows { get; } }
public interface IPreparedRecoveryPlan : IDisposable { ActiveVisual PriorVisual { get; } IReadOnlyList<IPreparedRow> ReverseRows { get; } }
public readonly struct PostconditionResult { public ResultDisposition Disposition { get; } public bool IsValid { get; } public bool Verified { get; } public string Code { get; } private PostconditionResult(ResultDisposition disposition, string code); public static PostconditionResult Success(); public static PostconditionResult Failure(string code); public static PostconditionResult Blocked(string code); }
public readonly struct PrepareResult { public ResultDisposition Disposition { get; } public bool IsValid { get; } public bool Succeeded { get; } public IPreparedPlan Plan { get; } public string Code { get; } private PrepareResult(ResultDisposition disposition, IPreparedPlan plan, string code); public static PrepareResult Success(IPreparedPlan plan); public static PrepareResult Failure(string code); public static PrepareResult Blocked(string code); }
public readonly struct RecoveryPrepareResult { public ResultDisposition Disposition { get; } public bool IsValid { get; } public bool Succeeded { get; } public IPreparedRecoveryPlan Plan { get; } public string Code { get; } private RecoveryPrepareResult(ResultDisposition disposition, IPreparedRecoveryPlan plan, string code); public static RecoveryPrepareResult Success(IPreparedRecoveryPlan plan); public static RecoveryPrepareResult Failure(string code); public static RecoveryPrepareResult Blocked(string code); }
public readonly struct ExecuteResult { public ResultDisposition Disposition { get; } public bool IsValid { get; } public bool Succeeded { get; } public long CommittedStamp { get; } public string Code { get; } private ExecuteResult(ResultDisposition disposition, long stamp, string code); public static ExecuteResult Success(long stamp); public static ExecuteResult Failure(string code); public static ExecuteResult Blocked(string code); }
public readonly struct RecoveryResult { public ResultDisposition Disposition { get; } public bool IsValid { get; } public bool Succeeded { get; } public long EstablishedStamp { get; } public string Code { get; } private RecoveryResult(ResultDisposition disposition, long stamp, string code); public static RecoveryResult Success(long stamp); public static RecoveryResult Failure(string code); public static RecoveryResult Blocked(string code); }
public interface IHollowKnightSkinApi : IDisposable
{
    AdapterReadiness Readiness { get; }
    SkinBindingToken CaptureBindingAndDefaults(IReadOnlyList<SkinCatalogDescriptor> catalog);
    IPreparedTexture Decode(DescriptorTextureEnvelope source, SkinResourceBudget budget);
    IPreparedRow PrepareRow(SkinCatalogDescriptor descriptor, IPreparedTexture desired);
    IRollbackRow CaptureRollback(SkinCatalogDescriptor descriptor);
    void Apply(IPreparedRow row, SkinBindingToken token);
    void Restore(IRollbackRow row, SkinBindingToken token);
    void RestorePrepared(IPreparedRow row, SkinBindingToken token);
    PostconditionResult VerifyComplete(IPreparedPlan plan, SkinBindingToken token);
    PostconditionResult VerifyRecovery(IPreparedRecoveryPlan plan, SkinBindingToken token);
}
public readonly struct SessionLeaseResult { public ResultDisposition Disposition { get; } public bool IsValid { get; } public bool Succeeded { get; } public LeaseHead Head { get; } public string Code { get; } private SessionLeaseResult(ResultDisposition disposition, LeaseHead head, string code); public static SessionLeaseResult Success(LeaseHead head); public static SessionLeaseResult Failure(string code); public static SessionLeaseResult Blocked(string code); }
public readonly struct SessionCommitResult { public ResultDisposition Disposition { get; } public bool IsValid { get; } public bool Succeeded { get; } public RegistryCommitReceipt Receipt { get; } public string Code { get; } private SessionCommitResult(ResultDisposition disposition, RegistryCommitReceipt receipt, string code); public static SessionCommitResult Success(RegistryCommitReceipt receipt); public static SessionCommitResult Failure(string code); public static SessionCommitResult Blocked(string code); }
public readonly struct SessionFailureResult { public ResultDisposition Disposition { get; } public bool IsValid { get; } public bool Persisted { get; } public RegistryCommitReceipt Receipt { get; } public string Code { get; } private SessionFailureResult(ResultDisposition disposition, RegistryCommitReceipt receipt, string code); public static SessionFailureResult Success(RegistryCommitReceipt receipt); public static SessionFailureResult Failure(string code); public static SessionFailureResult Blocked(string code); }
public sealed class RecoveryAuthorization { public string DescriptorId { get; } public string LeaseId { get; } public string ExpectedGenerationId { get; } public string ExpectedGenerationSha256 { get; } public string ExpectedInterlockSha256 { get; } public string TransactionId { get; } public SkinBindingToken Binding { get; } private RecoveryAuthorization(string descriptorId, string leaseId, string expectedGenerationId, string expectedGenerationSha256, string expectedInterlockSha256, string transactionId, SkinBindingToken binding); public static RecoveryAuthorization Create(string descriptorId, string leaseId, string expectedGenerationId, string expectedGenerationSha256, string expectedInterlockSha256, string transactionId, SkinBindingToken binding); }
public readonly struct SessionRecoveryAuthorizationResult { public ResultDisposition Disposition { get; } public bool IsValid { get; } public bool Authorized { get; } public RecoveryAuthorization Authorization { get; } public string Code { get; } private SessionRecoveryAuthorizationResult(ResultDisposition disposition, RecoveryAuthorization authorization, string code); public static SessionRecoveryAuthorizationResult Success(RecoveryAuthorization authorization); public static SessionRecoveryAuthorizationResult Failure(string code); public static SessionRecoveryAuthorizationResult Blocked(string code); }
public abstract class SkinModeOnlyClosure
{
    private SkinModeOnlyClosure() { }
    public sealed class OnToRotate : SkinModeOnlyClosure { public ActivationSnapshot Expected { get; } public string ExpectedGenerationId { get; } public string ExpectedGenerationSha256 { get; } public OnToRotate(ActivationSnapshot expected, string expectedGenerationId, string expectedGenerationSha256); }
    public sealed class VerifiedVanillaToOff : SkinModeOnlyClosure { public ActivationSnapshot Expected { get; } public VerifiedLiveVisualProof.Vanilla Proof { get; } public string ExpectedGenerationId { get; } public string ExpectedGenerationSha256 { get; } public VerifiedVanillaToOff(ActivationSnapshot expected, VerifiedLiveVisualProof.Vanilla proof, string expectedGenerationId, string expectedGenerationSha256); }
}
public interface IHollowKnightSkinSessionPort
{
    SessionLeaseResult Claim(SkinLaunchDescriptor descriptor); SessionLeaseResult Close(SkinLaunchDescriptor descriptor, string reason);
    SessionCommitResult Arm(TransactionEnvelope envelope); SessionCommitResult Complete(TransactionCorrelation correlation, RegistryCommitReceipt armReceipt, ActivationSnapshot closure);
    SessionCommitResult CommitModeOnly(SkinLaunchDescriptor descriptor, SkinModeOnlyClosure closure);
    SessionFailureResult PersistFailure(TransactionCorrelation correlation, RegistryCommitReceipt armReceipt, string originalCode, string rollbackCode);
    SessionRecoveryAuthorizationResult AuthorizeRecovery(SkinLaunchDescriptor descriptor, RotationInterlock interlock, SkinBindingToken freshBinding);
    SessionCommitResult CompleteRecovery(RecoveryAuthorization authorization, ActivationSnapshot closure);
    SessionFailureResult PersistFailure(RecoveryAuthorization authorization, string recoveryCode);
}
public sealed class HollowKnightSkinPlanPreparer
{
    public PrepareResult Prepare(SkinLaunchDescriptor descriptor, ActiveVisual desired, SkinBindingToken binding);
    public RecoveryPrepareResult PrepareRecordedPrior(SkinLaunchDescriptor descriptor, RotationInterlock interlock, SkinBindingToken freshBinding);
}
public sealed class HollowKnightSkinTransactionOrchestrator
{
    public HollowKnightSkinTransactionOrchestrator(IHollowKnightSkinApi api, IHollowKnightSkinSessionPort sessionPort);
    public ExecuteResult Execute(IPreparedPlan desired, IPreparedPlan prior, TransactionEnvelope envelope);
    public RecoveryResult RecoverRecordedPrior(SkinLaunchDescriptor descriptor, IPreparedRecoveryPlan preparedPrior, RotationInterlock interlock, SkinBindingToken freshBinding);
}
public readonly struct CoordinatorDecision { public ResultDisposition Disposition { get; } public bool IsValid { get; } public bool Succeeded { get; } public bool WritesBlocked { get; } public long SkinStamp { get; } public string Code { get; } private CoordinatorDecision(ResultDisposition disposition, long stamp, string code); public static CoordinatorDecision Success(long stamp); public static CoordinatorDecision Failure(long stamp, string code); public static CoordinatorDecision Blocked(long stamp, string code); }
public sealed class HollowKnightSkinRuntimeCoordinator
{
    public HollowKnightSkinRuntimeCoordinator(IHollowKnightSkinSessionPort sessionPort);
    public CoordinatorDecision Start(SkinLaunchDescriptor descriptor, IHollowKnightSkinApi api);
    public CoordinatorDecision HandleSession(CoreSessionEvent sessionEvent); public CoordinatorDecision HandleBindingChanged(SkinBindingToken binding);
    public CoordinatorDecision HandleRotation(RotationEvent rotationEvent); public CoordinatorDecision AcknowledgeCloneInvalidation(long skinStamp);
    public CoordinatorDecision DisposeWriteFree();
}
public sealed class HollowKnightSkinLifecycleAdapter
{
    public AdapterReadiness Readiness { get; }
    public LifecycleDecision BeforeDeath(HeroBindingToken hero);
    public LifecycleDecision AfterDeath(HeroBindingToken hero);
    public LifecycleDecision HeroUpdate(HeroObservation observation);
}
```
```kotlin
class ConsumedSkinLaunchEnvelope internal constructor(val schemaVersion: Int, val profileId: String, val descriptorId: String, val descriptorSha256: String, val descriptorPath: String, val sessionSequence: Long, val leaseId: String, val leaseToken: String) { override fun toString(): String = "ConsumedSkinLaunchEnvelope(redacted)" }
object SkinLaunchIntentContract {
    const val SCHEMA_VERSION = 1
    const val EXTRA_SCHEMA = "dev.silksong.launcher.hkskin.SCHEMA"; const val EXTRA_PROFILE = "dev.silksong.launcher.hkskin.PROFILE"
    const val EXTRA_DESCRIPTOR_ID = "dev.silksong.launcher.hkskin.DESCRIPTOR_ID"; const val EXTRA_DESCRIPTOR_SHA256 = "dev.silksong.launcher.hkskin.DESCRIPTOR_SHA256"; const val EXTRA_DESCRIPTOR_PATH = "dev.silksong.launcher.hkskin.DESCRIPTOR_PATH"
    const val EXTRA_SESSION_SEQUENCE = "dev.silksong.launcher.hkskin.SESSION_SEQUENCE"; const val EXTRA_LEASE_ID = "dev.silksong.launcher.hkskin.LEASE_ID"; const val EXTRA_LEASE_TOKEN = "dev.silksong.launcher.hkskin.LEASE_TOKEN"
    fun attachHollowKnight(intent: Intent, profileId: String, handle: SkinLaunchHandle): SkinResult<Unit>
}
object SkinSessionBridge {
    @JvmStatic fun consumeLaunchIntent(activity: Activity): SkinResult<ConsumedSkinLaunchEnvelope>
    @JvmStatic fun openAndClaim(envelope: ConsumedSkinLaunchEnvelope): String
    @JvmStatic fun commit(envelope: ConsumedSkinLaunchEnvelope, requestCanonicalJson: String): String
    @JvmStatic fun close(envelope: ConsumedSkinLaunchEnvelope, reason: String): String
}
```
- Non-core `HollowKnightSkinJvmBridge : IHollowKnightSkinSessionPort` is the sole production port. Its constructor accepts only the one `ConsumedSkinLaunchEnvelope` returned by `SkinSessionBridge.consumeLaunchIntent`; it verifies the raw 256-bit token against the descriptor hash binding, keeps it only in process memory, and converts strict canonical Kotlin replies into the validated typed factories. Kotlin replies contain disposition, closed code, accepted registry generation/digest, lease transition/head, and message; each call recomputes self `{uid,pid,startToken}`, token/descriptor/head, registry parent, and exact lease. The port exposes only claim/close, transaction Arm/Complete/failure CAS, closed mode-only CAS, and recovery authorization/completion/failure CAS—never visual work, scanning, or arbitrary writes.
- Every file under `src/skins/core/` is C# 9, Unity/game/JVM-free and enters host tests through Task 3's wildcard. `IHollowKnightSkinApi`, preparation/orchestration, and the coordinator are fake-tested there; non-core shells use policy plus exact HK compile. Every Task 4 readonly result—`AdapterReadiness`, `PostconditionResult`, `PrepareResult`, `RecoveryPrepareResult`, `ExecuteResult`, `RecoveryResult`, `SessionLeaseResult`, `SessionCommitResult`, `SessionFailureResult`, `SessionRecoveryAuthorizationResult`, and `CoordinatorDecision`—has only a validating private constructor and named `Success`/`Failure`/`Blocked` factories. Success sets `Disposition.Success`, `IsValid=true`, empty code, and its required non-null plan/head/receipt/authorization or nonnegative stamp; Failure/Blocked set their matching disposition, `IsValid=true`, nonempty closed code, null reference payloads and zero payload-only stamps (`CoordinatorDecision` retains the current stamp). `SessionFailureResult.Success` uniquely means a non-null persisted receipt. `default` has `Invalid`, `IsValid=false`, is never success, and every consumer rejects it before mutation, visual write, or publication.
- `HollowKnightSkinRuntime : MonoBehaviour` is one `DontDestroyOnLoad` process owner that delegates decisions to the coordinator and owns immutable descriptor/session, adapters, `long SkinStamp`, Unity resources, and teardown. Display detach neither disposes it nor cancels pending rotation.
- `HollowKnightSkinApi` uses only exact typed `1.5.12620` singleton/hook payloads and bounded descendants named by the 205 descriptors. Prepare resolves all rows, captures defaults/rollback, decodes mapped files, fills every unmapped row with vanilla, and mutates nothing until ARMED is durably confirmed.
- For ordinary transactions, mutation-free preparation precedes `Arm`; the orchestrator rejects invalid/default results, validates the exact base→ARMED receipt before the first API write, rechecks binding before every catalog write/postcondition, calls `Complete` only after full visual verification, validates exact ARMED→closure receipt, and only then publishes in-memory visual/stamp, invalidates clones, or retires resources. Same-binding apply failure restores captured rows in reverse, verifies prior, then closes through `Complete`; detachment/rollback failure uses exact-receipt `PersistFailure`. Any failed/mismatched/indeterminate receipt blocks without premature write/publication; disposal is write-free.
- `SkinModeOnlyClosure` is closed by its private base constructor to exactly `OnToRotate` and `VerifiedVanillaToOff`. The coordinator maps only `CommitOnToRotate`/`CommitVerifiedVanillaOff` to one closure using its latest validated generation ID/digest, then calls `CommitModeOnly(descriptor,closure)`; the port verifies current lease plus that exact head and CASes directly from CLEAR without `Arm`, ARMED, `Complete`, or API calls. It validates receipt expected fields, canonical child head/closure, and new digest before publishing mode in memory; failure, default, or mismatch leaves the prior mode and blocks.
- Visual recovery is core-owned, never performed by Kotlin/JVM. `PrepareRecordedPrior` mutation-free validates the descriptor/interlock prior, resolves Vanilla or the exact hash-qualified current/`retainedActiveObject`, and builds all 205 reverse-ordered rows. `RecoverRecordedPrior(descriptor,preparedPrior,interlock,freshBinding)` first requires `AuthorizeRecovery` to lease/head-validate the byte-equal existing non-CLEAR interlock and return a binding/head-qualified authorization; only then may it `RestorePrepared` in reverse order, recheck binding, and `VerifyRecovery`. `CompleteRecovery` and the recovery `PersistFailure` overload are the only post-authorization port calls and exact-CAS that authorization head/interlock; exact completion receipt must prove the prior closure before in-memory visual/stamp publication, while restore/verify failure persists only the correlated failure. Coordinator permits at most one attempt per process/binding and never asks the port to prepare, restore, or verify visuals.
- `SkinBindingAuthority.Production` returns `AdapterReadiness.Blocked("HKSKIN-ADAPTER-001")`, and production lifecycle returns `AdapterReadiness.Blocked("HKSKIN-LIFECYCLE-001")` until approved live evidence exists. Host fakes exercise non-vanilla transactions, but production non-vanilla apply and live death rotation fail closed.
- Runtime creates `VerifiedLiveVisualProof` only after authoritative full-catalog default capture or verified complete postcondition on the exact current binding; descriptor/registry `active` never creates proof. `HandleBindingChanged` clears prior proof before mode/death handling, then only fresh verification may install it. ROTATE→OFF cancels pending, uses same-binding Vanilla proof for closed no-write `CommitModeOnly`, otherwise runs `MODE_OFF` ARMED restoration and publishes neither OFF nor stamp before exact completion. Startup OFF writes nothing, ON establishes selected with authority, ROTATE re-establishes durable active without advancing, and rebind retains one confirmed pending candidate for a fresh stable token.
- Resource arena decodes sequentially, discards readable CPU scratch before the next decode, deduplicates only verified identity, borrows defaults, and retires at most one displaced generation after durable commit plus clone-invalidation acknowledgement. `Dispose` is write-free.
- Change `HkStageHooks.SkinStamp` from `int` to delegated `long`, and `HKDualScreen.lastSkinStamp` to `long`. The existing comparison calls `InvalidateCompanionClones()`; only after it destroys old-stamp clones does `HollowKnightSkinPresenter.cs` acknowledge that stamp to `HollowKnightSkinRuntime.Current`.
- `HollowKnightSkinPresenter.cs` contains `public partial class HKDualScreen`, borrows `HollowKnightSkinRuntime.Current`, renders `SKIN: OFF|ON|ROTATE`, selected name, eligible count/no-op, pending, and error, and its sole mode action routes payload-free `AdvanceMode`. It creates no presenter type/runtime/store/adapter or per-mode/rotation-toggle/random/shuffle action. `HollowKnightModsPresenter` gains only narrow calls to tick, route that one Skin row, and detach this borrowed view; H3 Mods master remains independent.
- Launch wiring is exact-HK-only. After every save/mod/other mutable launch write, `LauncherActivity` acquires the handle, calls `SkinLaunchIntentContract.attachHollowKnight`, and adds only schema=1, profile, descriptor ID/SHA-256/path, session sequence, lease ID, and raw 256-bit token extras to the exact game-component Intent immediately before `startActivity`, with no intervening work; a definitive attach/start failure removes extras and closes that exact pending lease. Silksong performs its unchanged launch, adds no H4 extra, and never consumes skin authority.
- `GameProcessStartup.prepare(Application)` performs only pre-Activity game-process setup and neither sees nor claims an Activity Intent. After the Unity Activity exists, first `Bootstrap` calls `SkinSessionBridge.consumeLaunchIntent(activity)`, which atomically accepts only the exact game process/HK profile/component, complete fields, descriptor file/digest/identity/session/lease, pending lease, and token hash; installs one immutable process-memory envelope; and removes every H4 extra from the Intent in `finally`. Replay/mismatch rejects, definitive failure closes the identified pending lease, and uncertainty blocks. The token is excluded from `toString`, canonical replies/descriptor, persistence, logs, and exceptions; only `HollowKnightSkinJvmBridge` receives the returned envelope. A repeated `Bootstrap` reuses the existing runtime before calling consume and cannot re-expose the token.
- Add exactly one entrypoint tuple `{nameSpace:"HollowKnightPatches",className:"HollowKnightSkinRuntime",methodName:"Bootstrap",loadTypes:0}`. `HollowKnightPatches.HollowKnightSkinRuntime` exposes `public static void Bootstrap()`; policy asserts tuple uniqueness and no project-file change is needed because current references are sufficient.

- [ ] **Red — write bridge/coordinator/wiring/presenter-mode tests and shell policy assertions.** Use recording fake API/ports and define `everyReadonlyResultFactoryConstructsNondefaultSuccessFailureAndBlocked`, `defaultReadonlyResultsAreInvalidAndRejectedBeforeEffects`, `bridgeClaimsOnlyExactPendingDescriptor`, `bridgeRejectsStaleLeaseAndRegistryParent`, `fakePortClaimPrecedesCoordinatorWork`, `coreSessionPortIsUnityAndJvmFree`, `fakePortOrderIsClaimPrepareArmWriteVerifyCompletePublish`, `fakePortArmReceiptPrecedesFirstApiWrite`, `armFailureOrReceiptMismatchCausesZeroApiWrites`, `coordinatorAppliesMappedAndVanillaForAll205Rows`, `coordinatorRollsBackChangedRowsInReverse`, `coordinatorDoesNotPublishVerifiedRollbackBeforeClosureCommit`, `fakePortCompleteFollowsVisualVerificationAndPrecedesPublication`, `completionFailureOrReceiptMismatchPublishesNoVisualOrStamp`, `onToRotateModeOnlyUsesZeroArmApiWritesOrComplete`, `verifiedVanillaOffModeOnlyUsesZeroArmApiWritesOrComplete`, `modeOnlyExactReceiptPrecedesInMemoryModePublication`, `modeOnlyReceiptMismatchBlocksWithoutPublication`, `preparesRecordedNonVanillaPriorFromExactRetainedOrCurrentObject`, `recoveryAuthorizationPrecedesFirstRestoreWrite`, `recoveryRestoresPreparedRowsInReverseAndVerifies`, `recoveryCompletionPrecedesVisualAndStampPublication`, `recoveryAttemptIsAtMostOncePerProcessBinding`, `recoveryPortNeverPerformsVisualWork`, `coordinatorBlocksIndeterminateOrRollbackFailure`, `coordinatorWaitsForCloneAcknowledgement`, `coordinatorDisposeIsWriteFree`, `productionAdaptersRemainBlocked`, `presenterRoutesOnlyPayloadFreeAdvanceMode`, `presenterHasNoDirectModeSetterSkipReverseToggleRandomOrShufflePath`, `coordinatorNeverInfersVanillaFromRegistryActive`, `coordinatorNonVanillaRotateOffCancelsThenWaitsForCompletion`, `coordinatorBindingChangeInvalidatesPriorLiveProof`, `jvmBridgeIsSoleNonCoreSessionPortImplementation`, `attachesExactHkExtrasAfterAllWritesImmediatelyBeforeStartActivity`, `consumesRawTokenExactlyOnceAfterUnityActivityExists`, `clearsEveryHkExtraAfterConsume`, `rejectsIntentReplayComponentProfileDigestDescriptorAndTokenMismatch`, `rawTokenNeverPersistsLogsOrEntersCanonicalDescriptor`, `consumeFailureClosesExactPendingLease`, `repeatedBootstrapReusesRuntimeWithoutTokenReexposure`, `gameProcessStartupApplicationNeverReadsActivityIntent`, `silksongIntentHasNoSkinExtrasAndNeverConsumes`, `presenterIsPartialBorrowedRuntimeOnly`, and `entrypointTupleIsExactAndUnique`.
- [ ] **Run actual red gates, then the expected-green Task 3 core regression.** Execute:
```bash
java -classpath 'D:\Temp\dualsouls-unity-player\android\Tools\gradle\lib\gradle-launcher-8.11.jar' org.gradle.launcher.GradleMain -p src/SilksongLauncher.Launcher :app:testDebugUnitTest --tests 'dev.silksong.launcher.skins.session.SkinLaunchIntentContractTest' --tests 'dev.silksong.launcher.skins.session.SkinSessionBridgeTest' --tests 'dev.silksong.launcher.skins.session.SkinLaunchWiringTest' --tests 'dev.silksong.launcher.runtime.GameProcessStartupTest'
dotnet test tools/shared-patches-tests/SharedPatches.Tests.csproj -c Release --filter 'FullyQualifiedName~HollowKnightSkinTransactionOrchestratorTests|FullyQualifiedName~HollowKnightSkinRuntimeCoordinatorTests|FullyQualifiedName~HollowKnightSkinSessionPortTests'
python -m unittest tools.ci.tests.test_hollow_knight_skin_policy -v
dotnet build tools/hollow-knight-skin-core-check/HollowKnightSkinCore.Check.csproj -c Release
```
Expected: commands 1–3 exit nonzero for absent validated factories, closed mode-only/recovery ports, one-use Intent/bridge, coordinator/wiring, and presenter contracts; command 4 exits 0 as the expected-green C# 9/netstandard2.1 Task 3 core regression.

- [ ] **Green — implement Unity-free API/plans/port/transactions/coordinator under `src/skins/core/`.** First implement every listed private validating result constructor/factory and reject `default`; make recording fakes and production construct each nondefault disposition. Inject the port, require exact `Arm` before ordinary writes and exact `Complete` after verification; dispatch the two mode-only commands only through lease-qualified exact-head `CommitModeOnly` with no Arm/ARMED/API/Complete and publish mode only after receipt validation. Prepare descriptor-qualified Vanilla/current/retained prior plans mutation-free, authorize recovery before reverse restore/verify, complete exact recovery CAS before publication, and enforce at-most-once process/binding recovery. Preserve 205-row, proof, rollback, stamp/resource/clone, and write-free-disposal contracts without Unity/JVM/game references.
- [ ] **Green — implement Kotlin Intent/bridge and non-core production port/shells.** Implement the exact one-use `SkinLaunchIntentContract` and process-memory envelope, then make `HollowKnightSkinJvmBridge` the sole port and sole envelope recipient. Translate strict claim/close, Arm/Complete/failure, closed mode-only, and authorization/completion/failure recovery CAS replies; reject invalid/default typed results, map uncertainty to Blocked, never let Kotlin/JVM perform visual recovery, and never persist/log/canonicalize the token. Implement bounded bindings, sequential Unity arena, and thin shells.
- [ ] **Green — implement lifecycle hook adapter/runtime.** Route exact hooks through pure core; on binding change call `HandleBindingChanged` to stale proof before preserving pending candidate/epoch with new tokens, retain blocked production readiness, and detach hooks/release zero-ref resources/close lease without teardown writes.
- [ ] **Green — integrate presenter/stamp/exact manual entrypoint.** Add the exact partial and sole payload-free action, narrow H3 calls, long stamp flow, synchronous clone acknowledgement, and one entrypoint tuple. `Bootstrap()` first reuses an existing runtime; otherwise, only after obtaining the Unity Activity, it calls `SkinSessionBridge.consumeLaunchIntent(activity)` exactly once and gives that immutable envelope directly to the new bridge/runtime.
- [ ] **Green — wire launch only after all prior Task 4 pieces exist.** Guard all skin work by exact HK profile; leave `GameProcessStartup.prepare(Application)` Intent-blind. After every other launch write, acquire and attach the exact extras with no intervening action before `startActivity`; on definitive attach/start/consume failure clear extras and close the identified pending lease. Prove Silksong has no extras, consume, or skin path.
- [ ] **Run focused host verification.** Repeat all four commands; each exits 0 with zero failures. Policy scans only H4 target-discovery files and does not reject existing H3 presenter reflection.
- [ ] **Run both authoritative compile checks.** Execute exactly:

```bash
pwsh -NoProfile -File tools/hollow-knight-patches/check.ps1 -Depot 'D:\Temp\dualsouls-hk-12620\Hollow Knight\hollow_knight_Data\Managed' -Player 'D:\Temp\dualsouls-unity-player\android\Variations\il2cpp\Managed'
pwsh -NoProfile -File tools/silksong-patches/check.ps1 -Depot 'D:\Temp\hkandroid-task11-silksong-managed' -Player 'D:\Temp\dualsouls-unity-player\android\Variations\il2cpp\Managed'
```
Expected: both exit 0. The HK compile proves exact symbols for target `1.5.12620`; accepting `1.5.12612` source compatibility in `HollowKnightProfile` does not alter H4 identity. The Silksong compile is regression evidence only and does not include/enable HK skin core/runtime files.

- [ ] **Run batch-wide host regressions.** Execute:
```bash
java -classpath 'D:\Temp\dualsouls-unity-player\android\Tools\gradle\lib\gradle-launcher-8.11.jar' org.gradle.launcher.GradleMain -p src/SilksongLauncher.Launcher :app:testDebugUnitTest
dotnet build tools/hollow-knight-skin-core-check/HollowKnightSkinCore.Check.csproj -c Release
dotnet test tools/shared-patches-tests/SharedPatches.Tests.csproj -c Release
python -m unittest discover -s tools/ci/tests -p 'test_hollow_knight_skin_*.py' -v
```
Expected: all exit 0 with zero failures; Python source assertions cover the non-core Unity shell and presenter without compiling either into host tests.

- [ ] **Remove disposable outputs, inspect the patch, commit once, and push.** Execute:
```bash
rm -rf src/SilksongLauncher.Launcher/.gradle src/SilksongLauncher.Launcher/app/build tools/shared-patches-tests/bin tools/shared-patches-tests/obj tools/hollow-knight-skin-core-check/bin tools/hollow-knight-skin-core-check/obj tools/hollow-knight-patches/bin tools/hollow-knight-patches/obj tools/silksong-patches/bin tools/silksong-patches/obj tools/ci/tests/__pycache__
git diff --check
git status --short
git add -- src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/session/SkinLaunchIntentContract.kt src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/session/SkinSessionBridge.kt src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/LauncherActivity.kt src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/runtime/GameProcessStartup.kt src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/session/SkinLaunchIntentContractTest.kt src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/session/SkinSessionBridgeTest.kt src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/session/SkinLaunchWiringTest.kt src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/runtime/GameProcessStartupTest.kt tools/hollow-knight-patches/src/skins tools/hollow-knight-patches/src/dualsouls/HollowKnightSkinPresenter.cs tools/hollow-knight-patches/src/dualsouls/HkStageHooks.cs tools/hollow-knight-patches/src/dualsouls/HKDualScreen.cs tools/hollow-knight-patches/src/dualsouls/HKDualScreen.Bottom.Frame.cs tools/hollow-knight-patches/src/dualsouls/HollowKnightModsPresenter.cs tools/hollow-knight-patches/entrypoints.json tools/shared-patches-tests/HollowKnightSkinTransactionOrchestratorTests.cs tools/shared-patches-tests/HollowKnightSkinRuntimeCoordinatorTests.cs tools/shared-patches-tests/HollowKnightSkinSessionPortTests.cs tools/ci/tests/test_hollow_knight_skin_policy.py
git commit -m 'feat: integrate fail-closed Hollow Knight skin runtime' -m 'Co-Authored-By: Claude <noreply@anthropic.com>'
git push fork HEAD:feature/h4-skins-host
```
Expected: exactly one Batch 4 commit is pushed after host tests and both compiles; no live-runtime claim is made.

### Task 5: Complete synthetic/fault/policy gates, exact corpus replay, reconciliation, and truthful closure
**Files — Test:**
- `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/importing/SkinImportSecurityMatrixTest.kt`
- `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/documents/SkinDocumentMaximumShapeTest.kt`
- `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/storage/SkinPublicationFaultMatrixTest.kt`
- `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/registry/SkinRegistryReconciliationMatrixTest.kt`
- `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/session/SkinSessionFaultMatrixTest.kt`
- `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/ui/SkinAccessibilityContractTest.kt`
- `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/corpus/HollowKnightSkinCorpusReplayTest.kt`
- `tools/shared-patches-tests/HollowKnightSkinHostMatrixTests.cs`
- `tools/ci/tests/test_hollow_knight_skin_corpus.py`
- `tools/ci/tests/test_hollow_knight_skin_reconciliation.py`
**Files — Create:**
- `tools/skins/replay_hollow_knight_skin_corpus.py`
- `tools/skins/reconcile_hollow_knight_skins_host.py`
- `docs/verification/hollow-knight-skins-host.md`
**Files — Modify (only when a new smallest Batch 5 regression exposes a written invariant):**
- Kotlin H4 roots: `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/contracts/`, `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/catalog/`, `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/importing/`, `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/documents/`, `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/storage/`, `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/registry/`, `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/session/`, `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/quota/`, `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/ui/`, and `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins/core/`.
- Launcher integration: `src/SilksongLauncher.Launcher/app/build.gradle.kts`, `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/profiles/ProfilePaths.kt`, `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/LauncherActivity.kt`, `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/SettingsActivity.kt`, `src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/runtime/GameProcessStartup.kt`, `src/SilksongLauncher.Launcher/app/src/main/AndroidManifest.xml`, `src/SilksongLauncher.Launcher/app/src/main/res/layout/activity_settings.xml`, `src/SilksongLauncher.Launcher/app/src/main/res/layout/activity_skins.xml`, and `src/SilksongLauncher.Launcher/app/src/main/res/values/strings.xml`.
- HK runtime: `tools/hollow-knight-patches/src/skins/core/`, `tools/hollow-knight-patches/src/skins/bindings/`, `tools/hollow-knight-patches/src/skins/HollowKnightSkinResourceArena.cs`, `tools/hollow-knight-patches/src/skins/HollowKnightSkinLifecycleAdapter.cs`, `tools/hollow-knight-patches/src/skins/HollowKnightSkinJvmBridge.cs`, `tools/hollow-knight-patches/src/skins/HollowKnightSkinRuntime.cs`, `tools/hollow-knight-patches/src/dualsouls/HollowKnightSkinPresenter.cs`, `tools/hollow-knight-patches/src/dualsouls/HkStageHooks.cs`, `tools/hollow-knight-patches/src/dualsouls/HKDualScreen.cs`, `tools/hollow-knight-patches/src/dualsouls/HKDualScreen.Bottom.Frame.cs`, `tools/hollow-knight-patches/src/dualsouls/HollowKnightModsPresenter.cs`, and `tools/hollow-knight-patches/entrypoints.json`.
- Tests/policy/data: all files under `src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins/`; `tools/shared-patches-tests/HollowKnightSkinCatalogTests.cs`, `HollowKnightSkinTransactionCoreTests.cs`, `HollowKnightSkinRotationCoreTests.cs`, `HollowKnightSkinLifecycleCoreTests.cs`, `HollowKnightSkinResourceCoreTests.cs`, `HollowKnightSkinGoldenTests.cs`, `HollowKnightSkinLaunchDescriptorTests.cs`, `HollowKnightSkinTransactionOrchestratorTests.cs`, `HollowKnightSkinRuntimeCoordinatorTests.cs`, `HollowKnightSkinSessionPortTests.cs`, and `HollowKnightSkinHostMatrixTests.cs`; `tools/ci/tests/test_hollow_knight_skin_policy.py`; and all files under `tools/skin-goldens/v1/`.
- Closure only after final verification: `docs/superpowers/specs/2026-09-02-hollow-knight-skins-design.md` status/ledger cells.
**Corpus constants to encode literally in both Kotlin and Python:**

```text
Daughter of Hallownest 1.2.zip | aace82ede0f928e6fa86de2875e11b3c26a1e491289483356d62ca9e8f12c243 | ZIP | 46
Grimm_Knight by 复印纸-20260829T012747Z-1-001.zip | 11f88e1d946f070cbb43b15df6192fcd64711b5098686fd45102eca1cf499a17 | ZIP | 9
Hylian Knight-20260829T014209Z-1-001.zip | 53fd262462b4b4444827c3674d4248a44df2df33a15b810118f9dd61bfa67dc5 | ZIP | 43
IllyiaKnight-20260829T014227Z-1-001.zip | ff5dc79a687483473507b06964987f46c45c52e2b68f631d4a4b396dc8e5585a | ZIP | 24
Isaac Knight Skin-20260829T014234Z-1-001.zip | 8c2f319ee09a0527ea0984b62a07ce5bcefb00b34110dc691e244dca312cbcca | ZIP | 138
Little Radiance (by HBKit)-20260829T012546Z-1-001.zip | e2ffe325a1d9d54ae187165921d9ab73eda4a4287a580fb9569b923aaef3998b | ZIP | 65
The Hollow Knight Skin Pack-20260829T021826Z-1-001.zip | 931dace39b1dfcc801fbbfdcbb5a96bb164ca2859bbe08912cd56b6b31404b20 | ZIP | 65,74,78
死亡细胞.zip | 06497cffa9ac156a12e78ae7ee9fe5a1cf733b243079498b98c021223ead1e07 | ZIP | 14
Biboo V0.6.rar | d4024d6b0e0be9122f5eac976340c4b395efc1c8414b961a8e5adee3618cbe10 | RAR4 | UNSUPPORTED_RAR
The novelist (1).rar | bee28d2193757b444c0673c6ce530413ac939a61abc95defa6f54b874899744f | RAR5 | UNSUPPORTED_RAR
```

- Python snapshots exact `relativePath,size,mtimeNs,fileAttributes,sha256` before work and in a `finally` block after every digest, Gradle, or assertion success/failure, then deletes every runner-owned temp profile/quarantine/result in an inner cleanup `finally`. Kotlin uses normal one-copy quarantine/coordinator into that isolated profile, verifies ten ZIP candidate counts and two unsupported RARs, and never writes beneath `G:`.
- `HollowKnightSkinCorpusReplayTest` uses `Assume.assumeNotNull(System.getenv("HOLLOW_KNIGHT_SKIN_CORPUS"))` only for ordinary tests. The callable Python runner rejects missing inputs, requires non-skipped Kotlin output, and returns canonical before/after/candidate evidence to its caller; all owned temp state is removed on every exit path.
- Synthetic matrices close every section 14 row: descriptor bit matrix, ZIP64/multi-disk/extra/path/collision/ratio/layout/mapping cases; canonical maximum shape and strict 8 MiB cross-language descriptor matrix; candidate isolation; every directory/pointer durability barrier; OPEN/CLAIMED/CLOSED import races and process-start orphan containment; registry/session ambiguity/races/PID reuse/pins/quota; payload-free sole-cycle effects, binding-qualified proof invalidation/no-active inference, validated result factories/default rejection, closed mode-only zero-Arm/write/Complete receipt ordering, ordinary Arm/write/Complete publication ordering, core-owned non-vanilla/vanilla recovery authorization/reverse restore/verification/completion, at-most-once recovery, exact one-use HK Intent transfer/extra clearing/failure close/Silksong isolation, tokens/visual-only rollback/exact closure receipts/stamps/resources; descriptor union; UI accessibility; profile separation.
- H4 target-discovery policy scans are restricted to `tools/hollow-knight-patches/src/skins/bindings` and runtime discovery calls in `tools/hollow-knight-patches/src/skins`. They ban native/offset/process/global scans, arbitrary reflection/name similarity, injected PlayerData/save writes, random/shuffle, dynamic Swap/text/cinematic discovery, and Silksong coupling. Presenter structure is checked separately, so pre-existing H3 presenter reflection is not a false failure.
- Pre-close reconciliation validates catalog/goldens/project isolation/exact entrypoint, calls corpus replay in-process with explicit corpus root and Gradle jar, then atomically writes (and removes on failure) a canonical temporary attestation with `skipped=false`, archive/source before+after snapshots, candidate results, catalog/golden digests, and source fingerprint `SHA256(ASCII("HKS-HOST-SOURCE-V1\0") || each U32BE(pathUtf8.length) || pathUtf8 || SHA256(file))` over unsigned-UTF-8-sorted `git ls-files --cached --others --exclude-standard`, excluding only `docs/superpowers/specs/2026-09-02-hollow-knight-skins-design.md` and `docs/verification/hollow-knight-skins-host.md`. Final reconciliation consumes it, recomputes that fingerprint, verifies exact status/evidence, and rejects any disconnect.

- [ ] **Red — add complete matrix/corpus/reconciliation assertions.** Name aggregate tests `coversEveryZipSecurityAndLayoutCase`, `coversMaximumCanonicalReceiptShape`, `faultsEveryPublicationAndPointerBarrier`, `reconcilesEveryRegistryAndLeaseTuple`, `coversCompleteRuntimePolicyMatrix`, `replaysExactReadOnlyCorpusThroughNormalQuarantine`, and `requiresOneCheckpointForAllHostEvidence`.
- [ ] **Run focused red gates without corpus.** Execute:
```bash
java -classpath 'D:\Temp\dualsouls-unity-player\android\Tools\gradle\lib\gradle-launcher-8.11.jar' org.gradle.launcher.GradleMain -p src/SilksongLauncher.Launcher :app:testDebugUnitTest --tests 'dev.silksong.launcher.skins.*' --tests 'dev.silksong.launcher.skins.**.*'
dotnet test tools/shared-patches-tests/SharedPatches.Tests.csproj -c Release --filter 'FullyQualifiedName~HollowKnightSkinHostMatrixTests'
python -m unittest discover -s tools/ci/tests -p 'test_hollow_knight_skin_*.py' -v
```
Expected: nonzero exits until missing matrix/reconciliation behavior is complete; the corpus test alone is explicitly skipped because the environment variable is absent.

- [ ] **Green — complete synthetic and fault matrices with bounded fixes.** When a new smallest regression exposes a written invariant, fix it only in the listed H4 production roots/integration files, add that regression, and rerun the focused gate; do not touch unrelated production.
- [ ] **Green — implement corpus runner and test.** Use nested `try/finally`: always take the post-run read-only snapshot before deleting owned temp profile/quarantine/result, and clean on digest, Gradle, assertion, or success exits; require exact candidate order/counts and unsupported results.
- [ ] **Green — implement two-phase policy/reconciliation.** Pre-close invokes replay in-process and writes the canonical attestation; final consumes it, recomputes the source fingerprint, requires exact evidence/status and entrypoint/project boundaries, and returns nonzero for skips, stale data, or source drift.
- [ ] **Run focused verification.** Repeat the red commands. Expected: Gradle/.NET/Python exit 0 with zero failures; only the ordinary corpus test is skipped.
- [ ] **Run one fresh full functional checkpoint.** Remove only generated outputs, then run the direct equivalents of the existing host gate, emulator-app host compile, all Python suites, and both exact compiles:

```bash
rm -rf src/SilksongLauncher.Launcher/.gradle src/SilksongLauncher.Launcher/app/build src/SilksongLauncher.Launcher/emulator-test-app/build tools/mod-weaver/bin tools/mod-weaver/obj tools/bundle-surgery/bin tools/bundle-surgery/obj tools/bundle-surgery-tests/bin tools/bundle-surgery-tests/obj tools/shared-patches-tests/bin tools/shared-patches-tests/obj tools/hollow-knight-skin-core-check/bin tools/hollow-knight-skin-core-check/obj tools/hollow-knight-patches/bin tools/hollow-knight-patches/obj tools/silksong-patches/bin tools/silksong-patches/obj tools/ci/tests/__pycache__ tools/skins/__pycache__
dotnet build tools/mod-weaver/ModWeaver.csproj -c Release
dotnet test tools/bundle-surgery-tests/BundleSurgery.Tests.csproj -c Release
java -classpath 'D:\Temp\dualsouls-unity-player\android\Tools\gradle\lib\gradle-launcher-8.11.jar' org.gradle.launcher.GradleMain -p src/SilksongLauncher.Launcher --no-daemon --rerun-tasks :app:testDebugUnitTest :emulator-test-app:compileDebugKotlin
dotnet test tools/shared-patches-tests/SharedPatches.Tests.csproj -c Release
dotnet build tools/hollow-knight-skin-core-check/HollowKnightSkinCore.Check.csproj -c Release
python -m unittest discover -s tools/ci/tests -p 'test_*.py' -v
pwsh -NoProfile -File tools/hollow-knight-patches/check.ps1 -Depot 'D:\Temp\dualsouls-hk-12620\Hollow Knight\hollow_knight_Data\Managed' -Player 'D:\Temp\dualsouls-unity-player\android\Variations\il2cpp\Managed'
pwsh -NoProfile -File tools/silksong-patches/check.ps1 -Depot 'D:\Temp\hkandroid-task11-silksong-managed' -Player 'D:\Temp\dualsouls-unity-player\android\Variations\il2cpp\Managed'
```
Expected: every command exits 0 with zero failures; ordinary Python may explicitly skip only the environment-gated corpus unit. Do not claim that `make` ran.

- [ ] **Run mandatory pre-close reconciliation and in-process corpus replay.** Execute:
```bash
rm -f /d/Temp/h4-skins-host-attestation.json
python tools/skins/reconcile_hollow_knight_skins_host.py --phase preclose --corpus-root 'G:\Modding\Downloads\Hollow Knight' --gradle-jar 'D:\Temp\dualsouls-unity-player\android\Tools\gradle\lib\gradle-launcher-8.11.jar' --attestation 'D:\Temp\h4-skins-host-attestation.json'
```
Expected: exit 0; reconciliation invokes replay in-process, all ten source hashes/snapshots and exact candidate results pass, `skipped=false`, and the canonical attestation contains the pre-close source fingerprint and catalog/golden digests.

- [ ] **Write evidence and close only host-qualified ledger truth.** From the attestation and functional command logs, record candidate fingerprint, UTC command times, commands/exits, corpus snapshots/results, catalog/golden digests, and both compiles in `docs/verification/hollow-knight-skins-host.md`. Change only design status/ledger cells to exactly `HOST-COMPLETE / ADAPTER-BLOCKED / LIFECYCLE-BLOCKED / DEVICE-PENDING`; retain RAR/browser deferrals and unauthorized device status.
- [ ] **Run final reconciliation with rollback-on-failure.** Execute:
```bash
if ! python tools/skins/reconcile_hollow_knight_skins_host.py --phase final --attestation 'D:\Temp\h4-skins-host-attestation.json' --evidence docs/verification/hollow-knight-skins-host.md; then
  git restore --source=HEAD -- docs/superpowers/specs/2026-09-02-hollow-knight-skins-design.md
  rm -f docs/verification/hollow-knight-skins-host.md /d/Temp/h4-skins-host-attestation.json
  exit 1
fi
```
Expected: exit 0 only when evidence/status cells are exact and recomputed source fingerprint equals pre-close. On failure, spec cells are restored, new evidence/attestation are removed, and no commit occurs.
- [ ] **Remove disposable outputs and prove the final patch is source/evidence only.** Execute:
```bash
rm -rf src/SilksongLauncher.Launcher/.gradle src/SilksongLauncher.Launcher/app/build src/SilksongLauncher.Launcher/emulator-test-app/build tools/mod-weaver/bin tools/mod-weaver/obj tools/bundle-surgery/bin tools/bundle-surgery/obj tools/bundle-surgery-tests/bin tools/bundle-surgery-tests/obj tools/shared-patches-tests/bin tools/shared-patches-tests/obj tools/hollow-knight-skin-core-check/bin tools/hollow-knight-skin-core-check/obj tools/hollow-knight-patches/bin tools/hollow-knight-patches/obj tools/silksong-patches/bin tools/silksong-patches/obj tools/ci/tests/__pycache__ tools/skins/__pycache__
rm -f /d/Temp/h4-skins-host-attestation.json
git diff --check
git status --short
```
Expected: diff check is silent and status lists only the explicitly permitted Batch 5 H4 production/integration fixes, tests/policy/tools/goldens, host evidence, and closure cells; no corpus archive, attestation, or generated binary is present.

- [ ] **Commit the single host-close checkpoint and push.** Execute:
```bash
git add -- src/SilksongLauncher.Launcher/app/build.gradle.kts src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/skins src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/profiles/ProfilePaths.kt src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/LauncherActivity.kt src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/SettingsActivity.kt src/SilksongLauncher.Launcher/app/src/main/kotlin/dev/silksong/launcher/runtime/GameProcessStartup.kt src/SilksongLauncher.Launcher/app/src/main/AndroidManifest.xml src/SilksongLauncher.Launcher/app/src/main/res/layout/activity_settings.xml src/SilksongLauncher.Launcher/app/src/main/res/layout/activity_skins.xml src/SilksongLauncher.Launcher/app/src/main/res/values/strings.xml src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/skins src/SilksongLauncher.Launcher/app/src/test/kotlin/dev/silksong/launcher/runtime/GameProcessStartupTest.kt tools/hollow-knight-patches/src/skins tools/hollow-knight-patches/src/dualsouls/HollowKnightSkinPresenter.cs tools/hollow-knight-patches/src/dualsouls/HkStageHooks.cs tools/hollow-knight-patches/src/dualsouls/HKDualScreen.cs tools/hollow-knight-patches/src/dualsouls/HKDualScreen.Bottom.Frame.cs tools/hollow-knight-patches/src/dualsouls/HollowKnightModsPresenter.cs tools/hollow-knight-patches/entrypoints.json ':(glob)tools/shared-patches-tests/HollowKnightSkin*.cs' tools/skin-goldens/v1 tools/skins/skin_golden_oracle.py tools/skins/replay_hollow_knight_skin_corpus.py tools/skins/reconcile_hollow_knight_skins_host.py tools/ci/tests/test_hollow_knight_skin_goldens.py tools/ci/tests/test_hollow_knight_skin_policy.py tools/ci/tests/test_hollow_knight_skin_corpus.py tools/ci/tests/test_hollow_knight_skin_reconciliation.py docs/verification/hollow-knight-skins-host.md docs/superpowers/specs/2026-09-02-hollow-knight-skins-design.md
git commit -m 'test: close Hollow Knight skin host gate' -m 'Co-Authored-By: Claude <noreply@anthropic.com>'
git push fork HEAD:feature/h4-skins-host
```
Expected: the fifth and final commit is pushed. Final truth is `HOST-COMPLETE / ADAPTER-BLOCKED / LIFECYCLE-BLOCKED / DEVICE-PENDING`; no stronger adapter, lifecycle, or device claim appears.

## Coverage matrix

| Design section / ledger ID | Batch | Concrete closure |
| --- | ---: | --- |
| §1 status/scope, §16 five-stage host order | 1–5 | Five implementation commits after the separate plan baseline; Batch 5 assigns only expected truth. |
| §2 modes, selection, eligibility, active receipt, deterministic ring | 2–4 | Payload-free sole cycle, exact effects, binding-qualified live proof/no receipt inference, deterministic ring, both ROTATE→OFF closures. |
| §3 evidence/authority, browser and RAR boundaries | 1, 5 | Magic-only unsupported RAR; no browser/download authority; deferred ledger remains open. |
| §4 fixed catalog | 1, 3, 5 | Pinned paths for mapping, 205 explicit descriptors, digest and cross-language checks. |
| §5.1 bounded raw ZIP | 1, 5 | Hand parser, descriptor matrix, raw path/extra/ratio limits, fault/security matrix. |
| §5.2 discovery/mapping/identity/replace | 1, 2, 5 | Finite layouts/aliases, one-copy singleton OPEN→CLAIMED handles, candidate ownership/idempotence, sole CAS Replace, corpus counts. |
| §5.3 strict documents | 1, 3, 5 | Canonical writer/parser, maximum receipt, independent goldens. |
| §5.4 paths/tree/PNG/bounds | 1, 5 | No-follow generated traversal, full decode, node/file/payload ceilings. |
| §5.5 canonical identities | 1, 3, 5 | Exact content/tree framing and three-language golden agreement. |
| §6 storage schemas | 1, 2, 5 | Immutable verified object/receipt plus canonical registry/descriptor/lease records. |
| §7 durability/registry recovery | 1, 2, 5 | One publisher, genesis/CAS/pointers, incomplete-handle orphan containment, every barrier faulted. |
| §8 descriptors/sessions/quota/retention | 2–5 | Strict ≤8 MiB Kotlin/C# descriptor+goldens, token hash plus exact one-use HK Intent delivery/clearing, exact identity/lease gate/reconciliation, pins/GC. |
| §9 runtime/typed boundaries | 3, 4, 5 | Unity/JVM-free core, validated constructible results, visual API+closed mode/recovery-authorizing port, sole non-core bridge, H4-scoped scan ban. |
| §10 resources/lifetime | 3–5 | Checked limits, dedup/refcounts, one retirement set, clone acknowledgement. |
| §11 write-ahead/stamp | 2–5 | Fakes prove mode-only CAS is zero-Arm/write/Complete; transactions prove Arm→write→verify→Complete→publish with rollback unpublished until closure; recovery proves authorize→reverse restore→verify→complete→publish. |
| §12 startup/rebind/lifecycle | 2–5 | Exact post-write/pre-start HK extras, Activity-time one-use consume with Intent-blind `GameProcessStartup.prepare(Application)`, lease/profile isolation, rebind proof invalidation, and three-update core; production proof remains blocked. |
| §13 UI/accessibility | 2, 4, 5 | Launcher library and borrowed game row expose one payload-free advance action with accessibility contracts. |
| §14 host/device gates | 1–5 | Full synthetic/corpus/reconciliation/compile gate; future combined device work is not executed. |
| §15 `HKSKIN-HOST-001` | 5 | Full host checkpoint plus mandatory exact corpus replay and both compiles. |
| §15 `HKSKIN-CATALOG-001` | 3, 5 | 205 finite descriptors and Kotlin/C#/Python equality. |
| §15 `HKSKIN-ADAPTER-001` | 4, 5 | Exact compile and fakes pass; production non-vanilla apply remains `ADAPTER-BLOCKED`. |
| §15 `HKSKIN-LIFECYCLE-001` | 3–5 | Host lifecycle matrix passes; production rotation remains `LIFECYCLE-BLOCKED`. |
| §15 `HKSKIN-STAMP-001` | 3–5 | Host transaction and exact long compile pass; clone behavior remains device-pending. |
| §15 `HKSKIN-RAR-001` | 1, 5 | `UNSUPPORTED_RAR`; deferred/nonblocking. |
| §15 `HKSKIN-BROWSER-001` | 5 | No implementation; deferred/nonblocking. |
| §15 `H34-DEVICE-001` | 5 | Referenced only as separate future first-level/input-disabled work requiring approval. |
