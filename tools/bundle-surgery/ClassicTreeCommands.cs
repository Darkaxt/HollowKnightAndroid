using System.Text.Json;
using System.Text.Json.Serialization;

internal static class ClassicTreeCommands
{
    private static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    internal static int Manifest(string sourceRoot, string reportPath)
    {
        try
        {
            var inventory = ClassicPlayerTree.Discover(sourceRoot);
            ClassicTreeConverter.WriteInventoryReport(inventory, reportPath);
            return inventory.Diagnostics.Count == 0 ? 0 : 3;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"manifest-classic-tree failed: {error.Message}");
            return 1;
        }
    }

    internal static int Retarget(
        string sourceRoot,
        string outputRoot,
        string manifestPath,
        string reportPath)
    {
        var inventory = ClassicPlayerTree.Discover(sourceRoot);
        var pathDiagnostics = ValidateCommandPaths(
            sourceRoot,
            outputRoot,
            manifestPath,
            reportPath);
        ClassicProfileManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ClassicProfileManifest>(
                File.ReadAllText(manifestPath),
                JsonOptions) ?? throw new InvalidDataException(
                    "Profile manifest is empty");
        }
        catch (Exception error) when (error is IOException or JsonException or InvalidDataException)
        {
            Console.Error.WriteLine($"retarget-classic-tree validation failed: {error.Message}");
            var invalidManifest = new ClassicProfileManifest(
                1,
                string.Empty,
                string.Empty,
                string.Empty,
                "linux",
                [],
                1,
                new string('0', 64));
            var diagnostics = inventory.Diagnostics
                .Concat(pathDiagnostics)
                .Append(new ClassicDiagnostic(
                    "PROFILE_MANIFEST_INVALID",
                    error.Message,
                    Path.GetFileName(manifestPath)))
                .ToArray();
            try
            {
                ClassicTreeConverter.WriteValidationFailure(
                    inventory,
                    invalidManifest,
                    reportPath,
                    diagnostics);
            }
            catch (Exception reportError)
            {
                Console.Error.WriteLine(
                    $"retarget-classic-tree could not publish validation report: {reportError.Message}");
                return 1;
            }
            return 3;
        }

        var validation = ClassicPlayerTree.Validate(inventory, manifest);
        var allDiagnostics = validation.Diagnostics.Concat(pathDiagnostics).ToArray();
        if (validation.Layout is null || pathDiagnostics.Count > 0)
        {
            foreach (var diagnostic in allDiagnostics)
            {
                Console.Error.WriteLine(
                    $"{diagnostic.Code}: {diagnostic.RelativePath ?? diagnostic.Message}");
            }
            try
            {
                ClassicTreeConverter.WriteValidationFailure(
                    inventory,
                    manifest,
                    reportPath,
                    allDiagnostics);
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(
                    $"retarget-classic-tree could not publish validation report: {error.Message}");
                return 1;
            }
            return 3;
        }

        try
        {
            ClassicTreeConverter.Convert(
                validation.Layout,
                outputRoot,
                reportPath,
                ClassicTransformOptions.Default);
            return 0;
        }
        catch (InvalidDataException error)
        {
            Console.Error.WriteLine($"retarget-classic-tree transform failed: {error.Message}");
            return 4;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"retarget-classic-tree failed: {error.Message}");
            return 1;
        }
    }

    private static IReadOnlyList<ClassicDiagnostic> ValidateCommandPaths(
        string sourceRoot,
        string outputRoot,
        string manifestPath,
        string reportPath)
    {
        var source = Path.GetFullPath(sourceRoot);
        var output = Path.GetFullPath(outputRoot);
        var manifest = Path.GetFullPath(manifestPath);
        var report = Path.GetFullPath(reportPath);
        var diagnostics = new List<ClassicDiagnostic>();

        if (PathsOverlap(source, output))
        {
            diagnostics.Add(new ClassicDiagnostic(
                "PATH_OVERLAP",
                "Classic source and output roots must be disjoint"));
        }
        if (IsInside(source, manifest) || IsInside(output, manifest))
        {
            diagnostics.Add(new ClassicDiagnostic(
                "PATH_OVERLAP",
                "Profile manifest must be outside the source and output trees",
                manifestPath));
        }
        if (IsInside(source, report) || IsInside(output, report))
        {
            diagnostics.Add(new ClassicDiagnostic(
                "PATH_OVERLAP",
                "Report must be outside the source and output trees",
                reportPath));
        }
        if (string.Equals(manifest, report, StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(new ClassicDiagnostic(
                "PATH_OVERLAP",
                "Profile manifest and report paths must differ",
                reportPath));
        }
        return diagnostics;
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
}
