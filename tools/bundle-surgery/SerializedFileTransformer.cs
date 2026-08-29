using AssetsTools.NET;
using AssetsTools.NET.Extra;

internal sealed record TransformReport(
    int ShadersSeen,
    int VulkanShaders,
    int MissingVulkanShaders,
    int OriginalTarget,
    int NewTarget);

internal sealed record LoadedTransformResult(
    TransformReport Report,
    bool Changed);

internal static class SerializedFileTransformer
{
    internal const int AndroidBuildTarget = 13;
    internal const int VulkanPlatform = 18;

    private static string ClassDataPath { get; } = Path.Combine(
        AppContext.BaseDirectory,
        "classdata.tpk");

    internal static TransformReport Transform(
        string input,
        string output,
        bool requireVulkan)
    {
        var resolvedInput = Path.GetFullPath(input);
        var resolvedOutput = Path.GetFullPath(output);
        var partPath = resolvedOutput + ".part";
        var outputDirectory = Path.GetDirectoryName(resolvedOutput)
            ?? throw new InvalidDataException($"Output has no parent directory: {resolvedOutput}");
        Directory.CreateDirectory(outputDirectory);
        if (File.Exists(partPath))
        {
            File.Delete(partPath);
        }

        var manager = new AssetsManager();
        try
        {
            manager.LoadClassPackage(ClassDataPath);
            var file = manager.LoadAssetsFile(resolvedInput);
            manager.LoadClassDatabaseFromPackage(file.file.Metadata.UnityVersion);

            var transformed = TransformLoaded(manager, file, requireVulkan);
            using (var stream = File.Create(partPath))
            using (var writer = new AssetsFileWriter(stream))
            {
                file.file.Write(writer);
            }

            var report = transformed.Report;
            manager.UnloadAll();
            Verify(partPath, report, requireVulkan);
            File.Move(partPath, resolvedOutput, overwrite: true);
            return report;
        }
        catch
        {
            if (File.Exists(partPath))
            {
                File.Delete(partPath);
            }
            throw;
        }
        finally
        {
            manager.UnloadAll();
        }
    }

    internal static LoadedTransformResult TransformLoaded(
        AssetsManager manager,
        AssetsFileInstance file,
        bool requireVulkan)
    {
        var shadersSeen = 0;
        var vulkanShaders = 0;
        var missingVulkanShaders = 0;
        var changed = false;
        var originalTarget = checked((int)file.file.Metadata.TargetPlatform);

        foreach (var asset in file.file.GetAssetsOfType(AssetClassID.Shader))
        {
            shadersSeen++;
            var shader = manager.GetBaseField(file, asset);
            if (shader is null)
            {
                missingVulkanShaders++;
                continue;
            }

            var vulkanIndex = FindVulkanIndex(shader);
            if (vulkanIndex < 0)
            {
                missingVulkanShaders++;
                continue;
            }

            vulkanShaders++;
            if (KeepOnlyVulkan(shader, vulkanIndex))
            {
                asset.SetNewData(shader);
                changed = true;
            }
        }

        if (requireVulkan && missingVulkanShaders > 0)
        {
            throw new InvalidDataException(
                $"{missingVulkanShaders} shader(s) have no verifiable Vulkan slice");
        }

        if (file.file.Metadata.TargetPlatform != AndroidBuildTarget)
        {
            file.file.Metadata.TargetPlatform = AndroidBuildTarget;
            changed = true;
        }

        return new LoadedTransformResult(
            new TransformReport(
                shadersSeen,
                vulkanShaders,
                missingVulkanShaders,
                originalTarget,
                AndroidBuildTarget),
            changed);
    }

    private static void Verify(
        string path,
        TransformReport expected,
        bool requireVulkan)
    {
        var manager = new AssetsManager();
        try
        {
            manager.LoadClassPackage(ClassDataPath);
            var file = manager.LoadAssetsFile(path);
            manager.LoadClassDatabaseFromPackage(file.file.Metadata.UnityVersion);
            if (file.file.Metadata.TargetPlatform != AndroidBuildTarget)
            {
                throw new InvalidDataException(
                    $"Serialized-file verification found target {file.file.Metadata.TargetPlatform}, expected Android");
            }

            var shadersSeen = 0;
            var vulkanShaders = 0;
            var missingVulkanShaders = 0;
            foreach (var asset in file.file.GetAssetsOfType(AssetClassID.Shader))
            {
                shadersSeen++;
                var shader = manager.GetBaseField(file, asset);
                if (shader is not null && HasVulkan(shader))
                {
                    var platforms = shader["platforms.Array"];
                    if (platforms.AsArray.size != 1 ||
                        platforms[0].AsInt != VulkanPlatform)
                    {
                        throw new InvalidDataException(
                            "Serialized-file verification found a Vulkan shader that was not normalized");
                    }
                    VerifyNormalizedChunks(shader);
                    vulkanShaders++;
                }
                else
                {
                    missingVulkanShaders++;
                }
            }

            if (shadersSeen != expected.ShadersSeen ||
                vulkanShaders != expected.VulkanShaders ||
                missingVulkanShaders != expected.MissingVulkanShaders)
            {
                throw new InvalidDataException(
                    "Serialized-file verification report differs from the transformation report");
            }
            if (requireVulkan && missingVulkanShaders > 0)
            {
                throw new InvalidDataException(
                    $"Serialized-file verification found {missingVulkanShaders} shader(s) without Vulkan");
            }
        }
        finally
        {
            manager.UnloadAll();
        }
    }

    private static bool HasVulkan(AssetTypeValueField shader) =>
        FindVulkanIndex(shader) >= 0;

    private static int FindVulkanIndex(AssetTypeValueField shader)
    {
        var platforms = shader["platforms.Array"];
        for (var index = 0; index < platforms.AsArray.size; index++)
        {
            if (platforms[index].AsInt == VulkanPlatform)
            {
                return index;
            }
        }

        return -1;
    }

    private static bool KeepOnlyVulkan(
        AssetTypeValueField shader,
        int vulkanIndex)
    {
        var platforms = shader["platforms.Array"];
        if (platforms.AsArray.size == 1 && vulkanIndex == 0)
        {
            return false;
        }

        var offsets = shader["offsets.Array"];
        var compressedLengths = shader["compressedLengths.Array"];
        var decompressedLengths = shader["decompressedLengths.Array"];
        EnsurePlatformIndex(offsets, vulkanIndex, "offsets");
        EnsurePlatformIndex(compressedLengths, vulkanIndex, "compressedLengths");
        EnsurePlatformIndex(decompressedLengths, vulkanIndex, "decompressedLengths");

        var offsetFields = GetChunkFields(offsets[vulkanIndex]);
        var compressedLengthFields = GetChunkFields(compressedLengths[vulkanIndex]);
        var decompressedLengthFields = GetChunkFields(decompressedLengths[vulkanIndex]);
        if (offsetFields.Count == 0 ||
            offsetFields.Count != compressedLengthFields.Count ||
            offsetFields.Count != decompressedLengthFields.Count)
        {
            throw new InvalidDataException(
                "Vulkan shader slice has inconsistent chunk metadata");
        }

        var blob = shader["compressedBlob.Array"].AsByteArray;
        using var selectedBlob = new MemoryStream();
        for (var chunk = 0; chunk < offsetFields.Count; chunk++)
        {
            var offset = offsetFields[chunk].AsUInt;
            var length = compressedLengthFields[chunk].AsUInt;
            var end = checked((ulong)offset + length);
            if (end > (ulong)blob.Length)
            {
                throw new InvalidDataException(
                    $"Vulkan shader chunk {chunk} exceeds the compressed blob");
            }

            offsetFields[chunk].AsUInt = checked((uint)selectedBlob.Length);
            selectedBlob.Write(blob, checked((int)offset), checked((int)length));
        }

        KeepOnlyArrayElement(platforms, vulkanIndex);
        KeepOnlyArrayElement(offsets, vulkanIndex);
        KeepOnlyArrayElement(compressedLengths, vulkanIndex);
        KeepOnlyArrayElement(decompressedLengths, vulkanIndex);
        shader["compressedBlob.Array"].AsByteArray = selectedBlob.ToArray();
        return true;
    }

    private static void VerifyNormalizedChunks(AssetTypeValueField shader)
    {
        var offsets = GetChunkFields(shader["offsets.Array"][0]);
        var compressedLengths = GetChunkFields(shader["compressedLengths.Array"][0]);
        var decompressedLengths = GetChunkFields(shader["decompressedLengths.Array"][0]);
        if (offsets.Count == 0 ||
            offsets.Count != compressedLengths.Count ||
            offsets.Count != decompressedLengths.Count)
        {
            throw new InvalidDataException(
                "Serialized-file verification found inconsistent Vulkan chunk metadata");
        }

        var blobLength = shader["compressedBlob.Array"].AsByteArray.Length;
        ulong expectedOffset = 0;
        for (var chunk = 0; chunk < offsets.Count; chunk++)
        {
            if (offsets[chunk].AsUInt != expectedOffset)
            {
                throw new InvalidDataException(
                    $"Serialized-file verification found a non-contiguous Vulkan chunk at index {chunk}");
            }
            expectedOffset = checked(expectedOffset + compressedLengths[chunk].AsUInt);
            if (expectedOffset > (ulong)blobLength)
            {
                throw new InvalidDataException(
                    $"Serialized-file verification found Vulkan chunk {chunk} outside the compressed blob");
            }
        }

        if (expectedOffset != (ulong)blobLength)
        {
            throw new InvalidDataException(
                "Serialized-file verification found unreferenced compressed shader data");
        }
    }

    private static void EnsurePlatformIndex(
        AssetTypeValueField array,
        int index,
        string fieldName)
    {
        if (index >= array.AsArray.size || index >= array.Children.Count)
        {
            throw new InvalidDataException(
                $"Vulkan shader {fieldName} array does not contain platform index {index}");
        }
    }

    private static List<AssetTypeValueField> GetChunkFields(
        AssetTypeValueField platformEntry)
    {
        if (platformEntry.Children.Count > 0 &&
            platformEntry.Children[0].FieldName == "Array")
        {
            return platformEntry["Array"].Children;
        }

        return [platformEntry];
    }

    private static void KeepOnlyArrayElement(
        AssetTypeValueField array,
        int selectedIndex)
    {
        var selected = array.Children[selectedIndex];
        array.Children = [selected];
        array.AsArray = new AssetTypeArrayInfo(1);
    }
}
