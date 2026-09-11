using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;
using System.Text;

namespace BundleSurgery;

internal static class BridgeSilksongNormalDeath
{
    internal const string PINNED_GAME_VERSION = "1.0.29980";
    internal const string PINNED_ASSEMBLY_SHA256 = "1af095416b89f73993058f9cbac3a93959d928314b735cc4acbca7bf1a952d2d";
    internal const string PINNED_REWRITTEN_ASSEMBLY_SHA256 = "37cd088f4de464b4cc7b01b193ac294a3dfeb282d1fa1757a653215d514db634";
    const string StateMachineName = "<Die>d__1101";
    const string OccurrenceName = "__dsNormalDeathOccurrence";
    const string HeroName = "__dsNormalDeathHero";
    const string ManagerName = "__dsNormalDeathManager";
    const string EligibleName = "__dsNormalDeathEligible";
    const string ClassifierName = "DsClassifyNormalDeath";
    const string RecorderName = "DsRecordNormalDeath";
    const string PinnedClassifierSha256 = "205c3ecd96f7a3cff0bd4076a61273479c7a9867a624f4fd3249ea8b9d8a27d4";
    const string PinnedRecorderSha256 = "f0f00da1c0ed44f07a800a767349b1ae6bba669ee7ef8e1d266bbea83e125b19";

    sealed class DieShape
    {
        public required ModuleDefinition Module;
        public required TypeDefinition Hero;
        public required TypeDefinition Manager;
        public required TypeDefinition StateMachine;
        public required MethodDefinition MoveNext;
        public required FieldDefinition HeroManager;
        public required FieldDefinition NonLethal;
        public required VariableDefinition MemoryScene;
        public required Instruction NormalizedBranch;
        public required Instruction NormalizedSite;
        public required Instruction NativeStart;
    }

    sealed class BridgeMembers
    {
        public required MethodDefinition Classifier;
        public required MethodDefinition Recorder;
        public required FieldDefinition Eligible;
    }

    public static int Verify(string path)
    {
        try
        {
            if (!File.Exists(path)) throw new InvalidOperationException("managed assembly input is missing");
            string hash = FileSha256(path);
            if (hash != PINNED_REWRITTEN_ASSEMBLY_SHA256)
                throw new InvalidOperationException("rewritten assembly differs from canonical identity: " + hash);
            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            using var assembly = AssemblyDefinition.ReadAssembly(path,
                new ReaderParameters { AssemblyResolver = resolver });
            if (!AlreadyRewritten(RequireExactDieShape(assembly.MainModule)))
                throw new InvalidOperationException("canonical strict death bridge is absent");
            Console.WriteLine("  verified executable strict-death bridge: normalized nonlethal, memory, cinematic/control, permadeath, demo terminal, duplicate, hazard, and unstable guards");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("  Silksong normal-death verification failed closed: " + error.Message);
            return 1;
        }
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
                if (!string.Equals(inputHash, PINNED_REWRITTEN_ASSEMBLY_SHA256, StringComparison.Ordinal))
                    throw new InvalidOperationException("already-bridged Silksong assembly differs from canonical rewritten identity: " + inputHash);
                File.Copy(inputPath, outputPath, false);
                Console.WriteLine("  exact Silksong normal-death bridge already present; copied unchanged");
                return 0;
            }
            if (HasAnyBridgeMember(shape))
                throw new InvalidOperationException("partial or foreign Silksong normal-death bridge is present");
            if (!string.Equals(inputHash, PINNED_ASSEMBLY_SHA256, StringComparison.Ordinal))
                throw new InvalidOperationException($"Assembly-CSharp.dll differs from pinned {PINNED_GAME_VERSION}: {inputHash}");

            var bridge = InjectBridge(shape);
            RewriteMoveNext(shape, bridge);
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
            Console.WriteLine("  injected exact post-normalization HeroController.Die normal-death occurrence bridge");
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
        var nonLethal = state.Fields.SingleOrDefault(x => x.Name == "nonLethal" &&
            x.FieldType.MetadataType == MetadataType.Boolean)
            ?? throw new InvalidOperationException("normalized nonLethal iterator field changed");
        var isMemory = RequireMethod(manager, "IsMemoryScene", module.TypeSystem.Boolean.FullName, 0);
        var memoryCalls = moveNext.Body.Instructions.Where(x =>
            (x.OpCode.Code == Code.Call || x.OpCode.Code == Code.Callvirt) &&
            x.Operand is MethodReference method && method.FullName == isMemory.FullName).ToList();
        if (memoryCalls.Count != 1) throw new InvalidOperationException("exact memory normalization call changed");
        var memoryStore = memoryCalls[0].Next;
        var memoryLocal = StoredVariable(memoryStore, moveNext.Body)
            ?? throw new InvalidOperationException("memory normalization local changed");
        var memoryLoad = memoryStore.Next;
        var normalizedBranch = memoryLoad?.Next;
        var assignmentStart = normalizedBranch?.Next;
        var assignmentStore = assignmentStart?.Next?.Next;
        var normalizedSite = moveNext.Body.Instructions.SkipWhile(x => !ReferenceEquals(x, normalizedBranch))
            .FirstOrDefault(x => x.OpCode.Code == Code.Ldarg_0 && x.Next?.OpCode.Code == Code.Ldfld &&
                x.Next.Operand is FieldReference field && field.FullName == nonLethal.FullName &&
                (x.Next.Next?.OpCode.Code == Code.Brfalse || x.Next.Next?.OpCode.Code == Code.Brfalse_S));
        if (LoadedVariable(memoryLoad, moveNext.Body) != memoryLocal || normalizedBranch?.OpCode.Code != Code.Brfalse_S ||
            normalizedBranch.Operand is not Instruction branchTarget || normalizedSite == null ||
            assignmentStart?.OpCode.Code != Code.Ldarg_0 || assignmentStart.Next?.OpCode.Code != Code.Ldc_I4_1 ||
            assignmentStore?.OpCode.Code != Code.Stfld || assignmentStore.Operand is not FieldReference assignedField ||
            assignedField.FullName != nonLethal.FullName || !ReferenceEquals(assignmentStore.Next, branchTarget))
            throw new InvalidOperationException("post-guard nonLethal/memory normalization site changed");
        return new DieShape { Module = module, Hero = hero, Manager = manager, StateMachine = state,
            MoveNext = moveNext, HeroManager = heroManagerDefinition, NonLethal = nonLethal,
            MemoryScene = memoryLocal, NormalizedBranch = normalizedBranch, NormalizedSite = normalizedSite,
            NativeStart = nativeStart };
    }

    static bool HasAnyBridgeMember(DieShape shape) => shape.Manager.Fields.Any(x =>
            x.Name == OccurrenceName || x.Name == HeroName || x.Name == ManagerName) ||
        shape.Manager.Methods.Any(x => x.Name == RecorderName || x.Name == ClassifierName) ||
        shape.StateMachine.Fields.Any(x => x.Name == EligibleName);

    static bool AlreadyRewritten(DieShape shape)
    {
        if (!HasAnyBridgeMember(shape)) return false;
        var occurrence = shape.Manager.Fields.SingleOrDefault(x => x.Name == OccurrenceName &&
            x.IsPublic && x.IsStatic && x.FieldType.MetadataType == MetadataType.Int64);
        var hero = shape.Manager.Fields.SingleOrDefault(x => x.Name == HeroName && x.IsPublic && x.IsStatic &&
            x.FieldType.FullName == shape.Hero.FullName);
        var manager = shape.Manager.Fields.SingleOrDefault(x => x.Name == ManagerName && x.IsPublic && x.IsStatic &&
            x.FieldType.FullName == shape.Manager.FullName);
        var eligible = shape.StateMachine.Fields.SingleOrDefault(x => x.Name == EligibleName && !x.IsStatic &&
            x.FieldType.MetadataType == MetadataType.Boolean);
        var classifier = shape.Manager.Methods.SingleOrDefault(x => x.Name == ClassifierName && x.IsPublic && x.IsStatic &&
            x.ReturnType.MetadataType == MetadataType.Boolean && x.Parameters.Count == 4 &&
            x.Parameters[0].ParameterType.FullName == shape.Hero.FullName &&
            x.Parameters[1].ParameterType.FullName == shape.Manager.FullName &&
            x.Parameters[2].ParameterType.MetadataType == MetadataType.Boolean &&
            x.Parameters[3].ParameterType.MetadataType == MetadataType.Boolean && x.HasBody);
        var recorder = shape.Manager.Methods.SingleOrDefault(x => x.Name == RecorderName && x.IsPublic && x.IsStatic &&
            x.ReturnType.MetadataType == MetadataType.Void && x.Parameters.Count == 3 &&
            x.Parameters[0].ParameterType.FullName == shape.Hero.FullName &&
            x.Parameters[1].ParameterType.FullName == shape.Manager.FullName &&
            x.Parameters[2].ParameterType.MetadataType == MetadataType.Boolean && x.HasBody);
        if (occurrence == null || hero == null || manager == null || eligible == null || classifier == null || recorder == null)
            throw new InvalidOperationException("Silksong normal-death bridge members are incomplete");
        RequireFingerprint(classifier, PinnedClassifierSha256, "classifier");
        RequireFingerprint(recorder, PinnedRecorderSha256, "recorder");

        var classifierCalls = Calls(shape.MoveNext, ClassifierName, shape.Manager.FullName);
        var recorderCalls = Calls(shape.MoveNext, RecorderName, shape.Manager.FullName);
        if (classifierCalls.Count != 1 || classifierCalls[0].Next?.OpCode.Code != Code.Stfld ||
            classifierCalls[0].Next.Operand is not FieldReference eligibilityStore || eligibilityStore.FullName != eligible.FullName ||
            !ReferenceEquals(classifierCalls[0].Next.Next, shape.NormalizedSite) ||
            LoadedVariable(classifierCalls[0].Previous, shape.MoveNext.Body) != shape.MemoryScene ||
            classifierCalls[0].Previous?.Previous?.OpCode.Code != Code.Ldfld ||
            classifierCalls[0].Previous.Previous.Operand is not FieldReference nonLethal || nonLethal.FullName != shape.NonLethal.FullName ||
            classifierCalls[0].Previous.Previous.Previous?.OpCode.Code != Code.Ldarg_0 ||
            classifierCalls[0].Previous.Previous.Previous.Previous?.OpCode.Code != Code.Ldfld ||
            classifierCalls[0].Previous.Previous.Previous.Previous.Operand is not FieldReference gm || gm.FullName != shape.HeroManager.FullName ||
            classifierCalls[0].Previous.Previous.Previous.Previous.Previous?.OpCode.Code != Code.Ldloc_1 ||
            classifierCalls[0].Previous.Previous.Previous.Previous.Previous.Previous?.OpCode.Code != Code.Ldloc_1 ||
            classifierCalls[0].Previous.Previous.Previous.Previous.Previous.Previous.Previous?.OpCode.Code != Code.Ldarg_0)
            throw new InvalidOperationException("post-normalization Silksong death classifier call site is incomplete or moved");
        if (recorderCalls.Count != 1 || !ReferenceEquals(recorderCalls[0].Next, shape.NativeStart) ||
            recorderCalls[0].Previous?.OpCode.Code != Code.Ldfld ||
            recorderCalls[0].Previous.Operand is not FieldReference eligibilityLoad || eligibilityLoad.FullName != eligible.FullName ||
            recorderCalls[0].Previous.Previous?.OpCode.Code != Code.Ldarg_0 ||
            recorderCalls[0].Previous.Previous.Previous?.OpCode.Code != Code.Ldfld ||
            recorderCalls[0].Previous.Previous.Previous.Operand is not FieldReference recorderGm || recorderGm.FullName != shape.HeroManager.FullName ||
            recorderCalls[0].Previous.Previous.Previous.Previous?.OpCode.Code != Code.Ldloc_1 ||
            recorderCalls[0].Previous.Previous.Previous.Previous.Previous?.OpCode.Code != Code.Ldloc_1)
            throw new InvalidOperationException("Silksong normal-death recorder call site is incomplete or moved");
        return true;
    }

    static List<Instruction> Calls(MethodDefinition owner, string name, string declaringType) =>
        owner.Body.Instructions.Where(x => (x.OpCode.Code == Code.Call || x.OpCode.Code == Code.Callvirt) &&
            x.Operand is MethodReference method && method.Name == name && method.DeclaringType.FullName == declaringType).ToList();

    static BridgeMembers InjectBridge(DieShape shape)
    {
        var occurrence = new FieldDefinition(OccurrenceName, FieldAttributes.Public | FieldAttributes.Static,
            shape.Module.TypeSystem.Int64);
        var heroField = new FieldDefinition(HeroName, FieldAttributes.Public | FieldAttributes.Static, shape.Hero);
        var managerField = new FieldDefinition(ManagerName, FieldAttributes.Public | FieldAttributes.Static, shape.Manager);
        var eligible = new FieldDefinition(EligibleName, FieldAttributes.Private, shape.Module.TypeSystem.Boolean);
        shape.Manager.Fields.Add(occurrence);
        shape.Manager.Fields.Add(heroField);
        shape.Manager.Fields.Add(managerField);
        shape.StateMachine.Fields.Add(eligible);

        var gameState = RequireType(shape.Module, "GlobalEnums.GameState");
        var perma = RequireType(shape.Module, "GlobalEnums.PermadeathModes");
        int playing = EnumValue(gameState, "PLAYING");
        int permaOff = EnumValue(perma, "Off");
        var playerData = RequireType(shape.Module, "PlayerData");
        var heroStates = RequireType(shape.Module, "HeroControllerStates");
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
        var canInput = RequireMethod(shape.Hero, "CanInput", shape.Module.TypeSystem.Boolean.FullName, 0);
        var relinquished = RequireField(shape.Hero, "controlReqlinquished", shape.Module.TypeSystem.Boolean.FullName);
        var heroState = RequireField(shape.Hero, "cState", heroStates.FullName);
        var dead = RequireField(heroStates, "dead", shape.Module.TypeSystem.Boolean.FullName);
        var hazardDeath = RequireField(heroStates, "hazardDeath", shape.Module.TypeSystem.Boolean.FullName);
        var hazardRespawning = RequireField(heroStates, "hazardRespawning", shape.Module.TypeSystem.Boolean.FullName);
        var isDemo = RequireMethod(demoHelper, "get_IsDemoMode", shape.Module.TypeSystem.Boolean.FullName, 0);
        var demoMax = RequireMethod(demo, "get_MaxDeathCount", shape.Module.TypeSystem.Int32.FullName, 0);

        var classifier = new MethodDefinition(ClassifierName,
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            shape.Module.TypeSystem.Boolean);
        classifier.Parameters.Add(new ParameterDefinition("hero", ParameterAttributes.None, shape.Hero));
        classifier.Parameters.Add(new ParameterDefinition("manager", ParameterAttributes.None, shape.Manager));
        classifier.Parameters.Add(new ParameterDefinition("normalizedNonLethal", ParameterAttributes.None, shape.Module.TypeSystem.Boolean));
        classifier.Parameters.Add(new ParameterDefinition("memoryForced", ParameterAttributes.None, shape.Module.TypeSystem.Boolean));
        classifier.Body.InitLocals = true;
        var classifierMaxDeaths = new VariableDefinition(shape.Module.TypeSystem.Int32);
        classifier.Body.Variables.Add(classifierMaxDeaths);
        shape.Manager.Methods.Add(classifier);
        var cil = classifier.Body.GetILProcessor();
        var classifierDemoDone = Instruction.Create(OpCodes.Nop);
        var classifierFalse = Instruction.Create(OpCodes.Ldc_I4_0);
        var classifierRet = Instruction.Create(OpCodes.Ret);
        void C(Instruction instruction) => cil.Append(instruction);
        C(Instruction.Create(OpCodes.Ldarg_0)); C(Instruction.Create(OpCodes.Brfalse, classifierFalse));
        C(Instruction.Create(OpCodes.Ldarg_1)); C(Instruction.Create(OpCodes.Brfalse, classifierFalse));
        C(Instruction.Create(OpCodes.Ldarg_2)); C(Instruction.Create(OpCodes.Brtrue, classifierFalse));
        C(Instruction.Create(OpCodes.Ldarg_3)); C(Instruction.Create(OpCodes.Brtrue, classifierFalse));
        C(Instruction.Create(OpCodes.Call, heroSilent)); C(Instruction.Create(OpCodes.Ldarg_0)); C(Instruction.Create(OpCodes.Ceq)); C(Instruction.Create(OpCodes.Brfalse, classifierFalse));
        C(Instruction.Create(OpCodes.Call, managerSilent)); C(Instruction.Create(OpCodes.Ldarg_1)); C(Instruction.Create(OpCodes.Ceq)); C(Instruction.Create(OpCodes.Brfalse, classifierFalse));
        C(Instruction.Create(OpCodes.Ldarg_1)); C(Instruction.Create(OpCodes.Callvirt, gameStateGetter)); C(Instruction.Create(OpCodes.Ldc_I4, playing)); C(Instruction.Create(OpCodes.Bne_Un, classifierFalse));
        C(Instruction.Create(OpCodes.Ldarg_1)); C(Instruction.Create(OpCodes.Ldfld, paused)); C(Instruction.Create(OpCodes.Brtrue, classifierFalse));
        C(Instruction.Create(OpCodes.Ldarg_1)); C(Instruction.Create(OpCodes.Callvirt, gameplay)); C(Instruction.Create(OpCodes.Brfalse, classifierFalse));
        C(Instruction.Create(OpCodes.Ldarg_1)); C(Instruction.Create(OpCodes.Callvirt, finished)); C(Instruction.Create(OpCodes.Brfalse, classifierFalse));
        C(Instruction.Create(OpCodes.Ldarg_1)); C(Instruction.Create(OpCodes.Callvirt, transitioning)); C(Instruction.Create(OpCodes.Brtrue, classifierFalse));
        C(Instruction.Create(OpCodes.Ldarg_1)); C(Instruction.Create(OpCodes.Callvirt, loading)); C(Instruction.Create(OpCodes.Brtrue, classifierFalse));
        C(Instruction.Create(OpCodes.Ldarg_1)); C(Instruction.Create(OpCodes.Ldfld, managerPlayerData)); C(Instruction.Create(OpCodes.Ldfld, permaField)); C(Instruction.Create(OpCodes.Ldc_I4, permaOff)); C(Instruction.Create(OpCodes.Bne_Un, classifierFalse));
        C(Instruction.Create(OpCodes.Ldarg_0)); C(Instruction.Create(OpCodes.Ldfld, relinquished)); C(Instruction.Create(OpCodes.Brtrue, classifierFalse));
        C(Instruction.Create(OpCodes.Ldarg_0)); C(Instruction.Create(OpCodes.Callvirt, canInput)); C(Instruction.Create(OpCodes.Brfalse, classifierFalse));
        foreach (var stateFlag in new[] { dead, hazardDeath, hazardRespawning })
        { C(Instruction.Create(OpCodes.Ldarg_0)); C(Instruction.Create(OpCodes.Ldfld, heroState)); C(Instruction.Create(OpCodes.Ldfld, stateFlag)); C(Instruction.Create(OpCodes.Brtrue, classifierFalse)); }
        C(Instruction.Create(OpCodes.Call, isDemo)); C(Instruction.Create(OpCodes.Brfalse, classifierDemoDone));
        C(Instruction.Create(OpCodes.Call, demoMax)); C(Instruction.Create(OpCodes.Stloc, classifierMaxDeaths));
        C(Instruction.Create(OpCodes.Ldloc, classifierMaxDeaths)); C(Instruction.Create(OpCodes.Ldc_I4_0)); C(Instruction.Create(OpCodes.Ble, classifierDemoDone));
        C(Instruction.Create(OpCodes.Ldarg_1)); C(Instruction.Create(OpCodes.Ldfld, deathCount)); C(Instruction.Create(OpCodes.Ldc_I4_1)); C(Instruction.Create(OpCodes.Add)); C(Instruction.Create(OpCodes.Ldloc, classifierMaxDeaths)); C(Instruction.Create(OpCodes.Bge, classifierFalse));
        C(classifierDemoDone); C(Instruction.Create(OpCodes.Ldc_I4_1)); C(classifierRet); C(classifierFalse); C(Instruction.Create(OpCodes.Ret));

        var recorder = new MethodDefinition(RecorderName,
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            shape.Module.TypeSystem.Void);
        recorder.Parameters.Add(new ParameterDefinition("hero", ParameterAttributes.None, shape.Hero));
        recorder.Parameters.Add(new ParameterDefinition("manager", ParameterAttributes.None, shape.Manager));
        recorder.Parameters.Add(new ParameterDefinition("eligible", ParameterAttributes.None, shape.Module.TypeSystem.Boolean));
        shape.Manager.Methods.Add(recorder);
        var ril = recorder.Body.GetILProcessor();
        var recorderRet = Instruction.Create(OpCodes.Ret);
        void R(Instruction instruction) => ril.Append(instruction);
        R(Instruction.Create(OpCodes.Ldarg_2)); R(Instruction.Create(OpCodes.Brfalse, recorderRet));
        R(Instruction.Create(OpCodes.Ldarg_0)); R(Instruction.Create(OpCodes.Brfalse, recorderRet));
        R(Instruction.Create(OpCodes.Ldarg_1)); R(Instruction.Create(OpCodes.Brfalse, recorderRet));
        R(Instruction.Create(OpCodes.Call, heroSilent)); R(Instruction.Create(OpCodes.Ldarg_0)); R(Instruction.Create(OpCodes.Ceq)); R(Instruction.Create(OpCodes.Brfalse, recorderRet));
        R(Instruction.Create(OpCodes.Call, managerSilent)); R(Instruction.Create(OpCodes.Ldarg_1)); R(Instruction.Create(OpCodes.Ceq)); R(Instruction.Create(OpCodes.Brfalse, recorderRet));
        R(Instruction.Create(OpCodes.Ldarg_1)); R(Instruction.Create(OpCodes.Callvirt, gameStateGetter)); R(Instruction.Create(OpCodes.Ldc_I4, playing)); R(Instruction.Create(OpCodes.Bne_Un, recorderRet));
        R(Instruction.Create(OpCodes.Ldarg_1)); R(Instruction.Create(OpCodes.Ldfld, paused)); R(Instruction.Create(OpCodes.Brtrue, recorderRet));
        R(Instruction.Create(OpCodes.Ldarg_1)); R(Instruction.Create(OpCodes.Callvirt, gameplay)); R(Instruction.Create(OpCodes.Brfalse, recorderRet));
        R(Instruction.Create(OpCodes.Ldarg_1)); R(Instruction.Create(OpCodes.Callvirt, finished)); R(Instruction.Create(OpCodes.Brfalse, recorderRet));
        R(Instruction.Create(OpCodes.Ldarg_1)); R(Instruction.Create(OpCodes.Callvirt, transitioning)); R(Instruction.Create(OpCodes.Brtrue, recorderRet));
        R(Instruction.Create(OpCodes.Ldarg_1)); R(Instruction.Create(OpCodes.Callvirt, loading)); R(Instruction.Create(OpCodes.Brtrue, recorderRet));
        R(Instruction.Create(OpCodes.Ldarg_1)); R(Instruction.Create(OpCodes.Ldfld, managerPlayerData)); R(Instruction.Create(OpCodes.Ldfld, permaField)); R(Instruction.Create(OpCodes.Ldc_I4, permaOff)); R(Instruction.Create(OpCodes.Bne_Un, recorderRet));
        R(Instruction.Create(OpCodes.Ldsfld, occurrence)); R(Instruction.Create(OpCodes.Ldc_I8, long.MaxValue)); R(Instruction.Create(OpCodes.Beq, recorderRet));
        R(Instruction.Create(OpCodes.Ldarg_0)); R(Instruction.Create(OpCodes.Stsfld, heroField));
        R(Instruction.Create(OpCodes.Ldarg_1)); R(Instruction.Create(OpCodes.Stsfld, managerField));
        R(Instruction.Create(OpCodes.Ldsfld, occurrence)); R(Instruction.Create(OpCodes.Ldc_I4_1)); R(Instruction.Create(OpCodes.Conv_I8)); R(Instruction.Create(OpCodes.Add)); R(Instruction.Create(OpCodes.Stsfld, occurrence));
        R(recorderRet);
        return new BridgeMembers { Classifier = classifier, Recorder = recorder, Eligible = eligible };
    }

    static void RewriteMoveNext(DieShape shape, BridgeMembers bridge)
    {
        var il = shape.MoveNext.Body.GetILProcessor();
        var classifyStart = Instruction.Create(OpCodes.Ldarg_0);
        il.InsertBefore(shape.NormalizedSite, classifyStart);
        il.InsertBefore(shape.NormalizedSite, Instruction.Create(OpCodes.Ldloc_1));
        il.InsertBefore(shape.NormalizedSite, Instruction.Create(OpCodes.Ldloc_1));
        il.InsertBefore(shape.NormalizedSite, Instruction.Create(OpCodes.Ldfld, shape.HeroManager));
        il.InsertBefore(shape.NormalizedSite, Instruction.Create(OpCodes.Ldarg_0));
        il.InsertBefore(shape.NormalizedSite, Instruction.Create(OpCodes.Ldfld, shape.NonLethal));
        il.InsertBefore(shape.NormalizedSite, Instruction.Create(OpCodes.Ldloc, shape.MemoryScene));
        il.InsertBefore(shape.NormalizedSite, Instruction.Create(OpCodes.Call, bridge.Classifier));
        il.InsertBefore(shape.NormalizedSite, Instruction.Create(OpCodes.Stfld, bridge.Eligible));
        shape.NormalizedBranch.Operand = classifyStart;

        il.InsertBefore(shape.NativeStart, Instruction.Create(OpCodes.Ldloc_1));
        il.InsertBefore(shape.NativeStart, Instruction.Create(OpCodes.Ldloc_1));
        il.InsertBefore(shape.NativeStart, Instruction.Create(OpCodes.Ldfld, shape.HeroManager));
        il.InsertBefore(shape.NativeStart, Instruction.Create(OpCodes.Ldarg_0));
        il.InsertBefore(shape.NativeStart, Instruction.Create(OpCodes.Ldfld, bridge.Eligible));
        il.InsertBefore(shape.NativeStart, Instruction.Create(OpCodes.Call, bridge.Recorder));
    }

    static VariableDefinition? StoredVariable(Instruction? instruction, MethodBody body) => instruction?.OpCode.Code switch
    {
        Code.Stloc_0 => body.Variables[0], Code.Stloc_1 => body.Variables[1],
        Code.Stloc_2 => body.Variables[2], Code.Stloc_3 => body.Variables[3],
        Code.Stloc or Code.Stloc_S => instruction.Operand as VariableDefinition, _ => null,
    };
    static VariableDefinition? LoadedVariable(Instruction? instruction, MethodBody body) => instruction?.OpCode.Code switch
    {
        Code.Ldloc_0 => body.Variables[0], Code.Ldloc_1 => body.Variables[1],
        Code.Ldloc_2 => body.Variables[2], Code.Ldloc_3 => body.Variables[3],
        Code.Ldloc or Code.Ldloc_S => instruction.Operand as VariableDefinition, _ => null,
    };

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

    static void RequireFingerprint(MethodDefinition helper, string expected, string label)
    {
        string actual = HelperFingerprint(helper);
        if (actual != expected)
            throw new InvalidOperationException("Silksong normal-death " + label + " differs from canonical body: " + actual);
    }

    internal static string HelperFingerprint(MethodDefinition helper)
    {
        var instructions = helper.Body.Instructions;
        var indexes = instructions.Select((value, index) => (value, index)).ToDictionary(x => x.value, x => x.index);
        string Operand(object? value) => value switch
        {
            null => "-", Instruction target => "target:" + indexes[target],
            MethodReference method => "method:" + method.FullName,
            FieldReference field => "field:" + field.FullName,
            ParameterDefinition parameter => "arg:" + parameter.Index,
            VariableDefinition variable => "local:" + variable.Index,
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "-",
        };
        var normalized = new List<string> { "schema=task101-silksong-death-v2", "locals=" +
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
