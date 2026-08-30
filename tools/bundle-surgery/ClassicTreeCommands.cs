using System.Text.Json;
using System.Text.Json.Serialization;

internal static class ClassicTreeCommands
{
    private static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
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

    internal static int CreateProfileManifest(
        string sourceRoot,
        string profileId,
        string gameVersion,
        string unityVersion,
        string manifestPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(profileId) ||
                string.IsNullOrWhiteSpace(gameVersion) ||
                string.IsNullOrWhiteSpace(unityVersion))
            {
                throw new InvalidDataException(
                    "Profile ID, game version, and Unity version are required");
            }

            var inventory = ClassicPlayerTree.Discover(sourceRoot);
            if (inventory.Diagnostics.Count > 0)
            {
                foreach (var diagnostic in inventory.Diagnostics)
                {
                    Console.Error.WriteLine(
                        $"{diagnostic.Code}: {diagnostic.RelativePath ?? diagnostic.Message}");
                }
                return 3;
            }
            if (profileId.Equals("hollow-knight", StringComparison.OrdinalIgnoreCase))
            {
                ValidateHollowKnightIdentity(
                    inventory.Identity,
                    gameVersion,
                    unityVersion);
            }

            var resolvedManifestPath = Path.GetFullPath(manifestPath);
            if (IsInside(Path.GetFullPath(sourceRoot), resolvedManifestPath))
            {
                throw new InvalidDataException(
                    "Profile manifest cannot be written inside its source tree");
            }
            EnsureNoReparsePoint(Path.GetDirectoryName(resolvedManifestPath)!);

            var files = inventory.Files.Select(file => new ClassicManifestFile(
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
                file.OwnerRelativePath)).ToArray();
            var draft = new ClassicProfileManifest(
                1,
                profileId,
                gameVersion,
                unityVersion,
                "linux",
                files,
                1,
                string.Empty);
            var manifest = draft with
            {
                ManifestSha256 = ClassicPlayerTree.ComputeManifestSha256(draft),
            };
            var validation = ClassicPlayerTree.Validate(inventory, manifest);
            if (validation.Layout is null)
            {
                throw new InvalidDataException(
                    "Generated profile manifest did not validate: " +
                    string.Join(
                        "; ",
                        validation.Diagnostics.Select(diagnostic => diagnostic.Code)));
            }

            var directory = Path.GetDirectoryName(resolvedManifestPath)!;
            Directory.CreateDirectory(directory);
            var partPath = resolvedManifestPath + ".part";
            try
            {
                var manifestJson = JsonSerializer.Serialize(manifest, JsonOptions)
                    .Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
                File.WriteAllText(partPath, manifestJson);
                var reopened = JsonSerializer.Deserialize<ClassicProfileManifest>(
                    File.ReadAllText(partPath),
                    JsonOptions) ?? throw new InvalidDataException(
                        "Generated profile manifest could not be reopened");
                if (ClassicPlayerTree.Validate(inventory, reopened).Layout is null)
                {
                    throw new InvalidDataException(
                        "Reopened profile manifest failed exact validation");
                }
                File.Move(partPath, resolvedManifestPath, overwrite: true);
            }
            catch
            {
                if (File.Exists(partPath)) File.Delete(partPath);
                throw;
            }
            return 0;
        }
        catch (InvalidDataException error)
        {
            Console.Error.WriteLine(
                $"create-classic-profile-manifest validation failed: {error.Message}");
            return 3;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(
                $"create-classic-profile-manifest failed: {error.Message}");
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

    private static void ValidateHollowKnightIdentity(
        ClassicSourceIdentity? identity,
        string gameVersion,
        string unityVersion)
    {
        if (identity is null ||
            !string.Equals(identity.CompanyName, "Team Cherry", StringComparison.Ordinal) ||
            !string.Equals(identity.ProductName, "Hollow Knight", StringComparison.Ordinal) ||
            !string.Equals(identity.Platform, "LinuxPlayer", StringComparison.Ordinal) ||
            !string.Equals(identity.UnityVersion, unityVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Hollow Knight source identity does not match its requested profile");
        }
        if (!int.TryParse(
                gameVersion[(gameVersion.LastIndexOf('.') + 1)..],
                out var expectedRevision) ||
            identity.BuildRevision != expectedRevision)
        {
            throw new InvalidDataException(
                $"Hollow Knight build revision {identity.BuildRevision?.ToString() ?? "missing"} " +
                $"does not match requested version {gameVersion}");
        }
        if (!string.IsNullOrWhiteSpace(identity.GameVersion) &&
            !string.Equals(identity.GameVersion, gameVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Hollow Knight PlayerSettings version {identity.GameVersion} " +
                $"does not match requested version {gameVersion}");
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
                    $"Profile manifest path contains a reparse point: {current.FullName}");
            }
            current = current.Parent;
        }
    }
}
