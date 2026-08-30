using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

internal sealed record ClassicTransformOptions(
    string TransformerVersion,
    int TargetPlatform,
    bool RequireVulkan,
    IReadOnlyList<int> GraphicsApis,
    string? UnityStamp)
{
    internal static ClassicTransformOptions Default { get; } = new(
        "1",
        SerializedFileTransformer.AndroidBuildTarget,
        true,
        [21],
        null);
}

internal sealed record ClassicFileReport(
    string Path,
    string Kind,
    string? Owner,
    string Action,
    long InputSize,
    string InputSha256,
    long? OutputSize,
    string? OutputSha256,
    int? TargetBefore,
    int? TargetAfter,
    int? ShadersSeen,
    int? VulkanShaders,
    int? MissingVulkanShaders);

internal sealed record ClassicCensus(
    int SerializedFiles,
    int Sidecars,
    int Shaders,
    int MissingVulkanShaders,
    IReadOnlyList<string> NativePlugins,
    IReadOnlyList<string> Media);

internal sealed record ClassicTreeReport(
    int SchemaVersion,
    string Command,
    string Status,
    string ProfileId,
    string GameVersion,
    string SourceTreeSha256,
    string ProfileManifestSha256,
    string ResumeKey,
    ClassicTransformOptions Transformer,
    IReadOnlyList<ClassicFileReport> Files,
    ClassicCensus Census,
    IReadOnlyList<ClassicDiagnostic> Diagnostics);

internal sealed record ClassicInventoryReport(
    int SchemaVersion,
    string Command,
    string Status,
    string SourceTreeSha256,
    ClassicSourceIdentity? Identity,
    IReadOnlyList<ClassicFileReport> Files,
    ClassicCensus Census,
    IReadOnlyList<ClassicDiagnostic> Diagnostics);

internal static class ClassicTreeConverter
{
    private static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    internal static ClassicTreeReport Convert(
        ClassicPlayerTree.ValidatedClassicPlayerLayout layout,
        string outputRoot,
        string reportPath,
        ClassicTransformOptions options)
    {
        if (!options.RequireVulkan ||
            options.TargetPlatform != SerializedFileTransformer.AndroidBuildTarget)
        {
            throw new InvalidDataException(
                "Classic conversion requires Vulkan and Android target 13");
        }

        var sourceRoot = Path.GetFullPath(layout.Inventory.SourceRoot);
        var resolvedOutputRoot = Path.GetFullPath(outputRoot);
        var resolvedReportPath = Path.GetFullPath(reportPath);
        ValidateDisjointPaths(sourceRoot, resolvedOutputRoot, resolvedReportPath);
        EnsureNoReparsePoint(resolvedOutputRoot);
        EnsureNoReparsePoint(Path.GetDirectoryName(resolvedReportPath)!);
        Directory.CreateDirectory(resolvedOutputRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(resolvedReportPath)!);

        var resumeKey = CreateResumeKey(layout, options);
        var priorFiles = LoadPriorFiles(resolvedReportPath, resumeKey);
        var reports = new List<ClassicFileReport>();
        try
        {
            foreach (var inventoryFile in layout.Inventory.Files)
            {
                var manifestFile = layout.ManifestFiles[inventoryFile.RelativePath];
                var sourcePath = ResolveContained(sourceRoot, inventoryFile.RelativePath);
                var outputPath = ResolveContained(
                    resolvedOutputRoot,
                    inventoryFile.RelativePath);
                if ((manifestFile.Action is ClassicManifestAction.Transform or
                        ClassicManifestAction.Copy) &&
                    TryReuse(inventoryFile, outputPath, priorFiles, out var reused))
                {
                    reports.Add(reused);
                    continue;
                }
                switch (manifestFile.Action)
                {
                    case ClassicManifestAction.Transform:
                        reports.Add(TransformSerialized(
                            inventoryFile,
                            sourcePath,
                            outputPath,
                            options));
                        break;
                    case ClassicManifestAction.Copy:
                        reports.Add(CopyFile(
                            inventoryFile,
                            sourcePath,
                            outputPath));
                        break;
                    case ClassicManifestAction.Exclude:
                        reports.Add(CreateNonOutputReport(
                            inventoryFile,
                            manifestFile,
                            "EXCLUDED"));
                        break;
                    case ClassicManifestAction.ReplaceAtAssembly:
                        reports.Add(CreateNonOutputReport(
                            inventoryFile,
                            manifestFile,
                            "REPLACEMENT_REQUIRED"));
                        break;
                    default:
                        throw new InvalidDataException(
                            $"Unsupported manifest action: {manifestFile.Action}");
                }
            }

            var report = new ClassicTreeReport(
                1,
                "retarget-classic-tree",
                "COMPLETE",
                layout.Manifest.ProfileId,
                layout.Manifest.GameVersion,
                layout.Inventory.SourceTreeSha256,
                layout.Manifest.ManifestSha256,
                resumeKey,
                options,
                reports,
                CreateCensus(layout.Inventory, reports),
                []);
            PublishReport(resolvedReportPath, report);
            return report;
        }
        catch (Exception error)
        {
            var partPath = resolvedReportPath + ".part";
            if (File.Exists(partPath)) File.Delete(partPath);
            var failure = new ClassicTreeReport(
                1,
                "retarget-classic-tree",
                "TRANSFORM_FAILED",
                layout.Manifest.ProfileId,
                layout.Manifest.GameVersion,
                layout.Inventory.SourceTreeSha256,
                layout.Manifest.ManifestSha256,
                resumeKey,
                options,
                reports,
                CreateCensus(layout.Inventory, reports),
                [new ClassicDiagnostic("TRANSFORM_FAILED", error.Message)]);
            PublishReport(resolvedReportPath, failure);
            throw;
        }
    }

    internal static ClassicTreeReport WriteValidationFailure(
        ClassicPlayerInventory inventory,
        ClassicProfileManifest manifest,
        string reportPath,
        IReadOnlyList<ClassicDiagnostic> diagnostics)
    {
        var resolvedReportPath = Path.GetFullPath(reportPath);
        if (IsInside(inventory.SourceRoot, resolvedReportPath))
        {
            throw new InvalidDataException(
                $"Classic validation report cannot be written inside its source: {reportPath}");
        }
        var reportDirectory = Path.GetDirectoryName(resolvedReportPath)!;
        EnsureNoReparsePoint(reportDirectory);
        Directory.CreateDirectory(reportDirectory);
        var files = inventory.Files.Select(input => new ClassicFileReport(
            input.RelativePath,
            input.Kind.ToString(),
            input.OwnerRelativePath,
            "INVENTORIED",
            input.Size,
            input.Sha256,
            null,
            null,
            null,
            null,
            null,
            null,
            null)).ToArray();
        var report = new ClassicTreeReport(
            1,
            "retarget-classic-tree",
            "VALIDATION_FAILED",
            manifest.ProfileId,
            manifest.GameVersion,
            inventory.SourceTreeSha256,
            manifest.ManifestSha256,
            string.Empty,
            ClassicTransformOptions.Default,
            files,
            CreateCensus(inventory, files),
            diagnostics);
        PublishReport(resolvedReportPath, report);
        return report;
    }

    internal static ClassicInventoryReport WriteInventoryReport(
        ClassicPlayerInventory inventory,
        string reportPath)
    {
        var sourceRoot = Path.GetFullPath(inventory.SourceRoot);
        var resolvedReportPath = Path.GetFullPath(reportPath);
        if (IsInside(sourceRoot, resolvedReportPath))
        {
            throw new InvalidDataException(
                $"Classic inventory report cannot be written inside its source: {reportPath}");
        }
        var reportDirectory = Path.GetDirectoryName(resolvedReportPath)!;
        EnsureNoReparsePoint(reportDirectory);
        Directory.CreateDirectory(reportDirectory);

        var files = new List<ClassicFileReport>();
        foreach (var input in inventory.Files)
        {
            TransformReport? inspected = null;
            if (input.Kind == ClassicFileKind.Serialized)
            {
                inspected = SerializedFileTransformer.Inspect(
                    ResolveContained(sourceRoot, input.RelativePath));
            }
            files.Add(new ClassicFileReport(
                input.RelativePath,
                input.Kind.ToString(),
                input.OwnerRelativePath,
                "INVENTORIED",
                input.Size,
                input.Sha256,
                null,
                null,
                inspected?.OriginalTarget,
                inspected?.NewTarget,
                inspected?.ShadersSeen,
                inspected?.VulkanShaders,
                inspected?.MissingVulkanShaders));
        }

        var report = new ClassicInventoryReport(
            1,
            "manifest-classic-tree",
            "INVENTORIED",
            inventory.SourceTreeSha256,
            inventory.Identity,
            files,
            CreateCensus(inventory, files),
            inventory.Diagnostics);
        PublishInventoryReport(resolvedReportPath, report);
        return report;
    }

    private static IReadOnlyDictionary<string, ClassicFileReport> LoadPriorFiles(
        string reportPath,
        string resumeKey)
    {
        if (!File.Exists(reportPath))
        {
            return new Dictionary<string, ClassicFileReport>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var report = JsonSerializer.Deserialize<ClassicTreeReport>(
                File.ReadAllText(reportPath),
                JsonOptions);
            if (report is null ||
                report.SchemaVersion != 1 ||
                report.Status != "COMPLETE" ||
                report.ResumeKey != resumeKey)
            {
                return new Dictionary<string, ClassicFileReport>(
                    StringComparer.OrdinalIgnoreCase);
            }
            return report.Files.ToDictionary(
                file => file.Path,
                StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, ClassicFileReport>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static bool TryReuse(
        ClassicFileEntry input,
        string outputPath,
        IReadOnlyDictionary<string, ClassicFileReport> priorFiles,
        out ClassicFileReport reused)
    {
        reused = null!;
        if (!priorFiles.TryGetValue(input.RelativePath, out var prior) ||
            prior.OutputSha256 is null ||
            !string.Equals(
                prior.InputSha256,
                input.Sha256,
                StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(outputPath) ||
            new FileInfo(outputPath).Length != prior.OutputSize ||
            !string.Equals(
                HashFile(outputPath),
                prior.OutputSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        reused = prior with
        {
            Action = "REUSED",
            InputSize = input.Size,
            InputSha256 = input.Sha256,
        };
        return true;
    }

    private static ClassicFileReport TransformSerialized(
        ClassicFileEntry input,
        string sourcePath,
        string outputPath,
        ClassicTransformOptions options)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var isGlobalManagers = string.Equals(
            Path.GetFileName(input.RelativePath),
            "globalgamemanagers",
            StringComparison.OrdinalIgnoreCase);
        var transformed = SerializedFileTransformer.Transform(
            sourcePath,
            outputPath,
            new SerializedTransformOptions(
                options.RequireVulkan,
                isGlobalManagers ? options.GraphicsApis : null,
                options.UnityStamp,
                isGlobalManagers ? options.UnityStamp : null));
        var outputHash = HashFile(outputPath);
        return new ClassicFileReport(
            input.RelativePath,
            input.Kind.ToString(),
            input.OwnerRelativePath,
            "TRANSFORMED",
            input.Size,
            input.Sha256,
            new FileInfo(outputPath).Length,
            outputHash,
            transformed.OriginalTarget,
            transformed.NewTarget,
            transformed.ShadersSeen,
            transformed.VulkanShaders,
            transformed.MissingVulkanShaders);
    }

    private static ClassicFileReport CopyFile(
        ClassicFileEntry input,
        string sourcePath,
        string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var partPath = outputPath + ".part";
        if (File.Exists(partPath)) File.Delete(partPath);
        try
        {
            File.Copy(sourcePath, partPath);
            var outputHash = HashFile(partPath);
            if (!string.Equals(outputHash, input.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Copied file failed SHA-256 verification: {input.RelativePath}");
            }
            File.Move(partPath, outputPath, overwrite: true);
            return new ClassicFileReport(
                input.RelativePath,
                input.Kind.ToString(),
                input.OwnerRelativePath,
                "COPIED",
                input.Size,
                input.Sha256,
                new FileInfo(outputPath).Length,
                outputHash,
                null,
                null,
                null,
                null,
                null);
        }
        catch
        {
            if (File.Exists(partPath)) File.Delete(partPath);
            throw;
        }
    }

    private static ClassicFileReport CreateNonOutputReport(
        ClassicFileEntry input,
        ClassicManifestFile manifest,
        string action) =>
        new(
            input.RelativePath,
            input.Kind.ToString(),
            manifest.OwnerRelativePath ?? input.OwnerRelativePath,
            action,
            input.Size,
            input.Sha256,
            null,
            null,
            null,
            null,
            null,
            null,
            null);

    private static ClassicCensus CreateCensus(
        ClassicPlayerInventory inventory,
        IReadOnlyList<ClassicFileReport> reports) =>
        new(
            inventory.Files.Count(file => file.Kind == ClassicFileKind.Serialized),
            inventory.Files.Count(file => file.Kind == ClassicFileKind.Sidecar),
            reports.Sum(report => report.ShadersSeen ?? 0),
            reports.Sum(report => report.MissingVulkanShaders ?? 0),
            inventory.Files
                .Where(file => file.Kind == ClassicFileKind.NativePlugin)
                .Select(file => file.RelativePath)
                .ToArray(),
            inventory.Files
                .Where(file => file.Kind == ClassicFileKind.Media)
                .Select(file => file.RelativePath)
                .ToArray());

    private static string CreateResumeKey(
        ClassicPlayerTree.ValidatedClassicPlayerLayout layout,
        ClassicTransformOptions options)
    {
        var canonical = string.Join(
            "\n",
            "classic-tree-report:1",
            layout.Manifest.ProfileId,
            layout.Manifest.GameVersion,
            layout.Inventory.SourceTreeSha256,
            layout.Manifest.ManifestSha256,
            options.TransformerVersion,
            options.TargetPlatform.ToString(),
            options.RequireVulkan.ToString(),
            string.Join(',', options.GraphicsApis),
            options.UnityStamp ?? string.Empty);
        return System.Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    private static void PublishReport(string reportPath, ClassicTreeReport report)
    {
        var partPath = reportPath + ".part";
        if (File.Exists(partPath)) File.Delete(partPath);
        File.WriteAllText(partPath, JsonSerializer.Serialize(report, JsonOptions));
        var reopened = JsonSerializer.Deserialize<ClassicTreeReport>(
            File.ReadAllText(partPath),
            JsonOptions) ?? throw new InvalidDataException("Classic report could not be reopened");
        if (reopened.SchemaVersion != report.SchemaVersion ||
            reopened.Status != report.Status ||
            reopened.ResumeKey != report.ResumeKey ||
            reopened.Files.Count != report.Files.Count)
        {
            throw new InvalidDataException("Classic report failed publication verification");
        }
        File.Move(partPath, reportPath, overwrite: true);
    }

    private static void PublishInventoryReport(
        string reportPath,
        ClassicInventoryReport report)
    {
        var partPath = reportPath + ".part";
        if (File.Exists(partPath)) File.Delete(partPath);
        try
        {
            File.WriteAllText(partPath, JsonSerializer.Serialize(report, JsonOptions));
            var reopened = JsonSerializer.Deserialize<ClassicInventoryReport>(
                File.ReadAllText(partPath),
                JsonOptions) ?? throw new InvalidDataException(
                    "Classic inventory report could not be reopened");
            if (reopened.SchemaVersion != report.SchemaVersion ||
                reopened.Status != "INVENTORIED" ||
                reopened.SourceTreeSha256 != report.SourceTreeSha256 ||
                reopened.Files.Count != report.Files.Count)
            {
                throw new InvalidDataException(
                    "Classic inventory report failed publication verification");
            }
            File.Move(partPath, reportPath, overwrite: true);
        }
        catch
        {
            if (File.Exists(partPath)) File.Delete(partPath);
            throw;
        }
    }

    private static void ValidateDisjointPaths(
        string sourceRoot,
        string outputRoot,
        string reportPath)
    {
        if (PathsOverlap(sourceRoot, outputRoot))
        {
            throw new InvalidDataException(
                $"Classic source and output roots overlap: {sourceRoot} / {outputRoot}");
        }
        if (IsInside(sourceRoot, reportPath) || IsInside(outputRoot, reportPath))
        {
            throw new InvalidDataException(
                $"Classic report path overlaps source or output: {reportPath}");
        }
    }

    private static bool PathsOverlap(string left, string right) =>
        IsInside(left, right) || IsInside(right, left);

    private static bool IsInside(string root, string candidate)
    {
        var relative = Path.GetRelativePath(root, candidate);
        return relative == "." ||
            (relative != ".." &&
             !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
             !Path.IsPathRooted(relative));
    }

    private static void EnsureNoReparsePoint(string path)
    {
        var current = new DirectoryInfo(Path.GetFullPath(path));
        while (current is not null && current.Exists)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    $"Classic output/report path contains a reparse point: {current.FullName}");
            }
            current = current.Parent;
        }
    }

    private static string ResolveContained(string root, string relativePath)
    {
        var resolved = Path.GetFullPath(
            Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsInside(root, resolved))
        {
            throw new InvalidDataException($"Classic path escapes its root: {relativePath}");
        }
        return resolved;
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return System.Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
