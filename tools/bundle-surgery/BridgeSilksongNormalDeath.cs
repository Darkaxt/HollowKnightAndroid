using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;
using System.Text;

namespace BundleSurgery;

internal static class BridgeSilksongNormalDeath
{
    internal const string PINNED_GAME_VERSION = "1.0.29980";
    internal const string PINNED_ASSEMBLY_SHA256 = "1af095416b89f73993058f9cbac3a93959d928314b735cc4acbca7bf1a952d2d";
    const string StateMachineName = "<Die>d__1101";
    const string OccurrenceName = "__dsNormalDeathOccurrence";
    const string HeroName = "__dsNormalDeathHero";
    const string ManagerName = "__dsNormalDeathManager";
    const string HelperName = "DsRecordNormalDeath";
    const string PinnedHelperSha256 = "ac2d03869b623aba238b2bb58867e3d2a906f194933e47a197b0c09eafa3ef3e";

    sealed class DieShape
    {
        public required ModuleDefinition Module;
        public required TypeDefinition Hero;
        public required TypeDefinition Manager;
        public required TypeDefinition StateMachine;
        public required MethodDefinition MoveNext;
        public required FieldDefinition HeroManager;
        public required Instruction NativeStart;
    }

    public static int Run(string inputPath, string outputPath)
    {
        try
        {
            RequireDistinctPaths(inputPath, outputPath);
            if (!File.Exists(inputPath)) throw new InvalidOperationException("managed assembly input is missing");
            if (File.Exists(outputPath)) throw new InvalidOperationException("refusing to replace an existing output");
            string inputHash = FileSha256(inputPath);
            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(inputPath))!);
            var reader = new ReaderParameters { AssemblyResolver = resolver };
            using var assembly = AssemblyDefinition.ReadAssembly(inputPath, reader);
            var shape = RequireExactDieShape(assembly.MainModule);
            if (AlreadyRewritten(shape))
            {
                File.Copy(inputPath, outputPath, false);
                Console.WriteLine("  exact Silksong normal-death bridge already present; copied unchanged");
                return 0;
            }
            if (HasAnyBridgeMember(shape.Manager))
                throw new InvalidOperationException("partial or foreign Silksong normal-death bridge is present");
            if (!string.Equals(inputHash, PINNED_ASSEMBLY_SHA256, StringComparison.Ordinal))
                throw new InvalidOperationException($"Assembly-CSharp.dll differs from pinned {PINNED_GAME_VERSION}: {inputHash}");

            var helper = InjectBridge(shape);
            RewriteMoveNext(shape, helper);
            if (!AlreadyRewritten(shape))
                throw new InvalidOperationException("in-memory Silksong normal-death bridge did not verify");

            string staging = outputPath + ".rewritten";
            try
            {
                assembly.Write(staging);
                using (var persisted = AssemblyDefinition.ReadAssembly(staging, reader))
                {
                    if (!AlreadyRewritten(RequireExactDieShape(persisted.MainModule)))
                        throw new InvalidOperationException("persisted Silksong normal-death bridge did not verify");
                }
                File.Move(staging, outputPath, false);
            }
            finally
            {
                if (File.Exists(staging)) File.Delete(staging);
            }
            Console.WriteLine("  injected exact HeroController.Die normal-death occurrence bridge");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("  Silksong normal-death rewrite failed closed: " + error.Message);
            return 1;
        }
    }

    static void RequireDistinctPaths(string inputPath, string outputPath)
    {
        if (string.Equals(Path.GetFullPath(inputPath), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("input and output must be distinct; original assemblies are read-only");
    }

    static DieShape RequireExactDieShape(ModuleDefinition module)
    {
        var hero = module.Types.SingleOrDefault(x => x.FullName == "HeroController")
            ?? throw new InvalidOperationException("exact HeroController type is missing");
        var manager = module.Types.SingleOrDefault(x => x.FullName == "GameManager")
            ?? throw new InvalidOperationException("exact GameManager type is missing");
        var die = hero.Methods.SingleOrDefault(x => x.Name == "Die" && x.Parameters.Count == 2 &&
            x.Parameters.All(p => p.ParameterType.MetadataType == MetadataType.Boolean) &&
            x.ReturnType.FullName == "System.Collections.IEnumerator")
            ?? throw new InvalidOperationException("exact HeroController.Die(bool,bool) signature is missing");
        var state = hero.NestedTypes.SingleOrDefault(x => x.Name == StateMachineName)
            ?? throw new InvalidOperationException("exact HeroController.Die state machine is missing");
        var iterator = die.CustomAttributes.SingleOrDefault(x =>
            x.AttributeType.FullName == "System.Runtime.CompilerServices.IteratorStateMachineAttribute");
        if (iterator == null || iterator.ConstructorArguments.Count != 1 ||
            iterator.ConstructorArguments[0].Value is not TypeReference iteratorType || iteratorType.FullName != state.FullName)
            throw new InvalidOperationException("HeroController.Die iterator signature changed");
        var moveNext = state.Methods.SingleOrDefault(x => x.Name == "MoveNext" && x.HasBody &&
            x.ReturnType.MetadataType == MetadataType.Boolean && x.Parameters.Count == 0)
            ?? throw new InvalidOperationException("exact HeroController.Die MoveNext is missing");
        var calls = moveNext.Body.Instructions.Where(x => x.OpCode.Code == Code.Callvirt &&
            x.Operand is MethodReference method && method.DeclaringType.FullName == manager.FullName &&
            method.Name == "PlayerDead" && method.Parameters.Count == 1 &&
            method.Parameters[0].ParameterType.MetadataType == MetadataType.Single &&
            method.ReturnType.FullName == "System.Collections.IEnumerator").ToList();
        if (calls.Count != 1) throw new InvalidOperationException("HeroController.Die must contain exactly one normal PlayerDead call");
        var call = calls[0];
        var waitLoad = call.Previous;
        var stateLoad = waitLoad?.Previous;
        var managerLoad = stateLoad?.Previous;
        var managerOwner = managerLoad?.Previous;
        var nativeStart = managerOwner?.Previous;
        if (waitLoad?.OpCode.Code != Code.Ldfld || waitLoad.Operand is not FieldReference wait ||
            wait.Name != "<deathWait>5__2" || wait.DeclaringType.FullName != state.FullName ||
            stateLoad?.OpCode.Code != Code.Ldarg_0 || managerLoad?.OpCode.Code != Code.Ldfld ||
            managerLoad.Operand is not FieldReference heroManager || heroManager.Name != "gm" ||
            heroManager.DeclaringType.FullName != hero.FullName || managerOwner?.OpCode.Code != Code.Ldloc_1 ||
            nativeStart?.OpCode.Code != Code.Ldloc_1)
            throw new InvalidOperationException("normal PlayerDead ownership site changed");
        var heroManagerDefinition = hero.Fields.SingleOrDefault(x => x.Name == "gm" && x.FieldType.FullName == manager.FullName)
            ?? throw new InvalidOperationException("HeroController manager owner field changed");
        return new DieShape { Module = module, Hero = hero, Manager = manager, StateMachine = state,
            MoveNext = moveNext, HeroManager = heroManagerDefinition, NativeStart = nativeStart };
    }

    static bool HasAnyBridgeMember(TypeDefinition manager) => manager.Fields.Any(x =>
            x.Name == OccurrenceName || x.Name == HeroName || x.Name == ManagerName) ||
        manager.Methods.Any(x => x.Name == HelperName);

    static bool AlreadyRewritten(DieShape shape)
    {
        if (!HasAnyBridgeMember(shape.Manager)) return false;
        var occurrence = shape.Manager.Fields.SingleOrDefault(x => x.Name == OccurrenceName &&
            x.IsPublic && x.IsStatic && x.FieldType.MetadataType == MetadataType.Int64);
        var hero = shape.Manager.Fields.SingleOrDefault(x => x.Name == HeroName && x.IsPublic && x.IsStatic &&
            x.FieldType.FullName == shape.Hero.FullName);
        var manager = shape.Manager.Fields.SingleOrDefault(x => x.Name == ManagerName && x.IsPublic && x.IsStatic &&
            x.FieldType.FullName == shape.Manager.FullName);
        var helper = shape.Manager.Methods.SingleOrDefault(x => x.Name == HelperName && x.IsPublic && x.IsStatic &&
            x.ReturnType.MetadataType == MetadataType.Void && x.Parameters.Count == 2 &&
            x.Parameters[0].ParameterType.FullName == shape.Hero.FullName &&
            x.Parameters[1].ParameterType.FullName == shape.Manager.FullName && x.HasBody);
        if (occurrence == null || hero == null || manager == null || helper == null)
            throw new InvalidOperationException("Silksong normal-death bridge members are incomplete");
        RequireHelperFingerprint(helper);
        var calls = shape.MoveNext.Body.Instructions.Where(x =>
            (x.OpCode.Code == Code.Call || x.OpCode.Code == Code.Callvirt) &&
            x.Operand is MethodReference method && method.Name == HelperName &&
            method.DeclaringType.FullName == shape.Manager.FullName).ToList();
        if (calls.Count != 1 || !ReferenceEquals(calls[0].Next, shape.NativeStart) ||
            calls[0].Previous?.OpCode.Code != Code.Ldfld ||
            calls[0].Previous?.Previous?.OpCode.Code != Code.Ldloc_1 ||
            calls[0].Previous?.Previous?.Previous?.OpCode.Code != Code.Ldloc_1)
            throw new InvalidOperationException("Silksong normal-death bridge call site is incomplete or moved");
        return true;
    }

    static MethodDefinition InjectBridge(DieShape shape)
    {
        var occurrence = new FieldDefinition(OccurrenceName, FieldAttributes.Public | FieldAttributes.Static,
            shape.Module.TypeSystem.Int64);
        var heroField = new FieldDefinition(HeroName, FieldAttributes.Public | FieldAttributes.Static, shape.Hero);
        var managerField = new FieldDefinition(ManagerName, FieldAttributes.Public | FieldAttributes.Static, shape.Manager);
        shape.Manager.Fields.Add(occurrence);
        shape.Manager.Fields.Add(heroField);
        shape.Manager.Fields.Add(managerField);
        var helper = new MethodDefinition(HelperName,
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            shape.Module.TypeSystem.Void);
        helper.Parameters.Add(new ParameterDefinition("hero", ParameterAttributes.None, shape.Hero));
        helper.Parameters.Add(new ParameterDefinition("manager", ParameterAttributes.None, shape.Manager));
        helper.Body.InitLocals = true;
        var maxDeaths = new VariableDefinition(shape.Module.TypeSystem.Int32);
        helper.Body.Variables.Add(maxDeaths);
        shape.Manager.Methods.Add(helper);

        var gameState = RequireType(shape.Module, "GlobalEnums.GameState");
        var perma = RequireType(shape.Module, "GlobalEnums.PermadeathModes");
        int playing = EnumValue(gameState, "PLAYING");
        int dead = EnumValue(perma, "Dead");
        var playerData = RequireType(shape.Module, "PlayerData");
        var demoHelper = RequireType(shape.Module, "DemoHelper");
        var demo = RequireType(shape.Module, "GlobalSettings.Demo");
        var managerPlayerData = RequireField(shape.Manager, "playerData", playerData.FullName);
        var permaField = RequireField(playerData, "permadeathMode", perma.FullName);
        var deathCount = RequireField(shape.Manager, "heroDeathCount", shape.Module.TypeSystem.Int32.FullName);
        var gameStateGetter = RequireMethod(shape.Manager, "get_GameState", gameState.FullName, 0);
        var gameplay = RequireMethod(shape.Manager, "IsGameplayScene", shape.Module.TypeSystem.Boolean.FullName, 0);
        var finished = RequireMethod(shape.Manager, "get_HasFinishedEnteringScene", shape.Module.TypeSystem.Boolean.FullName, 0);
        var transitioning = RequireMethod(shape.Manager, "get_IsInSceneTransition", shape.Module.TypeSystem.Boolean.FullName, 0);
        var loading = RequireMethod(shape.Manager, "get_IsLoadingSceneTransition", shape.Module.TypeSystem.Boolean.FullName, 0);
        var paused = RequireField(shape.Manager, "isPaused", shape.Module.TypeSystem.Boolean.FullName);
        var heroSilent = RequireMethod(shape.Hero, "get_SilentInstance", shape.Hero.FullName, 0);
        var managerSilent = RequireMethod(shape.Manager, "get_SilentInstance", shape.Manager.FullName, 0);
        var isDemo = RequireMethod(demoHelper, "get_IsDemoMode", shape.Module.TypeSystem.Boolean.FullName, 0);
        var demoMax = RequireMethod(demo, "get_MaxDeathCount", shape.Module.TypeSystem.Int32.FullName, 0);

        var il = helper.Body.GetILProcessor();
        var demoDone = Instruction.Create(OpCodes.Nop);
        var ret = Instruction.Create(OpCodes.Ret);
        void Add(Instruction instruction) => il.Append(instruction);
        Add(Instruction.Create(OpCodes.Ldarg_0)); Add(Instruction.Create(OpCodes.Brfalse, ret));
        Add(Instruction.Create(OpCodes.Ldarg_1)); Add(Instruction.Create(OpCodes.Brfalse, ret));
        Add(Instruction.Create(OpCodes.Call, heroSilent)); Add(Instruction.Create(OpCodes.Ldarg_0));
        Add(Instruction.Create(OpCodes.Ceq)); Add(Instruction.Create(OpCodes.Brfalse, ret));
        Add(Instruction.Create(OpCodes.Call, managerSilent)); Add(Instruction.Create(OpCodes.Ldarg_1));
        Add(Instruction.Create(OpCodes.Ceq)); Add(Instruction.Create(OpCodes.Brfalse, ret));
        Add(Instruction.Create(OpCodes.Ldarg_1)); Add(Instruction.Create(OpCodes.Callvirt, gameStateGetter));
        Add(Instruction.Create(OpCodes.Ldc_I4, playing)); Add(Instruction.Create(OpCodes.Bne_Un, ret));
        Add(Instruction.Create(OpCodes.Ldarg_1)); Add(Instruction.Create(OpCodes.Ldfld, paused)); Add(Instruction.Create(OpCodes.Brtrue, ret));
        Add(Instruction.Create(OpCodes.Ldarg_1)); Add(Instruction.Create(OpCodes.Callvirt, gameplay)); Add(Instruction.Create(OpCodes.Brfalse, ret));
        Add(Instruction.Create(OpCodes.Ldarg_1)); Add(Instruction.Create(OpCodes.Callvirt, finished)); Add(Instruction.Create(OpCodes.Brfalse, ret));
        Add(Instruction.Create(OpCodes.Ldarg_1)); Add(Instruction.Create(OpCodes.Callvirt, transitioning)); Add(Instruction.Create(OpCodes.Brtrue, ret));
        Add(Instruction.Create(OpCodes.Ldarg_1)); Add(Instruction.Create(OpCodes.Callvirt, loading)); Add(Instruction.Create(OpCodes.Brtrue, ret));
        Add(Instruction.Create(OpCodes.Ldarg_1)); Add(Instruction.Create(OpCodes.Ldfld, managerPlayerData));
        Add(Instruction.Create(OpCodes.Ldfld, permaField)); Add(Instruction.Create(OpCodes.Ldc_I4, dead)); Add(Instruction.Create(OpCodes.Beq, ret));
        Add(Instruction.Create(OpCodes.Call, isDemo)); Add(Instruction.Create(OpCodes.Brfalse, demoDone));
        Add(Instruction.Create(OpCodes.Call, demoMax)); Add(Instruction.Create(OpCodes.Stloc, maxDeaths));
        Add(Instruction.Create(OpCodes.Ldloc, maxDeaths)); Add(Instruction.Create(OpCodes.Ldc_I4_0)); Add(Instruction.Create(OpCodes.Ble, demoDone));
        Add(Instruction.Create(OpCodes.Ldarg_1)); Add(Instruction.Create(OpCodes.Ldfld, deathCount)); Add(Instruction.Create(OpCodes.Ldc_I4_1));
        Add(Instruction.Create(OpCodes.Add)); Add(Instruction.Create(OpCodes.Ldloc, maxDeaths)); Add(Instruction.Create(OpCodes.Bge, ret));
        Add(demoDone);
        Add(Instruction.Create(OpCodes.Ldsfld, occurrence)); Add(Instruction.Create(OpCodes.Ldc_I8, long.MaxValue)); Add(Instruction.Create(OpCodes.Beq, ret));
        Add(Instruction.Create(OpCodes.Ldarg_0)); Add(Instruction.Create(OpCodes.Stsfld, heroField));
        Add(Instruction.Create(OpCodes.Ldarg_1)); Add(Instruction.Create(OpCodes.Stsfld, managerField));
        Add(Instruction.Create(OpCodes.Ldsfld, occurrence)); Add(Instruction.Create(OpCodes.Ldc_I4_1));
        Add(Instruction.Create(OpCodes.Conv_I8)); Add(Instruction.Create(OpCodes.Add)); Add(Instruction.Create(OpCodes.Stsfld, occurrence));
        Add(ret);
        return helper;
    }

    static void RewriteMoveNext(DieShape shape, MethodDefinition helper)
    {
        var il = shape.MoveNext.Body.GetILProcessor();
        il.InsertBefore(shape.NativeStart, Instruction.Create(OpCodes.Ldloc_1));
        il.InsertBefore(shape.NativeStart, Instruction.Create(OpCodes.Ldloc_1));
        il.InsertBefore(shape.NativeStart, Instruction.Create(OpCodes.Ldfld, shape.HeroManager));
        il.InsertBefore(shape.NativeStart, Instruction.Create(OpCodes.Call, helper));
    }

    static TypeDefinition RequireType(ModuleDefinition module, string fullName) =>
        module.Types.SingleOrDefault(x => x.FullName == fullName)
        ?? throw new InvalidOperationException("required exact type is missing: " + fullName);
    static FieldDefinition RequireField(TypeDefinition type, string name, string fieldType) =>
        type.Fields.SingleOrDefault(x => x.Name == name && x.FieldType.FullName == fieldType)
        ?? throw new InvalidOperationException("required exact field is missing: " + type.FullName + "::" + name);
    static MethodDefinition RequireMethod(TypeDefinition type, string name, string returnType, int parameters) =>
        type.Methods.SingleOrDefault(x => x.Name == name && x.ReturnType.FullName == returnType && x.Parameters.Count == parameters)
        ?? throw new InvalidOperationException("required exact method is missing: " + type.FullName + "::" + name);
    static int EnumValue(TypeDefinition type, string name) => Convert.ToInt32(
        type.Fields.SingleOrDefault(x => x.Name == name && x.IsLiteral)?.Constant
        ?? throw new InvalidOperationException("required exact enum value is missing: " + type.FullName + "::" + name));

    static void RequireHelperFingerprint(MethodDefinition helper)
    {
        string actual = HelperFingerprint(helper);
        if (actual != PinnedHelperSha256)
            throw new InvalidOperationException("Silksong normal-death helper differs from canonical body: " + actual);
    }

    internal static string HelperFingerprint(MethodDefinition helper)
    {
        var instructions = helper.Body.Instructions;
        var indexes = instructions.Select((value, index) => (value, index)).ToDictionary(x => x.value, x => x.index);
        string Operand(object? value) => value switch
        {
            null => "-",
            Instruction target => "target:" + indexes[target],
            MethodReference method => "method:" + method.FullName,
            FieldReference field => "field:" + field.FullName,
            ParameterDefinition parameter => "arg:" + parameter.Index,
            VariableDefinition variable => "local:" + variable.Index,
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "-",
        };
        var normalized = new List<string> { "schema=task101-silksong-death-v1", "locals=" +
            string.Join(",", helper.Body.Variables.Select(x => x.VariableType.FullName)) };
        normalized.AddRange(instructions.Select((x, i) => i + "|" + x.OpCode.Code + "|" + Operand(x.Operand)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", normalized)))).ToLowerInvariant();
    }

    static string FileSha256(string path)
    {
        using var input = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
    }
}
