using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace BundleSurgery.Tests.Fixtures;

internal static class SyntheticAssetFactory
{
    private const string UnityVersion = "6000.0.61f1";
    private const uint LinuxBuildTarget = 24;

    internal static SyntheticSerializedFile WithShaderPlatforms(params int[] platforms)
    {
        var blobValue = 0;
        return WithShaderPlatformChunks(
            platforms
                .Select(platform => (
                    Platform: platform,
                    Chunks: new[] { checked((byte)++blobValue) }))
                .ToArray());
    }

    internal static SyntheticSerializedFile WithShaderPlatformChunks(
        params (int Platform, byte[] Chunks)[] platforms)
    {
        var fixture = SyntheticSerializedFile.Create();
        var manager = new AssetsManager();
        manager.LoadClassPackage(Path.Combine(AppContext.BaseDirectory, "classdata.tpk"));
        manager.LoadClassDatabaseFromPackage(UnityVersion);

        var file = new AssetsFile
        {
            Header = new AssetsFileHeader
            {
                Version = 22,
                Endianness = false,
            },
            Metadata = new AssetsFileMetadata
            {
                UnityVersion = UnityVersion,
                TargetPlatform = LinuxBuildTarget,
                TypeTreeEnabled = false,
                TypeTreeTypes = [],
                AssetInfos = [],
                ScriptTypes = [],
                Externals = [],
                RefTypes = [],
                UserInformation = string.Empty,
            },
        };

        var shaderType = manager.ClassDatabase.FindAssetClassByID((int)AssetClassID.Shader)
            ?? throw new InvalidOperationException("Shader class is absent from the class database");
        var template = new AssetTypeTemplateField();
        template.FromClassDatabase(manager.ClassDatabase, shaderType, false);
        var shader = ValueBuilder.DefaultValueFieldFromTemplate(template);
        var platformArray = shader["platforms.Array"];
        foreach (var platform in platforms)
        {
            var element = ValueBuilder.DefaultValueFieldFromArrayTemplate(platformArray);
            element.AsInt = platform.Platform;
            platformArray.Children.Add(element);
        }
        platformArray.AsArray = new AssetTypeArrayInfo(platformArray.Children.Count);

        PopulatePerPlatformUIntArrays(shader, platforms);
        shader["compressedBlob.Array"].AsByteArray = platforms
            .SelectMany(platform => platform.Chunks)
            .ToArray();

        var shaderInfo = AssetFileInfo.Create(
            file,
            1,
            (int)AssetClassID.Shader,
            manager.ClassDatabase,
            false);
        shaderInfo.SetNewData(shader);
        file.Metadata.AddAssetInfo(shaderInfo);

        using (var writer = new AssetsFileWriter(fixture.InputPath))
        {
            file.Write(writer);
        }
        manager.UnloadAll();
        return fixture;
    }

    internal static SyntheticAssetInspection Inspect(string path)
    {
        var manager = new AssetsManager();
        try
        {
            manager.LoadClassPackage(Path.Combine(AppContext.BaseDirectory, "classdata.tpk"));
            var file = manager.LoadAssetsFile(path);
            manager.LoadClassDatabaseFromPackage(file.file.Metadata.UnityVersion);
            var shaderInfos = file.file.GetAssetsOfType(AssetClassID.Shader).ToList();
            if (shaderInfos.Count != 1)
            {
                throw new InvalidDataException(
                    $"Expected one synthetic shader, found {shaderInfos.Count}");
            }
            var shaderInfo = shaderInfos[0];
            var shader = manager.GetBaseField(file, shaderInfo)
                ?? throw new InvalidDataException("Synthetic shader could not be deserialized");
            var platforms = shader["platforms.Array"];
            return new SyntheticAssetInspection(
                file.file.Metadata.TargetPlatform,
                Enumerable.Range(0, platforms.AsArray.size)
                    .Select(index => platforms[index].AsInt)
                    .ToArray(),
                shader["compressedBlob.Array"].AsByteArray,
                ReadChunkValues(shader["offsets.Array"][0]));
        }
        finally
        {
            manager.UnloadAll();
        }
    }

    private static void PopulatePerPlatformUIntArrays(
        AssetTypeValueField shader,
        IReadOnlyList<(int Platform, byte[] Chunks)> platforms)
    {
        uint blobOffset = 0;
        var offsets = new List<uint[]>();
        foreach (var platform in platforms)
        {
            if (platform.Chunks.Length == 0)
            {
                throw new ArgumentException("Each synthetic platform needs at least one chunk");
            }
            offsets.Add(platform.Chunks.Select(_ => blobOffset++).ToArray());
        }

        PopulatePerPlatformUIntArray(
            shader["offsets.Array"],
            offsets);
        PopulatePerPlatformUIntArray(
            shader["compressedLengths.Array"],
            platforms.Select(platform =>
                Enumerable.Repeat(1u, platform.Chunks.Length).ToArray()));
        PopulatePerPlatformUIntArray(
            shader["decompressedLengths.Array"],
            platforms.Select(platform =>
                Enumerable.Repeat(1u, platform.Chunks.Length).ToArray()));
    }

    private static void PopulatePerPlatformUIntArray(
        AssetTypeValueField outer,
        IEnumerable<IEnumerable<uint>> valuesByPlatform)
    {
        foreach (var platformValues in valuesByPlatform)
        {
            var values = platformValues.ToArray();
            var element = ValueBuilder.DefaultValueFieldFromArrayTemplate(outer);
            if (element.Children.Count > 0 && element.Children[0].FieldName == "Array")
            {
                var inner = element["Array"];
                foreach (var value in values)
                {
                    var chunk = ValueBuilder.DefaultValueFieldFromArrayTemplate(inner);
                    chunk.AsUInt = value;
                    inner.Children.Add(chunk);
                }
                inner.AsArray = new AssetTypeArrayInfo(inner.Children.Count);
            }
            else
            {
                if (values.Length != 1)
                {
                    throw new InvalidDataException(
                        "The class database exposes only one shader chunk per platform");
                }
                element.AsUInt = values[0];
            }
            outer.Children.Add(element);
        }
        outer.AsArray = new AssetTypeArrayInfo(outer.Children.Count);
    }

    private static uint[] ReadChunkValues(AssetTypeValueField platformEntry)
    {
        if (platformEntry.Children.Count > 0 &&
            platformEntry.Children[0].FieldName == "Array")
        {
            return platformEntry["Array"].Children
                .Select(chunk => chunk.AsUInt)
                .ToArray();
        }

        return [platformEntry.AsUInt];
    }
}

internal sealed record SyntheticAssetInspection(
    uint TargetPlatform,
    int[] ShaderPlatforms,
    byte[] CompressedBlob,
    uint[] CompressedOffsets);

internal sealed class SyntheticSerializedFile : IDisposable
{
    private SyntheticSerializedFile(string root)
    {
        Root = root;
        InputPath = Path.Combine(root, "source.assets");
        OutputRoot = Path.Combine(root, "output");
        Directory.CreateDirectory(OutputRoot);
    }

    internal string Root { get; }
    internal string InputPath { get; }
    internal string OutputRoot { get; }

    internal static SyntheticSerializedFile Create()
    {
        var taskTempRoot = Environment.GetEnvironmentVariable("DUALSOULS_TEMP_ROOT");
        if (string.IsNullOrWhiteSpace(taskTempRoot))
        {
            taskTempRoot = Directory.Exists(@"D:\Temp") ? @"D:\Temp" : Path.GetTempPath();
        }

        var root = Path.Combine(
            Path.GetFullPath(taskTempRoot),
            $"dualsouls-bundle-surgery-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return new SyntheticSerializedFile(root);
    }

    public void Dispose()
    {
        if (!Directory.Exists(Root))
        {
            return;
        }

        var resolvedRoot = Path.GetFullPath(Root);
        var resolvedParent = Path.GetFullPath(Path.GetDirectoryName(resolvedRoot)!);
        if (!resolvedRoot.StartsWith(
                resolvedParent + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(resolvedRoot).StartsWith(
                "dualsouls-bundle-surgery-test-",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Refusing to remove unexpected fixture root: {resolvedRoot}");
        }

        Directory.Delete(resolvedRoot, recursive: true);
    }
}
