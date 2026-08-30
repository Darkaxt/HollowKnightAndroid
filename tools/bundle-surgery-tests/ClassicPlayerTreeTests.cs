using BundleSurgery.Tests.Fixtures;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace BundleSurgery.Tests;

public sealed class ClassicPlayerTreeTests
{
    [Fact]
    public void Discover_parses_extensionless_files_and_sorts_numeric_names()
    {
        using var fixture = SyntheticAssetFactory.WithShaderPlatforms(18);
        var dataRoot = CreateDataRoot(
            fixture,
            "globalgamemanagers",
            "resources.assets",
            "level10",
            "level2",
            "sharedassets10.assets",
            "sharedassets2.assets");
        File.WriteAllBytes(Path.Combine(dataRoot, "resources.assets.resS"), [1, 2]);
        File.WriteAllBytes(Path.Combine(dataRoot, "sharedassets2.assets.resS"), [3, 4]);
        File.WriteAllBytes(Path.Combine(dataRoot, "sharedassets10.resource"), [5, 6]);

        var inventory = ClassicPlayerTree.Discover(dataRoot);

        Assert.Empty(inventory.Diagnostics);
        Assert.Equal(
            [
                "globalgamemanagers",
                "resources.assets",
                "level2",
                "level10",
                "sharedassets2.assets",
                "sharedassets10.assets",
            ],
            inventory.Files
                .Where(file => file.Kind == ClassicFileKind.Serialized)
                .Select(file => Path.GetFileName(file.RelativePath)));
        Assert.Equal(
            [
                "resources.assets.resS",
                "sharedassets2.assets.resS",
                "sharedassets10.resource",
            ],
            inventory.Files
                .Where(file => file.Kind == ClassicFileKind.Sidecar)
                .Select(file => Path.GetFileName(file.RelativePath)));
        Assert.All(inventory.Files, file =>
            Assert.Matches("^[0-9a-f]{64}$", file.Sha256));
        Assert.Matches("^[0-9a-f]{64}$", inventory.SourceTreeSha256);
        Assert.Equal(
            inventory.SourceTreeSha256,
            ClassicPlayerTree.Discover(dataRoot).SourceTreeSha256);
    }

    [Fact]
    public void Discover_reports_duplicate_numeric_indices_and_orphaned_sidecars()
    {
        using var fixture = SyntheticAssetFactory.WithShaderPlatforms(18);
        var dataRoot = CreateDataRoot(
            fixture,
            "globalgamemanagers",
            "level1",
            "level01");
        File.WriteAllBytes(Path.Combine(dataRoot, "orphan.assets.resS"), [1]);

        var inventory = ClassicPlayerTree.Discover(dataRoot);

        Assert.Contains(inventory.Diagnostics, diagnostic =>
            diagnostic.Code == "DUPLICATE_NUMERIC_INDEX" &&
            diagnostic.Message.Contains("level", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(inventory.Diagnostics, diagnostic =>
            diagnostic.Code == "ORPHANED_SIDECAR");
    }

    [Fact]
    public void Discover_scopes_numeric_indices_to_their_directory()
    {
        using var fixture = SyntheticAssetFactory.WithShaderPlatforms(18);
        var dataRoot = CreateDataRoot(fixture, "globalgamemanagers");
        foreach (var directory in new[] { "ScenesA", "ScenesB" })
        {
            var targetDirectory = Path.Combine(dataRoot, directory);
            Directory.CreateDirectory(targetDirectory);
            File.Copy(fixture.InputPath, Path.Combine(targetDirectory, "level1"));
        }

        var inventory = ClassicPlayerTree.Discover(dataRoot);

        Assert.DoesNotContain(inventory.Diagnostics, diagnostic =>
            diagnostic.Code == "DUPLICATE_NUMERIC_INDEX");
    }

    [Fact]
    public void Discover_rejects_a_non_linux_serialized_file()
    {
        using var fixture = SyntheticAssetFactory.WithShaderPlatformsAndTarget(19, 18);
        var dataRoot = CreateDataRoot(fixture, "globalgamemanagers");

        var inventory = ClassicPlayerTree.Discover(dataRoot);

        Assert.Contains(inventory.Diagnostics, diagnostic =>
            diagnostic.Code == "NON_LINUX_SERIALIZED_FILE" &&
            diagnostic.RelativePath == "globalgamemanagers");
    }

    [Fact]
    public void Discover_marks_parseable_unity_builtins_for_android_replacement()
    {
        using var fixture = SyntheticAssetFactory.WithShaderPlatforms(18);
        var dataRoot = CreateDataRoot(fixture, "globalgamemanagers");
        var resourcesRoot = Path.Combine(dataRoot, "Resources");
        Directory.CreateDirectory(resourcesRoot);
        File.Copy(
            fixture.InputPath,
            Path.Combine(resourcesRoot, "unity default resources"));

        var inventory = ClassicPlayerTree.Discover(dataRoot);

        var builtin = Assert.Single(
            inventory.Files,
            file => file.RelativePath == "Resources/unity default resources");
        Assert.Equal(ClassicFileKind.ReplacementRequired, builtin.Kind);
        Assert.DoesNotContain(inventory.Diagnostics, diagnostic =>
            diagnostic.RelativePath == builtin.RelativePath);
    }

    [Fact]
    public void Discover_does_not_classify_managed_assemblies_as_native_plugins()
    {
        using var fixture = SyntheticAssetFactory.WithShaderPlatforms(18);
        var dataRoot = CreateDataRoot(fixture, "globalgamemanagers");
        var managedRoot = Path.Combine(dataRoot, "Managed");
        Directory.CreateDirectory(managedRoot);
        File.WriteAllBytes(Path.Combine(managedRoot, "Assembly-CSharp.dll"), [1]);
        var pluginsRoot = Path.Combine(dataRoot, "Plugins");
        Directory.CreateDirectory(pluginsRoot);
        File.WriteAllBytes(Path.Combine(pluginsRoot, "desktop.dll"), [2]);

        var inventory = ClassicPlayerTree.Discover(dataRoot);

        Assert.Equal(
            ClassicFileKind.PassThrough,
            Assert.Single(
                inventory.Files,
                file => file.RelativePath == "Managed/Assembly-CSharp.dll").Kind);
        Assert.Equal(
            ClassicFileKind.NativePlugin,
            Assert.Single(
                inventory.Files,
                file => file.RelativePath == "Plugins/desktop.dll").Kind);
    }

    [Fact]
    public void Validate_rejects_a_manifest_required_missing_file()
    {
        using var fixture = SyntheticAssetFactory.WithShaderPlatforms(18);
        var dataRoot = CreateDataRoot(fixture, "globalgamemanagers");
        var inventory = ClassicPlayerTree.Discover(dataRoot);
        var manifest = ManifestFor(
            inventory,
            new ClassicManifestFile(
                "sharedassets1.assets",
                1,
                new string('0', 64),
                ClassicManifestAction.Transform,
                null));

        var result = ClassicPlayerTree.Validate(inventory, manifest);

        Assert.Null(result.Layout);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "MISSING_REQUIRED_FILE" &&
            diagnostic.RelativePath == "sharedassets1.assets");
    }

    [Theory]
    [InlineData("../outside.assets")]
    [InlineData("/outside.assets")]
    [InlineData("C:/outside.assets")]
    public void Validate_rejects_traversal_or_rooted_manifest_paths(string relativePath)
    {
        using var fixture = SyntheticAssetFactory.WithShaderPlatforms(18);
        var dataRoot = CreateDataRoot(fixture, "globalgamemanagers");
        var inventory = ClassicPlayerTree.Discover(dataRoot);
        var manifest = ManifestFor(
            inventory,
            new ClassicManifestFile(
                relativePath,
                1,
                new string('0', 64),
                ClassicManifestAction.Transform,
                null));

        var result = ClassicPlayerTree.Validate(inventory, manifest);

        Assert.Null(result.Layout);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "INVALID_MANIFEST_PATH" &&
            diagnostic.RelativePath == relativePath);
    }

    [Fact]
    public void Validate_rejects_case_insensitive_manifest_aliases()
    {
        using var fixture = SyntheticAssetFactory.WithShaderPlatforms(18);
        var dataRoot = CreateDataRoot(fixture, "globalgamemanagers");
        var inventory = ClassicPlayerTree.Discover(dataRoot);
        var hash = new string('0', 64);
        var manifest = ManifestFor(
            inventory,
            new ClassicManifestFile(
                "Data/File.bin",
                1,
                hash,
                ClassicManifestAction.Copy,
                null),
            new ClassicManifestFile(
                "data/file.bin",
                1,
                hash,
                ClassicManifestAction.Copy,
                null));

        var result = ClassicPlayerTree.Validate(inventory, manifest);

        Assert.Null(result.Layout);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "DUPLICATE_MANIFEST_PATH");
    }

    [Fact]
    public void Validate_rejects_manifest_policy_changed_without_a_new_hash()
    {
        using var fixture = SyntheticAssetFactory.WithShaderPlatforms(18);
        var dataRoot = CreateDataRoot(fixture, "globalgamemanagers");
        var inventory = ClassicPlayerTree.Discover(dataRoot);
        var manifest = ManifestFor(inventory);
        var tampered = manifest with
        {
            RequiredFiles = manifest.RequiredFiles
                .Select(file => file.RelativePath == "globalgamemanagers"
                    ? file with { Action = ClassicManifestAction.Exclude }
                    : file)
                .ToArray(),
        };

        var result = ClassicPlayerTree.Validate(inventory, tampered);

        Assert.Null(result.Layout);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "INVALID_MANIFEST_HASH");
    }

    [Fact]
    public void Validate_reports_source_case_aliases_without_throwing()
    {
        var hash = new string('1', 64);
        var inventory = new ClassicPlayerInventory(
            Path.GetTempPath(),
            new string('2', 64),
            [
                new ClassicFileEntry("Data/Foo", ClassicFileKind.PassThrough, 1, hash, null),
                new ClassicFileEntry("Data/foo", ClassicFileKind.PassThrough, 1, hash, null),
            ],
            []);
        var manifest = new ClassicProfileManifest(
            1,
            "hollow-knight",
            "1.5.12620",
            "6000.0.61f1",
            "linux",
            [new ClassicManifestFile(
                "Data/Foo",
                1,
                hash,
                ClassicManifestAction.Copy,
                null)],
            1,
            new string('3', 64));

        var result = ClassicPlayerTree.Validate(inventory, manifest);

        Assert.Null(result.Layout);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "DUPLICATE_SOURCE_PATH");
    }

    [Theory]
    [InlineData("Data/file.")]
    [InlineData("Data/file ")]
    public void Validate_rejects_windows_trailing_aliases(string relativePath)
    {
        using var fixture = SyntheticAssetFactory.WithShaderPlatforms(18);
        var inventory = ClassicPlayerTree.Discover(
            CreateDataRoot(fixture, "globalgamemanagers"));
        var manifest = ManifestFor(
            inventory,
            new ClassicManifestFile(
                relativePath,
                1,
                new string('0', 64),
                ClassicManifestAction.Copy,
                null));

        var result = ClassicPlayerTree.Validate(inventory, manifest);

        Assert.Null(result.Layout);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "INVALID_MANIFEST_PATH" &&
            diagnostic.RelativePath == relativePath);
    }

    [Fact]
    public void Convert_transforms_serialized_files_and_copies_sidecars_without_touching_source()
    {
        using var fixture = SyntheticAssetFactory.WithShaderAndBuildSettings(
            [15, 18],
            [17, 21],
            "6000.0.61f1");
        var dataRoot = CreateDataRoot(
            fixture,
            "globalgamemanagers",
            "level0",
            "sharedassets0.assets");
        var sidecarPath = Path.Combine(dataRoot, "sharedassets0.assets.resS");
        File.WriteAllBytes(sidecarPath, [7, 8, 9]);
        var inventory = ClassicPlayerTree.Discover(dataRoot);
        var validation = ClassicPlayerTree.Validate(inventory, ManifestFor(inventory));
        var sourceHashes = inventory.Files.ToDictionary(
            file => file.RelativePath,
            file => file.Sha256,
            StringComparer.OrdinalIgnoreCase);
        var outputRoot = Path.Combine(fixture.Root, "converted");
        var reportPath = Path.Combine(fixture.Root, "reports", "classic-report.json");

        var report = ClassicTreeConverter.Convert(
            Assert.IsType<ClassicPlayerTree.ValidatedClassicPlayerLayout>(validation.Layout),
            outputRoot,
            reportPath,
            ClassicTransformOptions.Default);

        Assert.Equal("COMPLETE", report.Status);
        Assert.Equal("1.5.12620", report.GameVersion);
        foreach (var relativePath in new[]
                 {
                     "globalgamemanagers",
                     "level0",
                     "sharedassets0.assets",
                 })
        {
            var inspected = SyntheticAssetFactory.Inspect(
                Path.Combine(outputRoot, relativePath));
            Assert.Equal(13u, inspected.TargetPlatform);
            Assert.Equal([18], inspected.ShaderPlatforms);
        }
        Assert.Equal(
            [7, 8, 9],
            File.ReadAllBytes(Path.Combine(outputRoot, "sharedassets0.assets.resS")));
        Assert.True(File.Exists(reportPath));
        Assert.False(File.Exists(reportPath + ".part"));
        Assert.False(Directory.Exists(outputRoot + ".part"));

        var after = ClassicPlayerTree.Discover(dataRoot);
        Assert.Equal(
            sourceHashes,
            after.Files.ToDictionary(
                file => file.RelativePath,
                file => file.Sha256,
                StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void Convert_rejects_source_output_overlap()
    {
        using var fixture = SyntheticAssetFactory.WithShaderAndBuildSettings(
            [18],
            [17, 21],
            "6000.0.61f1");
        var dataRoot = CreateDataRoot(fixture, "globalgamemanagers");
        var inventory = ClassicPlayerTree.Discover(dataRoot);
        var validation = ClassicPlayerTree.Validate(inventory, ManifestFor(inventory));
        var reportPath = Path.Combine(fixture.Root, "report.json");

        var error = Assert.Throws<InvalidDataException>(() =>
            ClassicTreeConverter.Convert(
                Assert.IsType<ClassicPlayerTree.ValidatedClassicPlayerLayout>(validation.Layout),
                dataRoot,
                reportPath,
                ClassicTransformOptions.Default));

        Assert.Contains("overlap", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Convert_missing_vulkan_does_not_publish_a_complete_report()
    {
        using var fixture = SyntheticAssetFactory.WithShaderPlatforms(15);
        var dataRoot = CreateDataRoot(fixture, "globalgamemanagers");
        var inventory = ClassicPlayerTree.Discover(dataRoot);
        var validation = ClassicPlayerTree.Validate(inventory, ManifestFor(inventory));
        var outputRoot = Path.Combine(fixture.Root, "converted");
        var reportPath = Path.Combine(fixture.Root, "report.json");

        var error = Assert.Throws<InvalidDataException>(() =>
            ClassicTreeConverter.Convert(
                Assert.IsType<ClassicPlayerTree.ValidatedClassicPlayerLayout>(validation.Layout),
                outputRoot,
                reportPath,
                ClassicTransformOptions.Default));

        Assert.Contains("Vulkan", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(reportPath));
        using var failureReport = JsonDocument.Parse(File.ReadAllText(reportPath));
        Assert.Equal(
            "TRANSFORM_FAILED",
            failureReport.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public void Convert_reuses_only_outputs_whose_recorded_hash_still_matches()
    {
        using var fixture = SyntheticAssetFactory.WithShaderAndBuildSettings(
            [18],
            [17, 21],
            "6000.0.61f1");
        var dataRoot = CreateDataRoot(fixture, "globalgamemanagers", "level0");
        var inventory = ClassicPlayerTree.Discover(dataRoot);
        var validation = ClassicPlayerTree.Validate(inventory, ManifestFor(inventory));
        var layout = Assert.IsType<ClassicPlayerTree.ValidatedClassicPlayerLayout>(
            validation.Layout);
        var outputRoot = Path.Combine(fixture.Root, "converted");
        var reportPath = Path.Combine(fixture.Root, "report.json");
        ClassicTreeConverter.Convert(
            layout,
            outputRoot,
            reportPath,
            ClassicTransformOptions.Default);
        var levelOutput = Path.Combine(outputRoot, "level0");
        var originalWriteTime = File.GetLastWriteTimeUtc(levelOutput);

        var reused = ClassicTreeConverter.Convert(
            layout,
            outputRoot,
            reportPath,
            ClassicTransformOptions.Default);

        Assert.All(
            reused.Files.Where(file => file.OutputSha256 is not null),
            file => Assert.Equal("REUSED", file.Action));
        Assert.Equal(originalWriteTime, File.GetLastWriteTimeUtc(levelOutput));

        File.WriteAllBytes(levelOutput, [1, 2, 3]);
        var repaired = ClassicTreeConverter.Convert(
            layout,
            outputRoot,
            reportPath,
            ClassicTransformOptions.Default);

        Assert.Equal(
            "TRANSFORMED",
            Assert.Single(repaired.Files, file => file.Path == "level0").Action);
        Assert.Equal(13u, SyntheticAssetFactory.Inspect(levelOutput).TargetPlatform);
    }

    [Fact]
    public void Manifest_command_writes_an_inventoried_shader_media_and_plugin_census()
    {
        using var fixture = SyntheticAssetFactory.WithShaderPlatforms(18);
        var dataRoot = CreateDataRoot(fixture, "globalgamemanagers", "level0");
        File.WriteAllBytes(Path.Combine(dataRoot, "intro.webm"), [1, 2]);
        var pluginDirectory = Path.Combine(dataRoot, "Plugins", "x86_64");
        Directory.CreateDirectory(pluginDirectory);
        File.WriteAllBytes(Path.Combine(pluginDirectory, "desktop.so"), [3, 4]);
        var reportPath = Path.Combine(fixture.Root, "inventory.json");

        var exitCode = Program.Main(
            ["manifest-classic-tree", dataRoot, reportPath]);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(reportPath));
        Assert.False(File.Exists(reportPath + ".part"));
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var root = report.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("INVENTORIED", root.GetProperty("status").GetString());
        var census = root.GetProperty("census");
        Assert.Equal(2, census.GetProperty("shaders").GetInt32());
        Assert.Equal(
            ["Plugins/x86_64/desktop.so"],
            census.GetProperty("nativePlugins")
                .EnumerateArray()
                .Select(item => item.GetString()));
        Assert.Equal(
            ["intro.webm"],
            census.GetProperty("media")
                .EnumerateArray()
                .Select(item => item.GetString()));
    }

    [Fact]
    public void Manifest_command_reports_app_build_and_serialized_identity()
    {
        using var fixture =
            SyntheticAssetFactory.WithShaderBuildSettingsAndPlayerSettings(
                [18],
                [17, 21],
                "6000.0.61f1",
                "1.5.12620");
        var dataRoot = CreateDataRoot(fixture, "globalgamemanagers");
        File.WriteAllText(
            Path.Combine(dataRoot, "app.info"),
            "Team Cherry\nHollow Knight\n");
        var streamingAssets = Path.Combine(dataRoot, "StreamingAssets");
        Directory.CreateDirectory(streamingAssets);
        File.WriteAllText(
            Path.Combine(streamingAssets, "BuildMetadata.json"),
            """{"branchName":"release-1","revision":"12620"}""");
        var reportPath = Path.Combine(fixture.Root, "inventory.json");

        var exitCode = Program.Main(
            ["manifest-classic-tree", dataRoot, reportPath]);

        Assert.Equal(0, exitCode);
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var identity = report.RootElement.GetProperty("identity");
        Assert.Equal("Team Cherry", identity.GetProperty("companyName").GetString());
        Assert.Equal("Hollow Knight", identity.GetProperty("productName").GetString());
        Assert.Equal("1.5.12620", identity.GetProperty("gameVersion").GetString());
        Assert.Equal("6000.0.61f1", identity.GetProperty("unityVersion").GetString());
        Assert.Equal("LinuxPlayer", identity.GetProperty("platform").GetString());
        Assert.Equal("release-1", identity.GetProperty("buildBranch").GetString());
        Assert.Equal(12620, identity.GetProperty("buildRevision").GetInt32());
    }

    [Fact]
    public void Create_profile_manifest_command_writes_valid_exact_actions()
    {
        using var fixture =
            SyntheticAssetFactory.WithShaderBuildSettingsAndPlayerSettings(
                [18],
                [17, 21],
                "6000.0.61f1",
                "1.5.12620");
        var dataRoot = CreateDataRoot(
            fixture,
            "globalgamemanagers",
            "sharedassets0.assets");
        File.WriteAllBytes(
            Path.Combine(dataRoot, "sharedassets0.assets.resS"),
            [1, 2]);
        var managedRoot = Path.Combine(dataRoot, "Managed");
        Directory.CreateDirectory(managedRoot);
        File.WriteAllBytes(Path.Combine(managedRoot, "Assembly-CSharp.dll"), [3]);
        var pluginsRoot = Path.Combine(dataRoot, "Plugins");
        Directory.CreateDirectory(pluginsRoot);
        File.WriteAllBytes(Path.Combine(pluginsRoot, "libsteam_api.so"), [4]);
        var resourcesRoot = Path.Combine(dataRoot, "Resources");
        Directory.CreateDirectory(resourcesRoot);
        File.Copy(
            fixture.InputPath,
            Path.Combine(resourcesRoot, "unity default resources"));
        File.WriteAllText(
            Path.Combine(dataRoot, "app.info"),
            "Team Cherry\nHollow Knight\n");
        var streamingAssets = Path.Combine(dataRoot, "StreamingAssets");
        Directory.CreateDirectory(streamingAssets);
        File.WriteAllText(
            Path.Combine(streamingAssets, "BuildMetadata.json"),
            """{"branchName":"release-1","revision":"12620"}""");
        var manifestPath = Path.Combine(fixture.Root, "profile-manifest.json");

        var exitCode = Program.Main(
            [
                "create-classic-profile-manifest",
                dataRoot,
                "hollow-knight",
                "1.5.12620",
                "6000.0.61f1",
                manifestPath,
            ]);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(manifestPath));
        Assert.False(File.Exists(manifestPath + ".part"));
        var manifestText = File.ReadAllText(manifestPath);
        Assert.DoesNotContain('\r', manifestText);
        Assert.EndsWith("\n", manifestText, StringComparison.Ordinal);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        };
        var manifest = JsonSerializer.Deserialize<ClassicProfileManifest>(
            File.ReadAllText(manifestPath),
            options);
        Assert.NotNull(manifest);
        var inventory = ClassicPlayerTree.Discover(dataRoot);
        Assert.NotNull(ClassicPlayerTree.Validate(inventory, manifest!).Layout);
        var actions = manifest.RequiredFiles.ToDictionary(
            file => file.RelativePath,
            file => file.Action,
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal(ClassicManifestAction.Transform, actions["globalgamemanagers"]);
        Assert.Equal(ClassicManifestAction.Copy, actions["Managed/Assembly-CSharp.dll"]);
        Assert.Equal(ClassicManifestAction.Copy, actions["sharedassets0.assets.resS"]);
        Assert.Equal(ClassicManifestAction.Exclude, actions["Plugins/libsteam_api.so"]);
        Assert.Equal(
            ClassicManifestAction.ReplaceAtAssembly,
            actions["Resources/unity default resources"]);
    }

    [Fact]
    public void Create_hollow_knight_manifest_rejects_a_wrong_build_revision()
    {
        using var fixture =
            SyntheticAssetFactory.WithShaderBuildSettingsAndPlayerSettings(
                [18],
                [17, 21],
                "6000.0.61f1",
                "1.5.12620");
        var dataRoot = CreateDataRoot(fixture, "globalgamemanagers");
        File.WriteAllText(
            Path.Combine(dataRoot, "app.info"),
            "Team Cherry\nHollow Knight\n");
        var streamingAssets = Path.Combine(dataRoot, "StreamingAssets");
        Directory.CreateDirectory(streamingAssets);
        File.WriteAllText(
            Path.Combine(streamingAssets, "BuildMetadata.json"),
            """{"branchName":"release-1","revision":"12620"}""");
        var manifestPath = Path.Combine(fixture.Root, "profile-manifest.json");

        var exitCode = Program.Main(
            [
                "create-classic-profile-manifest",
                dataRoot,
                "hollow-knight",
                "1.5.12612",
                "6000.0.61f1",
                manifestPath,
            ]);

        Assert.Equal(3, exitCode);
        Assert.False(File.Exists(manifestPath));
    }

    [Fact]
    public void Retarget_command_validates_the_manifest_and_converts_the_tree()
    {
        using var fixture = SyntheticAssetFactory.WithShaderAndBuildSettings(
            [18],
            [17, 21],
            "6000.0.61f1");
        var dataRoot = CreateDataRoot(fixture, "globalgamemanagers", "level0");
        var inventory = ClassicPlayerTree.Discover(dataRoot);
        var manifestPath = Path.Combine(fixture.Root, "profile-manifest.json");
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        };
        File.WriteAllText(
            manifestPath,
            JsonSerializer.Serialize(ManifestFor(inventory), jsonOptions));
        var outputRoot = Path.Combine(fixture.Root, "converted");
        var reportPath = Path.Combine(fixture.Root, "retarget-report.json");

        var exitCode = Program.Main(
            [
                "retarget-classic-tree",
                dataRoot,
                outputRoot,
                manifestPath,
                reportPath,
            ]);

        Assert.Equal(0, exitCode);
        Assert.Equal(
            13u,
            SyntheticAssetFactory.Inspect(Path.Combine(outputRoot, "level0")).TargetPlatform);
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        Assert.Equal("COMPLETE", report.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            "1.5.12620",
            report.RootElement.GetProperty("gameVersion").GetString());
    }

    [Fact]
    public void Retarget_command_writes_a_validation_failure_report()
    {
        using var fixture = SyntheticAssetFactory.WithShaderPlatforms(18);
        var dataRoot = CreateDataRoot(fixture, "globalgamemanagers");
        var inventory = ClassicPlayerTree.Discover(dataRoot);
        var manifest = ManifestFor(
            inventory,
            new ClassicManifestFile(
                "missing.assets",
                1,
                new string('0', 64),
                ClassicManifestAction.Transform,
                null));
        var manifestPath = Path.Combine(fixture.Root, "profile-manifest.json");
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        };
        File.WriteAllText(
            manifestPath,
            JsonSerializer.Serialize(manifest, jsonOptions));
        var reportPath = Path.Combine(fixture.Root, "validation-report.json");

        var exitCode = Program.Main(
            [
                "retarget-classic-tree",
                dataRoot,
                Path.Combine(fixture.Root, "converted"),
                manifestPath,
                reportPath,
            ]);

        Assert.Equal(3, exitCode);
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        Assert.Equal(
            "VALIDATION_FAILED",
            report.RootElement.GetProperty("status").GetString());
        Assert.Contains(
            report.RootElement.GetProperty("diagnostics").EnumerateArray(),
            diagnostic => diagnostic.GetProperty("code").GetString() ==
                "MISSING_REQUIRED_FILE");
    }

    [Fact]
    public void Retarget_command_reports_a_malformed_manifest_as_validation_failure()
    {
        using var fixture = SyntheticAssetFactory.WithShaderPlatforms(18);
        var dataRoot = CreateDataRoot(fixture, "globalgamemanagers");
        var manifestPath = Path.Combine(fixture.Root, "profile-manifest.json");
        File.WriteAllText(manifestPath, "{not-json");
        var reportPath = Path.Combine(fixture.Root, "validation-report.json");

        var exitCode = Program.Main(
            [
                "retarget-classic-tree",
                dataRoot,
                Path.Combine(fixture.Root, "converted"),
                manifestPath,
                reportPath,
            ]);

        Assert.Equal(3, exitCode);
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        Assert.Equal(
            "VALIDATION_FAILED",
            report.RootElement.GetProperty("status").GetString());
        Assert.Contains(
            report.RootElement.GetProperty("diagnostics").EnumerateArray(),
            diagnostic => diagnostic.GetProperty("code").GetString() ==
                "PROFILE_MANIFEST_INVALID");
    }

    [Fact]
    public void Retarget_command_rejects_a_manifest_inside_the_output_tree()
    {
        using var fixture = SyntheticAssetFactory.WithShaderPlatforms(18);
        var dataRoot = CreateDataRoot(fixture, "globalgamemanagers");
        var inventory = ClassicPlayerTree.Discover(dataRoot);
        var outputRoot = Path.Combine(fixture.Root, "converted");
        Directory.CreateDirectory(outputRoot);
        var manifestPath = Path.Combine(outputRoot, "profile-manifest.json");
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        };
        File.WriteAllText(
            manifestPath,
            JsonSerializer.Serialize(ManifestFor(inventory), jsonOptions));
        var reportPath = Path.Combine(fixture.Root, "validation-report.json");

        var exitCode = Program.Main(
            [
                "retarget-classic-tree",
                dataRoot,
                outputRoot,
                manifestPath,
                reportPath,
            ]);

        Assert.Equal(3, exitCode);
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        Assert.Contains(
            report.RootElement.GetProperty("diagnostics").EnumerateArray(),
            diagnostic => diagnostic.GetProperty("code").GetString() ==
                "PATH_OVERLAP");
    }

    [Fact]
    public void Convert_rewrites_build_settings_only_in_the_output_global_managers()
    {
        using var fixture = SyntheticAssetFactory.WithShaderAndBuildSettings(
            [18],
            [17, 21],
            "6000.0.61f1");
        var dataRoot = CreateDataRoot(fixture, "globalgamemanagers");
        var inventory = ClassicPlayerTree.Discover(dataRoot);
        var validation = ClassicPlayerTree.Validate(inventory, ManifestFor(inventory));
        var outputRoot = Path.Combine(fixture.Root, "converted");

        ClassicTreeConverter.Convert(
            Assert.IsType<ClassicPlayerTree.ValidatedClassicPlayerLayout>(validation.Layout),
            outputRoot,
            Path.Combine(fixture.Root, "report.json"),
            ClassicTransformOptions.Default);

        var sourceGraphics = SyntheticAssetFactory.Inspect(
            Path.Combine(dataRoot, "globalgamemanagers")).GraphicsApis;
        var outputGraphics = SyntheticAssetFactory.Inspect(
            Path.Combine(outputRoot, "globalgamemanagers")).GraphicsApis;
        Assert.NotNull(sourceGraphics);
        Assert.NotNull(outputGraphics);
        Assert.Equal([17, 21], sourceGraphics);
        Assert.Equal([21], outputGraphics);
    }

    [Fact]
    public void Convert_normalizes_unity_and_build_settings_versions_only_when_requested()
    {
        using var fixture = SyntheticAssetFactory.WithShaderAndBuildSettings(
            [18],
            [17, 21],
            "6000.0.61f1-internal-branch");
        var dataRoot = CreateDataRoot(fixture, "globalgamemanagers");
        var inventory = ClassicPlayerTree.Discover(dataRoot);
        var validation = ClassicPlayerTree.Validate(inventory, ManifestFor(inventory));
        var outputRoot = Path.Combine(fixture.Root, "converted");
        var options = ClassicTransformOptions.Default with
        {
            UnityStamp = "6000.0.61f1",
        };

        ClassicTreeConverter.Convert(
            Assert.IsType<ClassicPlayerTree.ValidatedClassicPlayerLayout>(validation.Layout),
            outputRoot,
            Path.Combine(fixture.Root, "report.json"),
            options);

        var source = SyntheticAssetFactory.Inspect(
            Path.Combine(dataRoot, "globalgamemanagers"));
        var output = SyntheticAssetFactory.Inspect(
            Path.Combine(outputRoot, "globalgamemanagers"));
        Assert.Equal("6000.0.61f1-internal-branch", source.BuildVersion);
        Assert.Equal("6000.0.61f1", output.BuildVersion);
        Assert.Equal("6000.0.61f1", output.UnityVersion);
    }

    [Fact]
    public void Convert_reports_desktop_builtins_and_plugins_without_copying_them()
    {
        using var fixture = SyntheticAssetFactory.WithShaderAndBuildSettings(
            [18],
            [17, 21],
            "6000.0.61f1");
        var dataRoot = CreateDataRoot(fixture, "globalgamemanagers");
        File.WriteAllBytes(Path.Combine(dataRoot, "unity default resources"), [1]);
        var pluginDirectory = Path.Combine(dataRoot, "Plugins", "x86_64");
        Directory.CreateDirectory(pluginDirectory);
        File.WriteAllBytes(Path.Combine(pluginDirectory, "desktop.so"), [2]);
        var inventory = ClassicPlayerTree.Discover(dataRoot);
        var validation = ClassicPlayerTree.Validate(inventory, ManifestFor(inventory));
        var outputRoot = Path.Combine(fixture.Root, "converted");

        var report = ClassicTreeConverter.Convert(
            Assert.IsType<ClassicPlayerTree.ValidatedClassicPlayerLayout>(validation.Layout),
            outputRoot,
            Path.Combine(fixture.Root, "report.json"),
            ClassicTransformOptions.Default);

        Assert.False(File.Exists(Path.Combine(outputRoot, "unity default resources")));
        Assert.False(File.Exists(Path.Combine(outputRoot, "Plugins", "x86_64", "desktop.so")));
        Assert.Equal(
            "REPLACEMENT_REQUIRED",
            Assert.Single(
                report.Files,
                file => file.Path == "unity default resources").Action);
        Assert.Equal(
            "EXCLUDED",
            Assert.Single(
                report.Files,
                file => file.Path == "Plugins/x86_64/desktop.so").Action);
    }

    private static string CreateDataRoot(
        SyntheticSerializedFile fixture,
        params string[] relativePaths)
    {
        var dataRoot = Path.Combine(fixture.Root, "Hollow Knight_Data");
        Directory.CreateDirectory(dataRoot);
        foreach (var relativePath in relativePaths)
        {
            File.Copy(fixture.InputPath, Path.Combine(dataRoot, relativePath));
        }
        return dataRoot;
    }

    private static ClassicProfileManifest ManifestFor(
        ClassicPlayerInventory inventory,
        params ClassicManifestFile[] additionalFiles)
    {
        var files = inventory.Files
            .Select(file => new ClassicManifestFile(
                file.RelativePath,
                file.Size,
                file.Sha256,
                file.Kind switch
                {
                    ClassicFileKind.Serialized => ClassicManifestAction.Transform,
                    ClassicFileKind.NativePlugin => ClassicManifestAction.Exclude,
                    ClassicFileKind.ReplacementRequired =>
                        ClassicManifestAction.ReplaceAtAssembly,
                    _ => ClassicManifestAction.Copy,
                },
                file.OwnerRelativePath))
            .Concat(additionalFiles)
            .ToArray();
        var manifest = new ClassicProfileManifest(
            1,
            "hollow-knight",
            "1.5.12620",
            "6000.0.61f1",
            "linux",
            files,
            1,
            string.Empty);
        return manifest with
        {
            ManifestSha256 = ClassicPlayerTree.ComputeManifestSha256(manifest),
        };
    }
}
