using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

internal enum ClassicFileKind
{
    Serialized,
    Sidecar,
    Media,
    NativePlugin,
    PassThrough,
    ReplacementRequired,
}

internal enum ClassicManifestAction
{
    Transform,
    Copy,
    Exclude,
    ReplaceAtAssembly,
}

internal sealed record ClassicDiagnostic(
    string Code,
    string Message,
    string? RelativePath = null);

internal sealed record ClassicFileEntry(
    string RelativePath,
    ClassicFileKind Kind,
    long Size,
    string Sha256,
    string? OwnerRelativePath);

internal sealed record ClassicPlayerInventory(
    string SourceRoot,
    string SourceTreeSha256,
    IReadOnlyList<ClassicFileEntry> Files,
    IReadOnlyList<ClassicDiagnostic> Diagnostics,
    ClassicSourceIdentity? Identity = null);

internal sealed record ClassicSourceIdentity(
    string? CompanyName,
    string? ProductName,
    string? GameVersion,
    string UnityVersion,
    string Platform,
    string? BuildBranch,
    int? BuildRevision);

internal sealed record ClassicManifestFile(
    string RelativePath,
    long Size,
    string Sha256,
    ClassicManifestAction Action,
    string? OwnerRelativePath);

internal sealed record ClassicProfileManifest(
    int SchemaVersion,
    string ProfileId,
    string GameVersion,
    string UnityVersion,
    string Platform,
    IReadOnlyList<ClassicManifestFile> RequiredFiles,
    int ConverterReportSchema,
    string ManifestSha256);

internal sealed record ClassicValidationResult(
    ClassicPlayerTree.ValidatedClassicPlayerLayout? Layout,
    IReadOnlyList<ClassicDiagnostic> Diagnostics);

internal static partial class ClassicPlayerTree
{
    internal const uint LinuxBuildTarget = 24;

    internal sealed class ValidatedClassicPlayerLayout
    {
        private ValidatedClassicPlayerLayout(
            ClassicPlayerInventory inventory,
            ClassicProfileManifest manifest,
            IReadOnlyDictionary<string, ClassicManifestFile> manifestFiles)
        {
            Inventory = inventory;
            Manifest = manifest;
            ManifestFiles = manifestFiles;
        }

        internal ClassicPlayerInventory Inventory { get; }
        internal ClassicProfileManifest Manifest { get; }
        internal IReadOnlyDictionary<string, ClassicManifestFile> ManifestFiles { get; }

        internal static ValidatedClassicPlayerLayout Create(
            ClassicPlayerInventory inventory,
            ClassicProfileManifest manifest,
            IReadOnlyDictionary<string, ClassicManifestFile> manifestFiles) =>
            new(inventory, manifest, manifestFiles);
    }

    internal static ClassicPlayerInventory Discover(string sourceRoot)
    {
        var root = Path.GetFullPath(sourceRoot);
        var files = new List<ClassicFileEntry>();
        var diagnostics = new List<ClassicDiagnostic>();

        if (!Directory.Exists(root))
        {
            diagnostics.Add(new ClassicDiagnostic(
                "SOURCE_ROOT_MISSING",
                $"Classic data root does not exist: {root}"));
            return CreateInventory(root, files, diagnostics);
        }
        if (IsReparsePoint(root))
        {
            diagnostics.Add(new ClassicDiagnostic(
                "REPARSE_POINT",
                $"Classic data root cannot be a reparse point: {root}"));
            return CreateInventory(root, files, diagnostics);
        }

        foreach (var path in EnumerateContainedFiles(root, diagnostics))
        {
            var relativePath = ToRelativePath(root, path);
            var kind = Classify(path, relativePath);
            files.Add(new ClassicFileEntry(
                relativePath,
                kind,
                new FileInfo(path).Length,
                HashFile(path),
                null));
            if (kind == ClassicFileKind.Serialized)
            {
                ValidateLinuxTarget(path, relativePath, diagnostics);
            }
        }

        files.Sort((left, right) =>
            ClassicPathComparer.Instance.Compare(left.RelativePath, right.RelativePath));
        ValidateLogicalPaths(files, diagnostics);
        ValidateNumericIndices(files, diagnostics);
        AssociateAndValidateSidecars(files, diagnostics);
        files.Sort((left, right) =>
        {
            var leftKey = left.OwnerRelativePath ?? left.RelativePath;
            var rightKey = right.OwnerRelativePath ?? right.RelativePath;
            var ownerOrder = ClassicPathComparer.Instance.Compare(leftKey, rightKey);
            return ownerOrder != 0
                ? ownerOrder
                : StringComparer.OrdinalIgnoreCase.Compare(
                    left.RelativePath,
                    right.RelativePath);
        });

        var globalManagers = files.Count(file =>
            file.Kind == ClassicFileKind.Serialized &&
            string.Equals(
                Path.GetFileName(file.RelativePath),
                "globalgamemanagers",
                StringComparison.OrdinalIgnoreCase));
        if (globalManagers != 1)
        {
            diagnostics.Add(new ClassicDiagnostic(
                "GLOBAL_MANAGERS_COUNT",
                $"Expected exactly one globalgamemanagers serialized file, found {globalManagers}"));
        }

        ClassicSourceIdentity? identity = null;
        if (globalManagers == 1)
        {
            var globalManagersPath = files.Single(file =>
                file.Kind == ClassicFileKind.Serialized &&
                string.Equals(
                    Path.GetFileName(file.RelativePath),
                    "globalgamemanagers",
                    StringComparison.OrdinalIgnoreCase));
            identity = ReadSourceIdentity(root, globalManagersPath, diagnostics);
        }

        return CreateInventory(root, files, diagnostics, identity);
    }

    internal static ClassicValidationResult Validate(
        ClassicPlayerInventory inventory,
        ClassicProfileManifest manifest)
    {
        var diagnostics = inventory.Diagnostics.ToList();
        var normalizedManifestFiles = new Dictionary<string, ClassicManifestFile>(
            StringComparer.OrdinalIgnoreCase);

        if (manifest.SchemaVersion != 1)
        {
            diagnostics.Add(new ClassicDiagnostic(
                "UNSUPPORTED_MANIFEST_SCHEMA",
                $"Manifest schema {manifest.SchemaVersion} is not supported"));
        }
        if (manifest.ConverterReportSchema != 1)
        {
            diagnostics.Add(new ClassicDiagnostic(
                "UNSUPPORTED_REPORT_SCHEMA",
                $"Converter report schema {manifest.ConverterReportSchema} is not supported"));
        }
        if (!string.Equals(manifest.Platform, "linux", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(new ClassicDiagnostic(
                "NON_LINUX_MANIFEST",
                $"Manifest platform must be linux, found {manifest.Platform}"));
        }
        if (!IsSha256(manifest.ManifestSha256) ||
            !string.Equals(
                manifest.ManifestSha256,
                ComputeManifestSha256(manifest),
                StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(new ClassicDiagnostic(
                "INVALID_MANIFEST_HASH",
                "Manifest SHA-256 does not match its canonical content"));
        }

        foreach (var manifestFile in manifest.RequiredFiles)
        {
            if (!TryNormalizeRelativePath(manifestFile.RelativePath, out var normalized))
            {
                diagnostics.Add(new ClassicDiagnostic(
                    "INVALID_MANIFEST_PATH",
                    $"Manifest path must be normalized and relative: {manifestFile.RelativePath}",
                    manifestFile.RelativePath));
                continue;
            }
            if (!normalizedManifestFiles.TryAdd(
                    normalized,
                    manifestFile with { RelativePath = normalized }))
            {
                diagnostics.Add(new ClassicDiagnostic(
                    "DUPLICATE_MANIFEST_PATH",
                    $"Manifest contains a case-insensitive duplicate path: {normalized}",
                    normalized));
            }
        }

        ValidateManifestNumericAliases(normalizedManifestFiles.Keys, diagnostics);
        var inventoryFiles = new Dictionary<string, ClassicFileEntry>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var inventoryFile in inventory.Files)
        {
            if (!inventoryFiles.TryAdd(inventoryFile.RelativePath, inventoryFile))
            {
                diagnostics.Add(new ClassicDiagnostic(
                    "DUPLICATE_SOURCE_PATH",
                    $"Source contains a case-insensitive duplicate path: {inventoryFile.RelativePath}",
                    inventoryFile.RelativePath));
            }
        }
        foreach (var (relativePath, manifestFile) in normalizedManifestFiles)
        {
            if (!inventoryFiles.TryGetValue(relativePath, out var inventoryFile))
            {
                diagnostics.Add(new ClassicDiagnostic(
                    "MISSING_REQUIRED_FILE",
                    $"Manifest-required file is missing: {relativePath}",
                    relativePath));
                continue;
            }
            if (inventoryFile.Size != manifestFile.Size)
            {
                diagnostics.Add(new ClassicDiagnostic(
                    "FILE_SIZE_MISMATCH",
                    $"File size differs from the manifest: {relativePath}",
                    relativePath));
            }
            if (!IsSha256(manifestFile.Sha256) ||
                !string.Equals(
                    inventoryFile.Sha256,
                    manifestFile.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(new ClassicDiagnostic(
                    "FILE_HASH_MISMATCH",
                    $"File SHA-256 differs from the manifest: {relativePath}",
                    relativePath));
            }
            ValidateAction(inventoryFile, manifestFile, diagnostics);
            ValidateManifestOwner(inventoryFiles, inventoryFile, manifestFile, diagnostics);
        }

        foreach (var inventoryFile in inventory.Files)
        {
            if (!normalizedManifestFiles.ContainsKey(inventoryFile.RelativePath))
            {
                diagnostics.Add(new ClassicDiagnostic(
                    "UNMANIFESTED_FILE",
                    $"Source contains a file absent from the exact manifest: {inventoryFile.RelativePath}",
                    inventoryFile.RelativePath));
            }
        }

        if (diagnostics.Count > 0)
        {
            return new ClassicValidationResult(null, diagnostics);
        }

        return new ClassicValidationResult(
            ValidatedClassicPlayerLayout.Create(
                inventory,
                manifest,
                normalizedManifestFiles),
            diagnostics);
    }

    internal static string ComputeManifestSha256(ClassicProfileManifest manifest)
    {
        var canonical = new StringBuilder()
            .Append(manifest.SchemaVersion).Append('\n')
            .Append(manifest.ProfileId).Append('\n')
            .Append(manifest.GameVersion).Append('\n')
            .Append(manifest.UnityVersion).Append('\n')
            .Append(manifest.Platform.ToLowerInvariant()).Append('\n')
            .Append(manifest.ConverterReportSchema).Append('\n');
        foreach (var file in manifest.RequiredFiles
                     .OrderBy(
                         file => file.RelativePath.ToLowerInvariant(),
                         StringComparer.Ordinal)
                     .ThenBy(file => file.RelativePath, StringComparer.Ordinal))
        {
            canonical
                .Append(file.RelativePath).Append('\0')
                .Append(file.Size.ToString(CultureInfo.InvariantCulture)).Append('\0')
                .Append(file.Sha256.ToLowerInvariant()).Append('\0')
                .Append(file.Action switch
                {
                    ClassicManifestAction.Transform => "TRANSFORM",
                    ClassicManifestAction.Copy => "COPY",
                    ClassicManifestAction.Exclude => "EXCLUDE",
                    ClassicManifestAction.ReplaceAtAssembly => "REPLACE_AT_ASSEMBLY",
                    _ => throw new InvalidDataException(
                        $"Unsupported manifest action: {file.Action}"),
                }).Append('\0')
                .Append(file.OwnerRelativePath ?? string.Empty).Append('\n');
        }
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();
    }

    private static ClassicPlayerInventory CreateInventory(
        string root,
        List<ClassicFileEntry> files,
        List<ClassicDiagnostic> diagnostics,
        ClassicSourceIdentity? identity = null)
    {
        var canonical = new StringBuilder();
        foreach (var file in files)
        {
            canonical
                .Append(file.RelativePath).Append('\0')
                .Append(file.Kind).Append('\0')
                .Append(file.Size.ToString(CultureInfo.InvariantCulture)).Append('\0')
                .Append(file.Sha256).Append('\n');
        }
        var treeHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();
        return new ClassicPlayerInventory(root, treeHash, files, diagnostics, identity);
    }

    private static ClassicSourceIdentity? ReadSourceIdentity(
        string root,
        ClassicFileEntry globalManagers,
        List<ClassicDiagnostic> diagnostics)
    {
        var manager = new AssetsManager();
        try
        {
            manager.LoadClassPackage(Path.Combine(AppContext.BaseDirectory, "classdata.tpk"));
            var path = Path.Combine(
                root,
                globalManagers.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            var file = manager.LoadAssetsFile(path);
            string? playerCompanyName = null;
            string? playerProductName = null;
            string? gameVersion = null;
            try
            {
                manager.LoadClassDatabaseFromPackage(file.file.Metadata.UnityVersion);
                var playerSettingsInfo = file.file
                    .GetAssetsOfType(AssetClassID.PlayerSettings)
                    .SingleOrDefault();
                if (playerSettingsInfo is not null)
                {
                    var playerSettings = manager.GetBaseField(file, playerSettingsInfo);
                    if (playerSettings is not null)
                    {
                        playerCompanyName = playerSettings["companyName"].AsString;
                        playerProductName = playerSettings["productName"].AsString;
                        gameVersion = playerSettings["bundleVersion"].AsString;
                    }
                }
            }
            catch (Exception error) when (
                error is EndOfStreamException or InvalidDataException)
            {
                // Some exact Unity patch releases have a PlayerSettings layout
                // newer than the nearest class-database template. Header target,
                // Unity version, app.info, BuildMetadata, and exact file hashes
                // remain authoritative; a guessed layout must not block them.
            }

            string? companyName = null;
            string? productName = null;
            var appInfoPath = Path.Combine(root, "app.info");
            if (File.Exists(appInfoPath))
            {
                var lines = File.ReadAllLines(appInfoPath);
                companyName = lines.ElementAtOrDefault(0)?.Trim();
                productName = lines.ElementAtOrDefault(1)?.Trim();
            }
            companyName ??= playerCompanyName;
            productName ??= playerProductName;

            string? buildBranch = null;
            int? buildRevision = null;
            var metadataPath = Path.Combine(
                root,
                "StreamingAssets",
                "BuildMetadata.json");
            if (File.Exists(metadataPath))
            {
                using var metadata = JsonDocument.Parse(File.ReadAllText(metadataPath));
                if (metadata.RootElement.TryGetProperty("branchName", out var branch))
                {
                    buildBranch = branch.GetString();
                }
                if (metadata.RootElement.TryGetProperty("revision", out var revision))
                {
                    if (revision.ValueKind == JsonValueKind.Number &&
                        revision.TryGetInt32(out var numericRevision))
                    {
                        buildRevision = numericRevision;
                    }
                    else if (revision.ValueKind == JsonValueKind.String &&
                        int.TryParse(
                            revision.GetString(),
                            NumberStyles.None,
                            CultureInfo.InvariantCulture,
                            out var stringRevision))
                    {
                        buildRevision = stringRevision;
                    }
                }
            }

            var platform = file.file.Metadata.TargetPlatform switch
            {
                LinuxBuildTarget => "LinuxPlayer",
                19 => "WindowsPlayer",
                var target => $"BuildTarget:{target}",
            };
            return new ClassicSourceIdentity(
                companyName,
                productName,
                gameVersion,
                file.file.Metadata.UnityVersion,
                platform,
                buildBranch,
                buildRevision);
        }
        catch (Exception error)
        {
            diagnostics.Add(new ClassicDiagnostic(
                "SOURCE_IDENTITY_INVALID",
                $"Classic source identity could not be read: {error.Message}",
                globalManagers.RelativePath));
            return null;
        }
        finally
        {
            manager.UnloadAll();
        }
    }

    private static IEnumerable<string> EnumerateContainedFiles(
        string root,
        List<ClassicDiagnostic> diagnostics)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var childDirectory in Directory.EnumerateDirectories(directory))
            {
                var relativePath = ToRelativePath(root, childDirectory);
                if (!IsContained(root, childDirectory))
                {
                    diagnostics.Add(new ClassicDiagnostic(
                        "PATH_ESCAPE",
                        $"Directory escapes classic data root: {relativePath}",
                        relativePath));
                    continue;
                }
                if (IsReparsePoint(childDirectory))
                {
                    diagnostics.Add(new ClassicDiagnostic(
                        "REPARSE_POINT",
                        $"Reparse-point directory is not allowed: {relativePath}",
                        relativePath));
                    continue;
                }
                pending.Push(childDirectory);
            }

            foreach (var file in Directory.EnumerateFiles(directory))
            {
                var relativePath = ToRelativePath(root, file);
                if (!IsContained(root, file))
                {
                    diagnostics.Add(new ClassicDiagnostic(
                        "PATH_ESCAPE",
                        $"File escapes classic data root: {relativePath}",
                        relativePath));
                    continue;
                }
                if (IsReparsePoint(file))
                {
                    diagnostics.Add(new ClassicDiagnostic(
                        "REPARSE_POINT",
                        $"Reparse-point file is not allowed: {relativePath}",
                        relativePath));
                    continue;
                }
                yield return Path.GetFullPath(file);
            }
        }
    }

    private static ClassicFileKind Classify(string path, string relativePath)
    {
        if (IsSidecar(path)) return ClassicFileKind.Sidecar;
        var name = Path.GetFileName(path);
        if (name.Equals("unity default resources", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("unity_builtin_extra", StringComparison.OrdinalIgnoreCase))
        {
            return ClassicFileKind.ReplacementRequired;
        }
        if (SerializedFileDetection.IsSerializedFile(path)) return ClassicFileKind.Serialized;

        if (relativePath.StartsWith("Managed/", StringComparison.OrdinalIgnoreCase) &&
            Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return ClassicFileKind.PassThrough;
        }

        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".so" or ".dll" or ".dylib" => ClassicFileKind.NativePlugin,
            ".mp4" or ".webm" or ".ogv" or ".wav" or ".ogg" or ".mp3" =>
                ClassicFileKind.Media,
            _ => ClassicFileKind.PassThrough,
        };
    }

    private static bool IsContained(string root, string candidate)
    {
        var relative = Path.GetRelativePath(root, Path.GetFullPath(candidate));
        return relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
            !Path.IsPathRooted(relative);
    }

    private static string ToRelativePath(string root, string path) =>
        Path.GetRelativePath(root, Path.GetFullPath(path))
            .Replace(Path.DirectorySeparatorChar, '/');

    private static bool TryNormalizeRelativePath(string path, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(path) ||
            path.Contains('\\') ||
            Path.IsPathRooted(path) ||
            DriveQualifiedPath().IsMatch(path))
        {
            return false;
        }

        var segments = path.Split('/');
        if (segments.Any(segment =>
                segment.Length == 0 ||
                segment == "." ||
                segment == ".." ||
                segment.EndsWith('.') ||
                segment.EndsWith(' ')))
        {
            return false;
        }
        normalized = string.Join('/', segments);
        return string.Equals(path, normalized, StringComparison.Ordinal);
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static bool IsSidecar(string path) =>
        path.EndsWith(".resS", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".resource", StringComparison.OrdinalIgnoreCase);

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static bool IsSha256(string value) => Sha256Text().IsMatch(value);

    private static void ValidateLinuxTarget(
        string path,
        string relativePath,
        List<ClassicDiagnostic> diagnostics)
    {
        var manager = new AssetsManager();
        try
        {
            var file = manager.LoadAssetsFile(path);
            if (file.file.Metadata.TargetPlatform != LinuxBuildTarget)
            {
                diagnostics.Add(new ClassicDiagnostic(
                    "NON_LINUX_SERIALIZED_FILE",
                    $"Serialized file is not Linux target {LinuxBuildTarget}: {relativePath}",
                    relativePath));
            }
        }
        catch (Exception error)
        {
            diagnostics.Add(new ClassicDiagnostic(
                "UNREADABLE_SERIALIZED_FILE",
                $"Serialized file could not be read: {relativePath}: {error.Message}",
                relativePath));
        }
        finally
        {
            manager.UnloadAll();
        }
    }

    private static void ValidateNumericIndices(
        IReadOnlyList<ClassicFileEntry> files,
        List<ClassicDiagnostic> diagnostics)
    {
        var numericFiles = files
            .Where(file => file.Kind == ClassicFileKind.Serialized)
            .Select(file => (
                File: file,
                Match: NumericClassicName().Match(Path.GetFileName(file.RelativePath))))
            .Where(item => item.Match.Success)
            .GroupBy(item => (
                Directory: Path.GetDirectoryName(item.File.RelativePath) ?? string.Empty,
                Prefix: item.Match.Groups[1].Value.ToLowerInvariant(),
                Index: int.Parse(item.Match.Groups[2].Value, CultureInfo.InvariantCulture)))
            .Where(group => group.Count() > 1);
        foreach (var duplicate in numericFiles)
        {
            diagnostics.Add(new ClassicDiagnostic(
                "DUPLICATE_NUMERIC_INDEX",
                $"Duplicate {duplicate.Key.Prefix} numeric index {duplicate.Key.Index}: " +
                string.Join(", ", duplicate.Select(item => item.File.RelativePath))));
        }
    }

    private static void ValidateLogicalPaths(
        IReadOnlyList<ClassicFileEntry> files,
        List<ClassicDiagnostic> diagnostics)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            if (!seen.Add(file.RelativePath))
            {
                diagnostics.Add(new ClassicDiagnostic(
                    "DUPLICATE_SOURCE_PATH",
                    $"Source contains a case-insensitive duplicate path: {file.RelativePath}",
                    file.RelativePath));
            }
        }
    }

    private static void AssociateAndValidateSidecars(
        List<ClassicFileEntry> files,
        List<ClassicDiagnostic> diagnostics)
    {
        var serialized = new Dictionary<string, ClassicFileEntry>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var file in files.Where(file => file.Kind == ClassicFileKind.Serialized))
        {
            serialized.TryAdd(file.RelativePath, file);
        }
        for (var index = 0; index < files.Count; index++)
        {
            var sidecar = files[index];
            if (sidecar.Kind != ClassicFileKind.Sidecar) continue;

            var owner = SidecarOwnerCandidates(sidecar.RelativePath)
                .FirstOrDefault(serialized.ContainsKey);
            if (owner is null)
            {
                diagnostics.Add(new ClassicDiagnostic(
                    "ORPHANED_SIDECAR",
                    $"Resource sidecar has no serialized owner: {sidecar.RelativePath}",
                    sidecar.RelativePath));
                continue;
            }
            files[index] = sidecar with { OwnerRelativePath = owner };
        }
    }

    private static IReadOnlyList<string> SidecarOwnerCandidates(string sidecar)
    {
        if (sidecar.EndsWith(".resS", StringComparison.OrdinalIgnoreCase))
        {
            return [sidecar[..^5]];
        }

        var basename = sidecar[..^9];
        return [basename, basename + ".assets"];
    }

    private static void ValidateManifestNumericAliases(
        IEnumerable<string> paths,
        List<ClassicDiagnostic> diagnostics)
    {
        var aliases = paths
            .Select(path => (
                Path: path,
                Match: NumericClassicName().Match(Path.GetFileName(path))))
            .Where(item => item.Match.Success)
            .GroupBy(item => (
                Directory: Path.GetDirectoryName(item.Path) ?? string.Empty,
                Prefix: item.Match.Groups[1].Value.ToLowerInvariant(),
                Index: int.Parse(item.Match.Groups[2].Value, CultureInfo.InvariantCulture)))
            .Where(group => group.Count() > 1);
        foreach (var alias in aliases)
        {
            diagnostics.Add(new ClassicDiagnostic(
                "DUPLICATE_MANIFEST_NUMERIC_INDEX",
                $"Manifest contains numeric aliases for {alias.Key.Prefix}{alias.Key.Index}: " +
                string.Join(", ", alias.Select(item => item.Path))));
        }
    }

    private static void ValidateAction(
        ClassicFileEntry inventoryFile,
        ClassicManifestFile manifestFile,
        List<ClassicDiagnostic> diagnostics)
    {
        var valid = manifestFile.Action switch
        {
            ClassicManifestAction.Transform =>
                inventoryFile.Kind == ClassicFileKind.Serialized,
            ClassicManifestAction.Copy =>
                inventoryFile.Kind is ClassicFileKind.Sidecar or
                    ClassicFileKind.Media or ClassicFileKind.PassThrough,
            ClassicManifestAction.Exclude => true,
            ClassicManifestAction.ReplaceAtAssembly =>
                inventoryFile.Kind == ClassicFileKind.ReplacementRequired,
            _ => false,
        };
        if (!valid)
        {
            diagnostics.Add(new ClassicDiagnostic(
                "INVALID_MANIFEST_ACTION",
                $"Action {manifestFile.Action} is invalid for {inventoryFile.Kind}: " +
                inventoryFile.RelativePath,
                inventoryFile.RelativePath));
        }
    }

    private static void ValidateManifestOwner(
        IReadOnlyDictionary<string, ClassicFileEntry> inventoryFiles,
        ClassicFileEntry inventoryFile,
        ClassicManifestFile manifestFile,
        List<ClassicDiagnostic> diagnostics)
    {
        if (inventoryFile.Kind != ClassicFileKind.Sidecar) return;
        var owner = manifestFile.OwnerRelativePath ?? inventoryFile.OwnerRelativePath;
        if (owner is null ||
            !TryNormalizeRelativePath(owner, out var normalizedOwner) ||
            !inventoryFiles.TryGetValue(normalizedOwner, out var ownerFile) ||
            ownerFile.Kind != ClassicFileKind.Serialized)
        {
            diagnostics.Add(new ClassicDiagnostic(
                "INVALID_SIDECAR_OWNER",
                $"Sidecar owner is missing or not serialized: {inventoryFile.RelativePath}",
                inventoryFile.RelativePath));
        }
    }

    [GeneratedRegex("^(level|sharedassets)([0-9]+)(?:\\.assets)?$", RegexOptions.IgnoreCase)]
    private static partial Regex NumericClassicName();

    [GeneratedRegex("^[A-Za-z]:")]
    private static partial Regex DriveQualifiedPath();

    [GeneratedRegex("^[0-9a-fA-F]{64}$")]
    private static partial Regex Sha256Text();

    private sealed class ClassicPathComparer : IComparer<string>
    {
        internal static ClassicPathComparer Instance { get; } = new();

        public int Compare(string? left, string? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return -1;
            if (right is null) return 1;

            var leftKey = SortKey(left);
            var rightKey = SortKey(right);
            var rank = leftKey.Rank.CompareTo(rightKey.Rank);
            if (rank != 0) return rank;
            var index = leftKey.Index.CompareTo(rightKey.Index);
            if (index != 0) return index;
            return StringComparer.OrdinalIgnoreCase.Compare(left, right);
        }

        private static (int Rank, int Index) SortKey(string path)
        {
            var name = Path.GetFileName(path);
            if (name.Equals("globalgamemanagers", StringComparison.OrdinalIgnoreCase))
                return (0, 0);
            if (name.Equals("globalgamemanagers.assets", StringComparison.OrdinalIgnoreCase))
                return (1, 0);
            if (name.StartsWith("resources", StringComparison.OrdinalIgnoreCase))
                return (2, 0);

            var match = NumericClassicName().Match(name);
            if (match.Success)
            {
                var rank = match.Groups[1].Value.Equals(
                    "level",
                    StringComparison.OrdinalIgnoreCase) ? 3 : 4;
                return (
                    rank,
                    int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture));
            }
            return (5, 0);
        }
    }
}
