using Mono.Cecil;
using Mono.Cecil.Cil;

namespace BundleSurgery;

// redirect-file-replace: point the game's File.Replace calls at SafeIo.
//
// Silksong commits both a save slot and its shared data by writing a temp
// beside the real file and swapping it in with File.Replace. On some devices
// that call fails every time with "IOException: Invalid argument" out of
// System.IO.FileSystem.ReplaceFile -- it wants a hard link to stand the backup
// up, and Android's FUSE-backed emulated storage has never had those. The game
// then cannot save at all after the first save of a slot, because the first
// one takes File.Move and every later one takes File.Replace.
//
// There is no way to fix that from outside the game. The launcher is not
// running while the game is, and WriteSaveSlot is compiled into the depot's
// assembly. So the call site is repointed here, while it is still IL and
// before IL2CPP turns it into C++, at a helper that tries the very same
// File.Replace first and falls back to rename only when it throws.
//
// Two call sites, both in Assembly-CSharp:
//   DesktopPlatform.WriteSaveSlot        File.Replace(tmp, userN.dat, bak + n)
//   JsonSharedData.WriteAllBytesSafe     File.Replace(tmp, shared.dat, bak, true)
//
// Failing when it finds NONE is deliberate. A rewrite that silently matches
// nothing is indistinguishable from one that worked, right up until a user
// reports the bug it was supposed to have fixed.
internal static class RedirectFileReplace
{
    const string HelperType = "SafeIo";
    const string HelperMethod = "Replace";

    public static int Run(string assemblyPath, string helperPath)
    {
        if (!File.Exists(assemblyPath))
        {
            Console.Error.WriteLine($"  ✗ no assembly at {assemblyPath}");
            return 1;
        }
        if (!File.Exists(helperPath))
        {
            Console.Error.WriteLine($"  ✗ no helper assembly at {helperPath}");
            return 1;
        }

        // Everything the target references lives beside it, staged there for
        // IL2CPP. Without this the resolver looks only in the working
        // directory and fails on the first UnityEngine type it meets.
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(assemblyPath))!);
        resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(helperPath))!);

        using var helper = AssemblyDefinition.ReadAssembly(helperPath);
        var safeIo = helper.MainModule.GetType(HelperType);
        if (safeIo is null)
        {
            Console.Error.WriteLine($"  ✗ {helperPath} has no type {HelperType}");
            return 1;
        }

        // One per arity. The game uses both overloads and they mean the same
        // thing, so both are redirected rather than normalised to one.
        var replacements = new Dictionary<int, MethodDefinition>();
        foreach (var m in safeIo.Methods)
        {
            if (m.Name != HelperMethod || !m.IsStatic) continue;
            replacements[m.Parameters.Count] = m;
        }
        if (replacements.Count == 0)
        {
            Console.Error.WriteLine($"  ✗ {HelperType} has no static {HelperMethod} overloads");
            return 1;
        }

        var readerParameters = new ReaderParameters { AssemblyResolver = resolver };
        int rewritten = 0;

        // Written beside the original and moved into place afterwards, so that
        // a failure part way through Write() cannot leave a truncated
        // Assembly-CSharp where a working one used to be. The move is outside
        // the using on purpose: Cecil holds the original open for reading
        // until it is disposed, and Windows will not let it be replaced first.
        var staging = assemblyPath + ".rewritten";

        using (var target = AssemblyDefinition.ReadAssembly(assemblyPath, readerParameters))
        {
            var module = target.MainModule;
            var imported = new Dictionary<int, MethodReference>();
            foreach (var kv in replacements) imported[kv.Key] = module.ImportReference(kv.Value);

            foreach (var type in AllTypes(module.Types))
            {
                foreach (var method in type.Methods)
                {
                    if (!method.HasBody) continue;
                    foreach (var instruction in method.Body.Instructions)
                    {
                        if (instruction.OpCode != OpCodes.Call) continue;
                        if (instruction.Operand is not MethodReference call) continue;
                        if (call.Name != HelperMethod) continue;
                        if (call.DeclaringType?.FullName != "System.IO.File") continue;
                        if (!imported.TryGetValue(call.Parameters.Count, out var target2))
                        {
                            Console.Error.WriteLine(
                                $"  ✗ no SafeIo.Replace overload for {call.Parameters.Count} arguments "
                                + $"(called from {type.FullName}.{method.Name})");
                            return 1;
                        }
                        instruction.Operand = target2;
                        rewritten++;
                        Console.WriteLine($"  → {type.FullName}.{method.Name}: File.Replace/"
                            + $"{call.Parameters.Count} -> {HelperType}.{HelperMethod}");
                    }
                }
            }

            if (rewritten == 0)
            {
                // See the header: a no-op rewrite is the failure mode that
                // hides itself, so it is an error rather than a shrug.
                Console.Error.WriteLine("  ✗ no File.Replace call sites found — the game's save path has "
                    + "changed shape and this command needs revisiting");
                return 1;
            }

            // Written beside the original and moved into place, so that a
            // failure half way through Write() cannot leave a truncated
            // Assembly-CSharp where a working one used to be.
            target.Write(staging);
        }

        File.Move(staging, assemblyPath, overwrite: true);

        Console.WriteLine($"  ✓ redirected {rewritten} File.Replace call site(s) in {Path.GetFileName(assemblyPath)}");
        return 0;
    }

    /// <summary>Types, including nested ones -- lambdas compile into those.</summary>
    static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types)
    {
        foreach (var t in types)
        {
            yield return t;
            foreach (var nested in AllTypes(t.NestedTypes)) yield return nested;
        }
    }
}
