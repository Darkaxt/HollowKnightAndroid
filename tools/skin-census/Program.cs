using AssetsTools.NET;
using AssetsTools.NET.Extra;

// Dev-only census of every tk2dSpriteCollectionData in a Silksong depot.
// Usage: SkinCensus <bundle root> <Managed dir> <output tsv>
// Streams one bundle at a time and unloads it before the next; nothing is written
// back to the depot.
static class Program
{
    record struct Ref(string Cab, long PathId);
    sealed record Collection(string Name, string Bundle, string Cab, List<Ref> Materials, List<Ref> Textures);
    sealed record Texture(string Name, int Width, int Height, int Format, string Bundle);

    static int Main(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("usage: SkinCensus <bundle root> <Managed dir> <output tsv>");
            return 2;
        }
        string root = Path.GetFullPath(args[0]), managed = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]);
        var bundles = Directory.EnumerateFiles(root, "*.bundle", SearchOption.AllDirectories)
            .OrderBy(x => x, StringComparer.Ordinal).ToList();
        var manager = new AssetsManager();
        manager.LoadClassPackage(Path.Combine(AppContext.BaseDirectory, "classdata.tpk"));
        var generator = new MonoCecilTempGenerator(managed);
        string loadedVersion = null, bundleVersion = null;

        // The script asset every collection MonoBehaviour points at.
        Ref? script = null; string assembly = null, nameSpace = null;
        foreach (var path in bundles.Where(x => Path.GetFileName(x).Contains("_monoscripts", StringComparison.Ordinal)))
        {
            Visit(path, (afile, cab) =>
            {
                foreach (var info in afile.file.GetAssetsOfType(AssetClassID.MonoScript))
                {
                    var field = manager.GetBaseField(afile, info);
                    if (field["m_ClassName"].AsString != "tk2dSpriteCollectionData") continue;
                    if (script != null) throw new InvalidDataException("Two tk2dSpriteCollectionData scripts.");
                    script = new Ref(cab, info.PathId);
                    assembly = field["m_AssemblyName"].AsString;
                    nameSpace = field["m_Namespace"].AsString;
                }
            });
        }
        if (script == null) throw new InvalidDataException("tk2dSpriteCollectionData MonoScript not found.");
        Console.Error.WriteLine($"script {script} {assembly} '{nameSpace}'");

        var collections = new List<Collection>();
        var materials = new Dictionary<Ref, Ref>();
        var textures = new Dictionary<Ref, Texture>();
        int index = 0;
        string only = Environment.GetEnvironmentVariable("CENSUS_ONLY");
        AssetTypeTemplateField collectionTemplate = null;
        foreach (var path in bundles)
        {
            if (!string.IsNullOrEmpty(only) && !path.Contains(only, StringComparison.Ordinal)) continue;
            if (++index % 50 == 0)
                Console.Error.WriteLine($"{index}/{bundles.Count} collections={collections.Count} " +
                    $"managed={GC.GetTotalMemory(false) >> 20}MiB ws={Environment.WorkingSet >> 20}MiB last={Path.GetFileName(path)}");
            string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            Visit(path, (afile, cab) =>
            {
                var file = afile.file;
                foreach (var info in file.GetAssetsOfType(AssetClassID.MonoBehaviour))
                {
                    int scriptIndex = file.GetScriptIndex(info);
                    if (scriptIndex < 0 || scriptIndex >= file.Metadata.ScriptTypes.Count) continue;
                    var pointer = file.Metadata.ScriptTypes[scriptIndex];
                    if (Resolve(file, cab, pointer.FileId, pointer.PathId) != script.Value) continue;
                    // Some bundles keep their type tree; only stripped ones need the Managed layout.
                    var template = manager.GetTemplateBaseField(afile, info, AssetReadFlags.SkipMonoBehaviourFields);
                    if (template.Children.Count <= 4)
                    {
                        if (collectionTemplate == null)
                        {
                            collectionTemplate = generator.GetTemplateField(template, assembly, nameSpace,
                                "tk2dSpriteCollectionData", new UnityVersion(bundleVersion));
                            if (Environment.GetEnvironmentVariable("CENSUS_DUMP") == "1") Dump(collectionTemplate, 0);
                        }
                        template = collectionTemplate;
                    }
                    long start = info.GetAbsoluteByteOffset(file);
                    file.Reader.Position = start;
                    var field = template.MakeValue(file.Reader);
                    if (file.Reader.Position != start + info.ByteSize)
                        throw new InvalidDataException($"{relative} {info.PathId}: template read {file.Reader.Position - start} of {info.ByteSize} bytes");
                    collections.Add(new Collection(field["spriteCollectionName"].AsString, relative, cab,
                        Pointers(file, cab, field["materials"]), Pointers(file, cab, field["textures"])));
                }
                foreach (var info in file.GetAssetsOfType(AssetClassID.Material))
                {
                    var field = manager.GetBaseField(afile, info);
                    foreach (var pair in field["m_SavedProperties"]["m_TexEnvs"]["Array"].Children)
                    {
                        if (pair["first"].AsString != "_MainTex") continue;
                        var texture = pair["second"]["m_Texture"];
                        materials[new Ref(cab, info.PathId)] = Resolve(file, cab, texture["m_FileID"].AsInt, texture["m_PathID"].AsLong);
                        break;
                    }
                }
                foreach (var info in file.GetAssetsOfType(AssetClassID.Texture2D))
                {
                    // Header fields only: never materialise inline image data.
                    var template = manager.GetTemplateBaseField(afile, info);
                    int keep = template.Children.FindIndex(x => x.Name == "m_TextureFormat");
                    if (keep < 0) throw new InvalidDataException("Texture2D template has no m_TextureFormat.");
                    var header = new AssetTypeTemplateField { Name = template.Name, Type = template.Type,
                        Children = template.Children.Take(keep + 1).ToList() };
                    file.Reader.Position = info.GetAbsoluteByteOffset(file);
                    var field = header.MakeValue(file.Reader);
                    textures[new Ref(cab, info.PathId)] = new Texture(field["m_Name"].AsString, field["m_Width"].AsInt,
                        field["m_Height"].AsInt, field["m_TextureFormat"].AsInt, relative);
                }
            });
        }

        using var writer = new StreamWriter(output, false, new System.Text.UTF8Encoding(false)) { NewLine = "\n" };
        writer.WriteLine("collection\tbundle\tmaterials\ttextures\tpage\ttexture\twidth\theight\tformat\tshared");
        foreach (var collection in collections.OrderBy(x => x.Name, StringComparer.Ordinal).ThenBy(x => x.Bundle, StringComparer.Ordinal))
        {
            var pages = collection.Materials.Select(material =>
                materials.TryGetValue(material, out var texture) && textures.TryGetValue(texture, out var info)
                    ? (texture, info) : (texture: default(Ref), info: (Texture)null)).ToList();
            bool shared = pages.Count > 1 && pages.All(x => x.info != null && x.texture == pages[0].texture);
            for (int page = 0; page < pages.Count; page++)
            {
                var info = pages[page].info;
                writer.WriteLine(string.Join('\t', collection.Name, collection.Bundle, collection.Materials.Count,
                    collection.Textures.Count, page, info?.Name ?? "?", info?.Width ?? 0, info?.Height ?? 0,
                    info?.Format ?? -1, shared ? "shared" : ""));
            }
            if (pages.Count == 0)
                writer.WriteLine(string.Join('\t', collection.Name, collection.Bundle, 0, collection.Textures.Count, -1, "?", 0, 0, -1, ""));
        }
        Console.Error.WriteLine($"done: {collections.Count} collections, {collections.Select(x => x.Name).Distinct().Count()} names");
        return 0;

        void Visit(string path, Action<AssetsFileInstance, string> visit)
        {
            var bundle = manager.LoadBundleFile(path, true);
            try
            {
                string version = bundle.file.Header.EngineVersion;
                bundleVersion = version;
                if (version != loadedVersion) { manager.LoadClassDatabaseFromPackage(version); loadedVersion = version; }
                var infos = bundle.file.BlockAndDirInfo.DirectoryInfos;
                for (int i = 0; i < infos.Count; i++)
                {
                    if ((infos[i].Flags & 4) == 0) continue;
                    visit(manager.LoadAssetsFileFromBundle(bundle, i, false), infos[i].Name);
                }
            }
            finally
            {
                manager.UnloadAllAssetsFiles(true);
                manager.UnloadAllBundleFiles();
            }
        }
    }

    static void Dump(AssetTypeTemplateField field, int depth)
    {
        Console.Error.WriteLine(new string(' ', depth * 2) + field.Type + " " + field.Name + (field.IsArray ? " []" : "") +
            (field.IsAligned ? " align" : ""));
        if (depth < 6) foreach (var child in field.Children) Dump(child, depth + 1);
    }

    static List<Ref> Pointers(AssetsFile file, string cab, AssetTypeValueField array) =>
        array["Array"].Children.Select(x => Resolve(file, cab, x["m_FileID"].AsInt, x["m_PathID"].AsLong)).ToList();

    static Ref Resolve(AssetsFile file, string cab, int fileId, long pathId)
    {
        if (fileId == 0) return new Ref(cab, pathId);
        var external = file.Metadata.Externals[fileId - 1].PathName;
        return new Ref(Path.GetFileName(external.Replace('\\', '/')), pathId);
    }
}
