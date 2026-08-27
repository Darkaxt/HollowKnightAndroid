using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System.Text;

namespace BundleSurgery;

// bundle-surgery: surgical rewrites of Unity asset bundles for the Silksong
// Android port. The pipeline's main consumer is `extract-vulkan-android`,
// which slices each shader down to its Vulkan blob and retargets the
// bundle's BuildTarget header from Linux to Android. All other commands
// are dev/inspection tools.
//
// usage:
//   dotnet run -- <command> [args]
// Run with no command for the full usage text.
//
// ─── Reference: Unity enum values you'll see while poking at bundles ────────
//
// GPUPlatform (in shader subprograms' platforms[]):
//   4=d3d11  9=gles3  15=glcore  18=vulkan
//
// BuildTarget (in the inner SerializedFile header's m_TargetPlatform):
//   13=Android  19=StandaloneWin64  24=StandaloneLinux64
//
// ShaderGpuProgramType (in a shader subprogram's ProgType field):
//   4=GLES3  6=GLCore32  15=DX11VertexSM40  17=DX11PixelSM40  25=SPIRV
//
// The compressed shader blob is a list of subprogram entries concatenated.
// Each entry starts with the magic `BA 75 0A 0C` (little-endian 202012090).
// A parser for it lived in ShaderBlob.cs until nothing called it any more;
// git has it if the format is ever needed again.
//
// On-device shader debugging (Adreno Vulkan):
//   adb shell setprop log.tag.Adreno-Vulkan VERBOSE
//   adb logcat | grep -iE '(vulkan|adreno|spirv|shader)'
// For frame captures, use RenderDoc on a Vulkan target.
internal static class Program
{
    // classdata.tpk sits next to the assembly, so resolve it from there rather
    // than from the working directory. Callers run this tool from wherever the
    // content happens to be -- on the device it is driven from the app's data
    // directory -- and a bare relative path silently turns every bundle into a
    // "could not find file" failure.
    static string ClassDataPath { get; } = Path.Combine(
        AppContext.BaseDirectory, "classdata.tpk");

    static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: BundleSurgery <command> [args]");
            Console.Error.WriteLine("commands:");
            Console.Error.WriteLine("  extract-vulkan-android <in.bundle> <out.bundle>  — extract Vulkan SPIR-V slice + retarget to Android (the main pipeline command)");
            Console.Error.WriteLine("  set-unity-version <file> <version>               — rewrite the Unity version stamped in a SerializedFile (e.g. strip an internal branch suffix)");
            Console.Error.WriteLine("  set-graphics-apis <globalgamemanagers> <ids>     — set BuildSettings.m_GraphicsAPIs (21=Vulkan, 11=GLES3, 17=OpenGLCore)");
            Console.Error.WriteLine("  set-build-version <globalgamemanagers> <version> — set BuildSettings.m_Version (must match the SerializedFile version)");
            Console.Error.WriteLine("  patch-catalog-path <in.bin> <out.bin> <abs-path> — repoint an Addressables catalog's content root at an absolute path");
            Console.Error.WriteLine("  redirect-file-replace <Assembly-CSharp.dll> <SilksongIo.dll> — point the game's File.Replace calls at SafeIo (fixes saving where ReplaceFile is unsupported)");
            Console.Error.WriteLine("  retarget-tree <src-dir> <dst-dir>               — extract-vulkan-android over a whole bundle tree, in parallel, resumable");
            Console.Error.WriteLine();
            Console.Error.WriteLine("inspection (diagnostic):");
            return 2;
        }

        return args[0] switch
        {
            "set-unity-version" when args.Length >= 3 => SetUnityVersion(args[1], args[2]),
            "set-graphics-apis" when args.Length >= 3 => SetGraphicsApis(args[1], args[2]),
            "set-build-version" when args.Length >= 3 => SetBuildVersion(args[1], args[2]),
            "patch-catalog-path" when args.Length >= 4 => PatchCatalogPath(args[1], args[2], args[3]),
            "redirect-file-replace" when args.Length >= 3 => RedirectFileReplace.Run(args[1], args[2]),
            "retarget-tree" when args.Length >= 3 => RetargetTree(args[1], args[2]),
            "extract-vulkan-android" when args.Length >= 3 => ExtractVulkanAndroid(args[1], args[2]),
            "shader-report" when args.Length >= 2 => ShaderReport(args[1]),
            _ => Usage(),
        };
    }

    // Retargets an entire bundle tree in one process.
    //
    // The per-bundle work is fast, but starting a .NET process and loading the
    // class database is not: measured per-bundle cost is ~1.1 s almost
    // regardless of size, so a 4 KB bundle costs as much as a 131 MB one.
    // Across the ~2000 bundles a game ships that is over half an hour of pure
    // startup, which matters a great deal when this eventually has to run on a
    // phone.
    //
    // Doing the whole tree in one process removes that overhead entirely, and
    // lets the work run in parallel across cores. Output is written to a
    // temporary file and moved into place, and existing outputs are skipped,
    // so an interrupted run resumes rather than restarting -- worth having for
    // a multi-gigabyte job.
    static int RetargetTree(string srcRoot, string dstRoot)
    {
        srcRoot = Path.GetFullPath(srcRoot);
        dstRoot = Path.GetFullPath(dstRoot);

        // In-place is the normal mode on a phone, where there is no room for a
        // second copy of a multi-gigabyte content tree.
        bool inPlace = string.Equals(srcRoot, dstRoot, StringComparison.Ordinal);

        // Recursive on purpose: a shipped Addressables tree groups content
        // into subdirectories (scenes, atlases, ...), and processing only the
        // top level silently leaves that content targeted at the wrong
        // platform, to fail at load time rather than here.
        var inputs = Directory.GetFiles(srcRoot, "*.bundle", SearchOption.AllDirectories);
        if (inputs.Length == 0)
        {
            Console.Error.WriteLine($"  ✗ no bundles under {srcRoot}");
            return 1;
        }

        Console.Error.WriteLine($"  {inputs.Length} bundle(s) → {(inPlace ? "in place" : dstRoot)}");

        int done = 0, skipped = 0, failed = 0;
        var errors = new System.Collections.Concurrent.ConcurrentBag<string>();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        Parallel.ForEach(inputs, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, input =>
        {
            var rel = Path.GetRelativePath(srcRoot, input);
            var output = inPlace ? input : Path.Combine(dstRoot, rel);
            if (!inPlace) Directory.CreateDirectory(Path.GetDirectoryName(output)!);

            // An out-of-place run resumes by skipping outputs that already
            // exist. An in-place run cannot do that, and does not need to:
            // RetargetOne is a no-op on a bundle that is already Android, so
            // re-running is safe and cheap either way.
            if (!inPlace && File.Exists(output) && new FileInfo(output).Length > 0)
            {
                Interlocked.Increment(ref skipped);
            }
            else
            {
                try
                {
                    // Each bundle gets its own AssetsManager: they hold per-file
                    // state and are not safe to share across threads.
                    if (RetargetOne(input, output)) Interlocked.Increment(ref done);
                    else Interlocked.Increment(ref skipped);
                }
                catch (Exception e)
                {
                    Interlocked.Increment(ref failed);
                    errors.Add($"{rel}: {e.Message}");
                }
            }

            int n = done + skipped + failed;
            if (n % 100 == 0)
                Console.Error.WriteLine($"  {n} / {inputs.Length}  ({sw.Elapsed.TotalSeconds:N0}s)");
        });

        sw.Stop();
        Console.Error.WriteLine($"  {done} retargeted, {skipped} already Android or present, {failed} failed  in {sw.Elapsed.TotalSeconds:N0}s");
        foreach (var e in errors.Take(10)) Console.Error.WriteLine($"    ✗ {e}");
        return failed > 0 ? 1 : 0;
    }

    // The single-bundle body of RetargetTree: strip each shader to its Vulkan
    // slice and stamp every inner serialized file as Android. Returns false if
    // the bundle needed nothing done, in which case nothing is written -- that
    // is what lets an in-place run be idempotent and safely resumable.
    //
    // Kept separate from ExtractVulkanAndroid so the batch path stays quiet:
    // printing a line per bundle across thousands of files is just noise.
    static bool RetargetOne(string inputPath, string outputPath)
    {
        const int ANDROID_BUILD_TARGET = 13;

        var manager = new AssetsManager();
        manager.LoadClassPackage(ClassDataPath);

        var bundle = manager.LoadBundleFile(inputPath, true);
        manager.LoadClassDatabaseFromPackage(bundle.file.Header.EngineVersion);
        bool changedAnything = false;

        foreach (var dirInfo in bundle.file.BlockAndDirInfo.DirectoryInfos)
        {
            if ((dirInfo.Flags & 4) == 0) continue;
            var afile = manager.LoadAssetsFileFromBundle(bundle, dirInfo.Name);
            bool anyChange = false;

            foreach (var asset in afile.file.GetAssetsOfType(AssetClassID.Shader))
            {
                var bf = manager.GetBaseField(afile, asset);
                if (bf == null) continue;
                if (StripToVulkanOnly(bf))
                {
                    asset.SetNewData(bf);
                    anyChange = true;
                }
            }

            if (afile.file.Metadata.TargetPlatform != ANDROID_BUILD_TARGET)
            {
                afile.file.Metadata.TargetPlatform = ANDROID_BUILD_TARGET;
                anyChange = true;
            }
            if (anyChange)
            {
                dirInfo.SetNewData(afile.file);
                changedAnything = true;
            }
        }

        if (!changedAnything && string.Equals(inputPath, outputPath, StringComparison.Ordinal))
        {
            manager.UnloadAll();
            return false;
        }

        var tempPath = outputPath + ".tmp";
        using (var fs = File.Create(tempPath))
        using (var writer = new AssetsFileWriter(fs))
            bundle.file.Write(writer);
        // Release the source handle before replacing it: an in-place run has
        // input and output pointing at the same file.
        manager.UnloadAll();
        if (File.Exists(outputPath)) File.Delete(outputPath);
        File.Move(tempPath, outputPath);
        return true;
    }

    // Repoints an Addressables catalog's content root.
    //
    // A shipped catalog does not store absolute paths. Bundle locations are
    // written as a token plus a relative path,
    //
    //   {UnityEngine.AddressableAssets.Addressables.RuntimePath}/<platform>/x.bundle
    //
    // and the token is resolved at load time. The token is stored once and
    // referenced by every location, so replacing that single string moves the
    // whole content set at once.
    //
    // Strings in the binary catalog are length-prefixed rather than
    // terminated:
    //
    //   11 00 00 00  "StandaloneLinux64"   38 00 00 00  "{UnityEngine...}"
    //
    // so a replacement of exactly the same byte length keeps the prefix valid
    // and leaves every subsequent offset untouched — no re-serialization, and
    // nothing else in the file has to be understood.
    //
    // The path is padded to length with trailing slashes, which POSIX
    // collapses, so "/data/.../aa///////" + "/x.bundle" opens the same file as
    // "/data/.../aa/x.bundle".
    static int PatchCatalogPath(string inPath, string outPath, string newRoot)
    {
        const string Token = "{UnityEngine.AddressableAssets.Addressables.RuntimePath}";

        var bytes = File.ReadAllBytes(inPath);
        var tokenBytes = Encoding.ASCII.GetBytes(Token);

        var hits = new List<int>();
        for (int i = 0; i + tokenBytes.Length <= bytes.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < tokenBytes.Length; j++)
                if (bytes[i + j] != tokenBytes[j]) { match = false; break; }
            if (match) hits.Add(i);
        }

        if (hits.Count == 0)
        {
            Console.Error.WriteLine($"  ✗ token not found in {inPath}");
            Console.Error.WriteLine("    (already patched, or not an Addressables binary catalog)");
            return 1;
        }

        newRoot = newRoot.TrimEnd('/');
        var rootBytes = Encoding.ASCII.GetBytes(newRoot);
        if (rootBytes.Length > tokenBytes.Length)
        {
            Console.Error.WriteLine($"  ✗ path is {rootBytes.Length} bytes, only {tokenBytes.Length} available");
            Console.Error.WriteLine("    a longer path would shift every following offset and require a full rewrite");
            return 1;
        }

        // Pad with '/' rather than spaces or NULs: the padding sits between
        // the root and the catalog's own "/<platform>/x.bundle" suffix, where
        // extra separators are simply collapsed.
        var replacement = new byte[tokenBytes.Length];
        Array.Copy(rootBytes, replacement, rootBytes.Length);
        for (int i = rootBytes.Length; i < replacement.Length; i++)
            replacement[i] = (byte)'/';

        foreach (var at in hits)
        {
            // The four bytes before the string are its length. Verifying it
            // matches turns a silent corruption into a loud failure if the
            // catalog format ever changes.
            if (at >= 4)
            {
                int declared = BitConverter.ToInt32(bytes, at - 4);
                if (declared != tokenBytes.Length)
                {
                    Console.Error.WriteLine($"  ✗ at 0x{at:x}: length prefix is {declared}, expected {tokenBytes.Length}");
                    Console.Error.WriteLine("    the catalog string encoding is not what this command assumes");
                    return 1;
                }
            }
            Array.Copy(replacement, 0, bytes, at, replacement.Length);
        }

        File.WriteAllBytes(outPath, bytes);
        Console.Error.WriteLine($"  ✓ content root → {newRoot}");
        Console.Error.WriteLine($"    {hits.Count} occurrence(s), {rootBytes.Length} bytes + {tokenBytes.Length - rootBytes.Length} padding, file size unchanged ({bytes.Length} bytes)");
        return 0;
    }




    // Rewrites the Unity version string stamped into a SerializedFile's metadata.
    //
    // A game built on an internal Unity branch stamps a version like
    // "6000.0.50f1-uum-100966-branch1" into every serialized file it writes.
    // A stock Unity player of the same numeric version refuses to load that
    // data outright:
    //
    //   Invalid serialized file version. File: ".../globalgamemanagers".
    //   Expected version: 6000.0.50f1. Actual version: 6000.0.50f1-uum-100966-branch1.
    //
    // The version is a NUL-terminated string sitting near the start of the
    // metadata, so shortening it shifts every following field and every object
    // offset. It cannot be patched in place — the file has to be re-serialized,
    // which is what AssetsFile.Write does.
    static int SetUnityVersion(string inputPath, string newVersion)
    {
        var manager = new AssetsManager();
        manager.LoadClassPackage(ClassDataPath);

        // Don't infer the container type from the extension: a player image
        // has SerializedFiles named `globalgamemanagers` and `level0` with no
        // extension at all. UnityFS bundles are identified by their magic.
        bool isBundle;
        using (var probe = File.OpenRead(inputPath))
        {
            var magic = new byte[7];
            isBundle = probe.Read(magic, 0, 7) == 7
                && System.Text.Encoding.ASCII.GetString(magic) == "UnityFS";
        }

        var tempPath = inputPath + ".tmp";

        if (isBundle)
        {
            var bundle = manager.LoadBundleFile(inputPath, true);
            manager.LoadClassDatabaseFromPackage(bundle.file.Header.EngineVersion);
            foreach (var dirInfo in bundle.file.BlockAndDirInfo.DirectoryInfos)
            {
                if ((dirInfo.Flags & 4) == 0) continue;
                var inner = manager.LoadAssetsFileFromBundle(bundle, dirInfo.Name);
                inner.file.Metadata.UnityVersion = newVersion;
                dirInfo.SetNewData(inner.file);
            }
            bundle.file.Header.EngineVersion = newVersion;
            using (var fs = File.Create(tempPath))
            using (var writer = new AssetsFileWriter(fs))
                bundle.file.Write(writer);
        }
        else
        {
            var afile = manager.LoadAssetsFile(inputPath);
            manager.LoadClassDatabaseFromPackage(afile.file.Metadata.UnityVersion);
            var old = afile.file.Metadata.UnityVersion;
            if (old == newVersion)
            {
                Console.Error.WriteLine($"  {Path.GetFileName(inputPath)}: already {newVersion}");
                return 0;
            }
            afile.file.Metadata.UnityVersion = newVersion;
            using (var fs = File.Create(tempPath))
            using (var writer = new AssetsFileWriter(fs))
                afile.file.Write(writer);
            Console.Error.WriteLine($"  {Path.GetFileName(inputPath)}: '{old}' -> '{newVersion}'");
        }

        manager.UnloadAll();
        File.Delete(inputPath);
        File.Move(tempPath, inputPath);
        return 0;
    }

    // ── PlayerSettings surgery ───────────────────────────────────────────────
    //
    // A desktop build's PlayerSettings lists graphics APIs only for the
    // platforms it was built for. Handing that data to an Android player
    // leaves the engine with no usable API for the running platform and it
    // aborts at startup with "Unable to initialize the Unity Engine Graphics
    // API" — even though the Adreno driver loaded fine. The fix is to give
    // m_BuildTargetGraphicsAPIs an entry for the Android build target.

    // Locates the single PlayerSettings asset (ClassID 129) in a serialized file.
    static (AssetsManager manager, AssetsFileInstance afile, AssetFileInfo info) LoadPlayerSettings(string path)
    {
        var manager = new AssetsManager();
        manager.LoadClassPackage(ClassDataPath);
        var afile = manager.LoadAssetsFile(path);
        manager.LoadClassDatabaseFromPackage(afile.file.Metadata.UnityVersion);
        var info = afile.file.GetAssetsOfType(AssetClassID.PlayerSettings).FirstOrDefault()
            ?? throw new InvalidOperationException($"no PlayerSettings asset in {path}");
        return (manager, afile, info);
    }





    // Rewrites BuildSettings for a different runtime platform.
    //
    // A *built* player does not carry the editor's per-platform
    // m_BuildTargetGraphicsAPIs table — Unity resolves it at build time and
    // stores the result as BuildSettings.m_GraphicsAPIs. A Linux build leaves
    // [17 OpenGLCore, 21 Vulkan] there; on Android 17 does not exist, and the
    // player aborts at startup with "Unable to initialize the Unity Engine
    // Graphics API" even though the Adreno driver loaded. Narrowing the list
    // to [21] gives the engine the one API Android actually provides.
    //
    // GraphicsDeviceType ids: 8=GLES2, 11=GLES3, 17=OpenGLCore, 21=Vulkan.
    static int SetGraphicsApis(string path, string idsCsv)
    {
        var ids = idsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => int.Parse(s.Trim())).ToArray();

        var (manager, afile, info) = LoadAsset(path, (int)AssetClassID.BuildSettings);
        var bf = manager.GetBaseField(afile, info);
        var arr = bf["m_GraphicsAPIs"]["Array"];

        var before = arr.Children.Select(c => c.AsInt).ToArray();
        arr.Children.Clear();
        foreach (var id in ids)
        {
            var el = ValueBuilder.DefaultValueFieldFromArrayTemplate(arr);
            el.AsInt = id;
            arr.Children.Add(el);
        }

        info.SetNewData(bf);
        WriteInPlace(manager, afile, path);
        Console.Error.WriteLine($"  m_GraphicsAPIs: [{string.Join(", ", before)}] -> [{string.Join(", ", ids)}]");
        return 0;
    }

    // BuildSettings carries its own copy of the engine version, separate from
    // the SerializedFile metadata that set-unity-version rewrites. Both have to
    // agree or the player rejects the data.
    static int SetBuildVersion(string path, string newVersion)
    {
        var (manager, afile, info) = LoadAsset(path, (int)AssetClassID.BuildSettings);
        var bf = manager.GetBaseField(afile, info);
        var old = bf["m_Version"].AsString;
        bf["m_Version"].AsString = newVersion;
        info.SetNewData(bf);
        WriteInPlace(manager, afile, path);
        Console.Error.WriteLine($"  BuildSettings.m_Version: '{old}' -> '{newVersion}'");
        return 0;
    }

    static (AssetsManager manager, AssetsFileInstance afile, AssetFileInfo info) LoadAsset(string path, int classId)
    {
        var manager = new AssetsManager();
        manager.LoadClassPackage(ClassDataPath);
        var afile = manager.LoadAssetsFile(path);
        manager.LoadClassDatabaseFromPackage(afile.file.Metadata.UnityVersion);
        var info = afile.file.GetAssetsOfType((AssetClassID)classId).FirstOrDefault()
            ?? throw new InvalidOperationException($"no asset with classId {classId} in {path}");
        return (manager, afile, info);
    }

    static void WriteInPlace(AssetsManager manager, AssetsFileInstance afile, string path)
    {
        var tempPath = path + ".tmp";
        using (var fs = File.Create(tempPath))
        using (var writer = new AssetsFileWriter(fs))
            afile.file.Write(writer);
        manager.UnloadAll();
        File.Delete(path);
        File.Move(tempPath, path);
    }



    static int ExtractVulkanAndroid(string inputPath, string outputPath)
    {
        const int ANDROID_BUILD_TARGET = 13;
        const int LINUX64_BUILD_TARGET = 24;

        var manager = new AssetsManager();
        manager.LoadClassPackage(ClassDataPath);

        bool isBundle = !inputPath.EndsWith(".assets", StringComparison.OrdinalIgnoreCase);
        if (!isBundle)
        {
            // .assets file path — process the single file.
            var afile = manager.LoadAssetsFile(inputPath);
            manager.LoadClassDatabaseFromPackage(afile.file.Metadata.UnityVersion);
            int shadersProcessed = 0, shadersSkipped = 0;
            foreach (var asset in afile.file.GetAssetsOfType(AssetClassID.Shader))
            {
                var bf = manager.GetBaseField(afile, asset);
                if (bf == null) continue;
                if (StripToVulkanOnly(bf))
                {
                    asset.SetNewData(bf);
                    shadersProcessed++;
                }
                else shadersSkipped++;
            }
            // Update Android BuildTarget
            afile.file.Metadata.TargetPlatform = ANDROID_BUILD_TARGET;
            using var fs = File.Create(outputPath);
            using var writer = new AssetsFileWriter(fs);
            afile.file.Write(writer);
            Console.Error.WriteLine($"  {Path.GetFileName(inputPath)}: {shadersProcessed} stripped, {shadersSkipped} skipped, BuildTarget→Android");
            return 0;
        }

        var bundle = manager.LoadBundleFile(inputPath, true);
        manager.LoadClassDatabaseFromPackage(bundle.file.Header.EngineVersion);
        int totalShaders = 0, totalSkipped = 0;
        var modifiedAfiles = new HashSet<AssetsFileInstance>();
        foreach (var dirInfo in bundle.file.BlockAndDirInfo.DirectoryInfos)
        {
            if ((dirInfo.Flags & 4) == 0) continue;
            var afile = manager.LoadAssetsFileFromBundle(bundle, dirInfo.Name);
            bool anyChange = false;
            foreach (var asset in afile.file.GetAssetsOfType(AssetClassID.Shader))
            {
                var bf = manager.GetBaseField(afile, asset);
                if (bf == null) continue;
                if (StripToVulkanOnly(bf))
                {
                    asset.SetNewData(bf);
                    totalShaders++;
                    anyChange = true;
                }
                else totalSkipped++;
            }
            // Update BuildTarget Linux→Android on every inner serialized file.
            if (afile.file.Metadata.TargetPlatform == LINUX64_BUILD_TARGET ||
                afile.file.Metadata.TargetPlatform != ANDROID_BUILD_TARGET)
            {
                afile.file.Metadata.TargetPlatform = ANDROID_BUILD_TARGET;
                anyChange = true;
            }
            if (anyChange)
            {
                dirInfo.SetNewData(afile.file);
                modifiedAfiles.Add(afile);
            }
        }

        Console.Error.WriteLine($"  {Path.GetFileName(inputPath)}: {totalShaders} stripped, {totalSkipped} skipped");

        // Always write — even if no shaders changed, we want the BuildTarget bump.
        var tempPath = outputPath + ".tmp";
        using (var fs = File.Create(tempPath))
        using (var writer = new AssetsFileWriter(fs))
            bundle.file.Write(writer);
        if (File.Exists(outputPath)) File.Delete(outputPath);
        File.Move(tempPath, outputPath);
        return 0;
    }

    // Mutate the shader's blob arrays to keep only the Vulkan (platform 18)
    // slice. Returns false if the shader has no Vulkan slice (caller should
    // pass-through the original).
    static bool StripToVulkanOnly(AssetTypeValueField bf)
    {
        var platformsArr = bf["platforms.Array"];
        if (platformsArr.AsArray.size == 0) return false;

        // Find the slot whose value is 18 (vulkan).
        int vulkanIdx = -1;
        for (int i = 0; i < platformsArr.AsArray.size; i++)
        {
            if (platformsArr[i].AsInt == 18) { vulkanIdx = i; break; }
        }
        if (vulkanIdx < 0) return false;

        // Read the per-platform arrays. Each array's element is either a
        // direct uint or a wrapper (`.Array[0]`) depending on the asset type.
        uint ReadVal(AssetTypeValueField outer, int idx)
        {
            var inner = outer[idx];
            if (inner.Children.Count > 0 && inner.Children[0].FieldName == "Array")
                return inner["Array"][0].AsUInt;
            return inner.AsUInt;
        }
        void SetVal(AssetTypeValueField outer, int idx, uint v)
        {
            var inner = outer[idx];
            if (inner.Children.Count > 0 && inner.Children[0].FieldName == "Array")
                inner["Array"][0].AsUInt = v;
            else
                inner.AsUInt = v;
        }

        var offsetsArr = bf["offsets.Array"];
        var compressedLengthsArr = bf["compressedLengths.Array"];
        var decompressedLengthsArr = bf["decompressedLengths.Array"];

        uint vulkanOffset = ReadVal(offsetsArr, vulkanIdx);
        uint vulkanCompressedLen = ReadVal(compressedLengthsArr, vulkanIdx);
        uint vulkanDecompressedLen = ReadVal(decompressedLengthsArr, vulkanIdx);

        // Extract the Vulkan slice from the shared compressedBlob.
        var compressedBlob = bf["compressedBlob.Array"].AsByteArray;
        var vulkanSlice = new byte[vulkanCompressedLen];
        Array.Copy(compressedBlob, (int)vulkanOffset, vulkanSlice, 0, (int)vulkanCompressedLen);

        // Reduce platforms[] to a single element holding 18, and rewrite the
        // per-platform arrays accordingly. AssetTypeValueField doesn't expose
        // a clean array-truncate, but moving the desired element to slot[0]
        // and shortening size = 1 via array AsArray.size works for primitive
        // arrays.
        if (vulkanIdx != 0)
        {
            // Copy the values down to slot[0].
            platformsArr[0].AsInt = platformsArr[vulkanIdx].AsInt;
            SetVal(offsetsArr, 0, ReadVal(offsetsArr, vulkanIdx));
            SetVal(compressedLengthsArr, 0, ReadVal(compressedLengthsArr, vulkanIdx));
            SetVal(decompressedLengthsArr, 0, ReadVal(decompressedLengthsArr, vulkanIdx));
        }
        SetVal(offsetsArr, 0, 0); // The slice starts at byte 0 of the new blob.
        // Resize down to 1 element. AssetTypeArrayInformation.size works for
        // primitive int/uint arrays — the writer truncates to that size.
        var pa = platformsArr.AsArray; pa.size = 1; platformsArr.AsArray = pa;
        var oa = offsetsArr.AsArray; oa.size = 1; offsetsArr.AsArray = oa;
        var ca = compressedLengthsArr.AsArray; ca.size = 1; compressedLengthsArr.AsArray = ca;
        var da = decompressedLengthsArr.AsArray; da.size = 1; decompressedLengthsArr.AsArray = da;
        // Also truncate the Children list so the serializer doesn't try to
        // write the stale extra elements.
        platformsArr.Children = new List<AssetTypeValueField> { platformsArr.Children[0] };
        offsetsArr.Children = new List<AssetTypeValueField> { offsetsArr.Children[0] };
        compressedLengthsArr.Children = new List<AssetTypeValueField> { compressedLengthsArr.Children[0] };
        decompressedLengthsArr.Children = new List<AssetTypeValueField> { decompressedLengthsArr.Children[0] };

        bf["compressedBlob.Array"].AsByteArray = vulkanSlice;
        return true;
    }



    // Subset of Unity's internal ShaderGpuProgramType enum (originally
    // observed in USCSandbox's source; the enum itself is part of Unity's
    // ShaderCompilerData and isn't exposed through public APIs).
    enum ShaderGpuProgramTypeId
    {
        Unknown = 0,
        GLES31AEP = 2, GLES31 = 3, GLES3 = 4, GLES = 5,
        GLCore32 = 6, GLCore41 = 7, GLCore43 = 8,
        DX11VertexSM40 = 15, DX11VertexSM50 = 16,
        DX11PixelSM40 = 17, DX11PixelSM50 = 18,
        MetalVS = 23, MetalFS = 24, SPIRV = 25,
    }


    /// <summary>
    /// Reports how each shader's compressed blob is divided up.
    ///
    /// The question it exists to answer: does a platform ever have more than
    /// ONE entry in offsets/compressedLengths? Unity's newer shader format
    /// nests those arrays per platform -- offsets[platform][chunk] -- and
    /// StripToVulkanOnly only ever read and rewrote chunk 0. A shader with
    /// more than one chunk therefore kept stale offsets pointing past the end
    /// of the blob it was given, which is not a load failure and not a magenta
    /// error shader: it is a variant that silently renders wrong.
    /// </summary>
    static int ShaderReport(string path)
    {
        var manager = new AssetsManager();
        manager.LoadClassPackage(ClassDataPath);

        var bundle = manager.LoadBundleFile(path, true);
        manager.LoadClassDatabaseFromPackage(bundle.file.Header.EngineVersion);

        int shaders = 0, multi = 0, noVulkan = 0;
        var mixes = new Dictionary<string, int>();
        foreach (var dirInfo in bundle.file.BlockAndDirInfo.DirectoryInfos)
        {
            if ((dirInfo.Flags & 4) == 0) continue;
            var afile = manager.LoadAssetsFileFromBundle(bundle, dirInfo.Name);
            foreach (var asset in afile.file.GetAssetsOfType(AssetClassID.Shader))
            {
                var bf = manager.GetBaseField(afile, asset);
                if (bf == null) continue;
                var platforms = bf["platforms.Array"];
                if (platforms == null || platforms.AsArray.size == 0) continue;
                shaders++;

                var offsets = bf["offsets.Array"];
                var name = bf["m_ParsedForm"]?["m_Name"]?.AsString ?? "?";

                var counts = new List<string>();
                var platformIds = new List<int>();
                bool any = false;
                bool hasVulkan = false;
                for (int i = 0; i < platforms.AsArray.size; i++)
                {
                    int id = platforms[i].AsInt;
                    platformIds.Add(id);
                    if (id == 18) hasVulkan = true;
                    var inner = offsets[i];
                    int n = (inner.Children.Count > 0 && inner.Children[0].FieldName == "Array")
                        ? inner["Array"].AsArray.size
                        : 1;
                    counts.Add($"{id}:{n}");
                    if (n > 1) any = true;
                }
                if (any)
                {
                    multi++;
                    Console.WriteLine($"  MULTI     {name}  [{string.Join(" ", counts)}]");
                }
                if (!hasVulkan)
                {
                    noVulkan++;
                    Console.WriteLine($"  NO-VULKAN {name}  [{string.Join(" ", counts)}]");
                }

                // The platform set, tallied rather than printed per shader:
                // the question is what a Linux build ships, and 122 identical
                // lines is a worse answer than one line saying "all of them".
                var key = string.Join("+", platformIds);
                mixes.TryGetValue(key, out int seen);
                mixes[key] = seen + 1;
            }
        }
        manager.UnloadAll();
        foreach (var kv in mixes)
            Console.WriteLine($"  platforms [{kv.Key}]: {kv.Value} shader(s)");
        Console.WriteLine($"{shaders} shader(s), {multi} with more than one chunk, {noVulkan} with no Vulkan slice");
        return 0;
    }

    static int Usage()
    {
        Console.Error.WriteLine("unknown command. Run with no arguments for usage.");
        return 2;
    }





    // Decompress a specific platform's slice of the shared compressedBlob.
    // platforms[N] / offsets[N] / compressedLengths[N] / decompressedLengths[N]
    // describe each per-platform slice. Linux bundles ship platforms [15,18]
    // (GLCore + Vulkan); platformIndex 1 reads the Vulkan slice.
    static byte[] DecompressPlatformBlob(AssetTypeValueField bf, byte[] compressedBlob, int platformIndex)
    {
        var offsetsArr = bf["offsets.Array"];
        var compressedLengthsArr = bf["compressedLengths.Array"];
        var decompressedLengthsArr = bf["decompressedLengths.Array"];
        uint ReadArrayValue(AssetTypeValueField outer, int idx)
        {
            var inner = outer[idx];
            if (inner.Children.Count > 0 && inner.Children[0].FieldName == "Array")
                return inner["Array"][0].AsUInt;
            return inner.AsUInt;
        }
        uint offset = ReadArrayValue(offsetsArr, platformIndex);
        uint compressedLen = ReadArrayValue(compressedLengthsArr, platformIndex);
        uint decompressedLen = ReadArrayValue(decompressedLengthsArr, platformIndex);
        var slice = new byte[compressedLen];
        Array.Copy(compressedBlob, (int)offset, slice, 0, (int)compressedLen);
        var decompressed = new byte[decompressedLen];
        using (var lz4 = new AssetsTools.NET.Extra.Decompressors.LZ4.Lz4DecoderStream(new MemoryStream(slice)))
            lz4.Read(decompressed, 0, (int)decompressedLen);
        return decompressed;
    }




}
