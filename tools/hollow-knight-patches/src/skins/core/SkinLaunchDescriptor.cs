using System;
using System.Collections.Generic;

namespace DualSouls.Skins.HollowKnight.Core
{
    // Expected launch correlation, supplied by the caller; not an authenticity proof.
    public sealed class DescriptorExpectations
    {
        public Guid DescriptorId { get; }
        public string ProfileId { get; }
        public string GameVersion { get; }
        public string CatalogId { get; }
        public string CatalogSha256 { get; }
        public Guid LeaseId { get; }
        public DescriptorExpectations(Guid descriptorId, string profileId, string gameVersion,
            string catalogId, string catalogSha256, Guid leaseId)
        {
            DescriptorId = descriptorId; ProfileId = profileId; GameVersion = gameVersion;
            CatalogId = catalogId; CatalogSha256 = catalogSha256; LeaseId = leaseId;
        }
    }

    // Only the parser returns a result. Success means validated wire data, never live/durable proof.
    public sealed class DescriptorParseResult
    {
        public bool Success => Descriptor != null;
        public SkinLaunchDescriptor Descriptor { get; }
        public string Code => Success ? null : "DOCUMENT_INVALID";
        internal DescriptorParseResult(SkinLaunchDescriptor descriptor) { Descriptor = descriptor; }
    }

    public sealed class SkinLaunchDescriptor
    {
        public int SchemaVersion { get; }
        public Guid DescriptorId { get; }
        public long SessionSequence { get; }
        public string ProfileId { get; }
        public string GameVersion { get; }
        public string CatalogId { get; }
        public string CatalogSha256 { get; }
        public string RegistryGenerationId { get; }
        public string RegistryGenerationSha256 { get; }
        public DescriptorActivation Activation { get; }
        public IReadOnlyList<DescriptorPackEnvelope> Packs { get; }
        public Guid LeaseId { get; }
        public string LeaseTokenSha256 { get; }
        internal SkinLaunchDescriptor(int schemaVersion, Guid descriptorId, long sessionSequence,
            string profileId, string gameVersion, string catalogId, string catalogSha256,
            string registryGenerationId, string registryGenerationSha256, DescriptorActivation activation,
            IEnumerable<DescriptorPackEnvelope> packs, Guid leaseId, string leaseTokenSha256)
        {
            SchemaVersion = schemaVersion; DescriptorId = descriptorId; SessionSequence = sessionSequence;
            ProfileId = profileId; GameVersion = gameVersion; CatalogId = catalogId; CatalogSha256 = catalogSha256;
            RegistryGenerationId = registryGenerationId; RegistryGenerationSha256 = registryGenerationSha256;
            Activation = activation; Packs = new List<DescriptorPackEnvelope>(packs).AsReadOnly();
            LeaseId = leaseId; LeaseTokenSha256 = leaseTokenSha256;
        }
    }

    // The five-field wire activation; existing four-field snapshots and interlocks stay unchanged.
    public sealed class DescriptorActivation
    {
        public SkinMode Mode { get; }
        public string SelectedPackId { get; }
        public ActiveVisual Active { get; }
        public long SkinStamp { get; }
        public RotationInterlock RotationInterlock { get; }
        internal DescriptorActivation(ActivationSnapshot snapshot, RotationInterlock rotationInterlock)
        {
            Mode = snapshot.Mode; SelectedPackId = snapshot.SelectedPackId;
            Active = snapshot.Active; SkinStamp = snapshot.SkinStamp; RotationInterlock = rotationInterlock;
        }
    }

    public sealed class DescriptorPackEnvelope
    {
        public string Id { get; }
        public string Name { get; }
        public string Author { get; }
        public string CandidateKey { get; }
        public bool RotationEligible { get; }
        public DescriptorObjectEnvelope CurrentObject { get; }
        public DescriptorObjectEnvelope RetainedActiveObject { get; }
        internal DescriptorPackEnvelope(string id, string name, string author, string candidateKey,
            bool rotationEligible, DescriptorObjectEnvelope currentObject, DescriptorObjectEnvelope retainedActiveObject)
        {
            Id = id; Name = name; Author = author; CandidateKey = candidateKey;
            RotationEligible = rotationEligible; CurrentObject = currentObject; RetainedActiveObject = retainedActiveObject;
        }
    }

    public sealed class DescriptorObjectEnvelope
    {
        public string ObjectRoot { get; }
        public string ReceiptPath { get; }
        public string TreeSha256 { get; }
        public string ContentSha256 { get; }
        public string ManifestSha256 { get; }
        public string ImportReceiptSha256 { get; }
        public IReadOnlyList<DescriptorTextureEnvelope> Textures { get; }
        internal DescriptorObjectEnvelope(string objectRoot, string receiptPath, string treeSha256,
            string contentSha256, string manifestSha256, string importReceiptSha256,
            IEnumerable<DescriptorTextureEnvelope> textures)
        {
            ObjectRoot = objectRoot; ReceiptPath = receiptPath; TreeSha256 = treeSha256;
            ContentSha256 = contentSha256; ManifestSha256 = manifestSha256; ImportReceiptSha256 = importReceiptSha256;
            Textures = new List<DescriptorTextureEnvelope>(textures).AsReadOnly();
        }
    }

    public sealed class DescriptorTextureEnvelope
    {
        public int Ordinal { get; }
        public string Target { get; }
        public string SourceRelativePath { get; }
        public string SourceSha256 { get; }
        public long Length { get; }
        internal DescriptorTextureEnvelope(int ordinal, string target, string sourceRelativePath, string sourceSha256, long length)
        {
            Ordinal = ordinal; Target = target; SourceRelativePath = sourceRelativePath;
            SourceSha256 = sourceSha256; Length = length;
        }
    }
}
