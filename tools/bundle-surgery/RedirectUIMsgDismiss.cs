using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace BundleSurgery;

// Adds one instance-scoped companion dismissal request to UIMsgBase.DoMsg's
// existing wait. The native coroutine still evaluates controller input and owns
// every sound, vibration, hide, completion and deactivation after that wait.
internal static class RedirectUIMsgDismiss
{
    const string ProxyName = "UIMsgProxy";
    const string BaseName = "UIMsgBase`1";
    const string StateMachineName = "<DoMsg>d__10";
    const string OwnerFieldName = "<>4__this";
    const string PressedFieldName = "<wasPressed>5__2";
    const string GenerationFieldName = "__dsCompanionDismissGeneration";
    const string ArmedFieldName = "__dsCompanionDismissArmed";
    const string RequestedFieldName = "__dsCompanionDismissRequested";
    const string BeginName = "DsPortCompanionDismissBegin";
    const string ArmName = "DsPortCompanionDismissArm";
    const string ObserveName = "DsPortCompanionDismissObserve";
    const string RequestName = "DsPortCompanionDismissRequest";
    const string ConsumeName = "DsPortCompanionDismissConsume";
    const string EndName = "DsPortCompanionDismissEnd";
    internal const string PINNED_GAME_VERSION = "1.0.29980";
    internal const string PINNED_NATIVE_TAIL_SHA256 = "1886e0884a720b0b53412e04f912fb6d7c31d7c9da9cd365e9ac6b85fc4bc179";
    static readonly IReadOnlyDictionary<string, string> PinnedBridgeBodySha256 =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [BeginName] = "6a64119447cce048bf44f71b37f2f804ef90cda1ed6beea0a7b2d4f963ea893e",
            [ArmName] = "1aa949b88527f1e6902916d27d995d3dd4f8416e7bab78229bdd1965210f9730",
            [ObserveName] = "d22a108de8fdd6ff26223e08f4474277b2e45f26b918a936e3bcd00d780a15d1",
            [RequestName] = "34f189627f03289b4dbc4ef8e8487c1eabe831de2e29be7123bacfe4cdecf84a",
            [ConsumeName] = "2f5f0aa762c4e369baf8ed677bdf6a25c6447254383a0eec7c5707dc1b834ef4",
            [EndName] = "374cae4558e2909df1eb56dd862fbbd471f6d01faadb63f7a5354bf6eeb31c79",
        };

    sealed class WaitShape
    {
        public required TypeDefinition Proxy;
        public required TypeDefinition StateMachine;
        public required FieldDefinition Owner;
        public required FieldDefinition Pressed;
        public required MethodDefinition MoveNext;
        public required MethodDefinition Dispose;
        public required Instruction BeginAfter;
        public required Instruction ArmAfter;
        public required Instruction PollStore;
        public required Instruction Continuation;
        public required Instruction NativeContinuation;
    }

    sealed class Bridge
    {
        public required MethodDefinition Begin;
        public required MethodDefinition Arm;
        public required MethodDefinition Observe;
        public required MethodDefinition Request;
        public required MethodDefinition Consume;
        public required MethodDefinition End;
    }

    public static int Run(string inputPath, string outputPath)
    {
        try
        {
            RequireDistinctPaths(inputPath, outputPath);
            if (!File.Exists(inputPath))
                throw new InvalidOperationException("native managed assembly input is missing");
            if (File.Exists(outputPath))
                throw new InvalidOperationException("refusing to replace an existing output");

            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(inputPath))!);
            var reader = new ReaderParameters { AssemblyResolver = resolver };
            using var assembly = AssemblyDefinition.ReadAssembly(inputPath, reader);
            var shape = RequireOriginalWaitShape(assembly.MainModule);

            if (AlreadyRewritten(shape))
            {
                File.Copy(inputPath, outputPath, overwrite: false);
                Console.WriteLine("  companion dismissal bridge already present; copied unchanged");
                return 0;
            }

            var bridge = InjectBridgeState(shape.Proxy);
            RewriteMoveNext(shape, bridge);
            RewriteDispose(shape, bridge);
            RequireExactNativeContinuation(shape.MoveNext, shape.NativeContinuation);

            var staging = outputPath + ".rewritten";
            try
            {
                assembly.Write(staging);
                using (var persisted = AssemblyDefinition.ReadAssembly(staging, reader))
                {
                    var persistedShape = RequireOriginalWaitShape(persisted.MainModule);
                    if (!AlreadyRewritten(persistedShape))
                        throw new InvalidOperationException("persisted companion dismissal rewrite did not verify");
                }
                File.Move(staging, outputPath, overwrite: false);
            }
            finally
            {
                if (File.Exists(staging)) File.Delete(staging);
            }
            Console.WriteLine("  injected one armed UIMsgBase companion dismissal wait bridge");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("  companion dismissal rewrite failed closed: " + error.Message);
            return 1;
        }
    }

    static void RequireDistinctPaths(string inputPath, string outputPath)
    {
        if (string.Equals(Path.GetFullPath(inputPath), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("input and output must be distinct; original assemblies are read-only");
    }

    static WaitShape RequireOriginalWaitShape(ModuleDefinition module)
    {
        var proxy = module.Types.SingleOrDefault(type => type.FullName == ProxyName)
            ?? throw new InvalidOperationException("exact UIMsgProxy type is missing");
        var messageBase = module.Types.SingleOrDefault(type => type.FullName == BaseName)
            ?? throw new InvalidOperationException("exact UIMsgBase`1 type is missing");
        var state = messageBase.NestedTypes.SingleOrDefault(type => type.Name == StateMachineName)
            ?? throw new InvalidOperationException("exact UIMsgBase.DoMsg state machine is missing");
        var owner = state.Fields.SingleOrDefault(field => field.Name == OwnerFieldName)
            ?? throw new InvalidOperationException("DoMsg state machine owner field is missing");
        var pressed = state.Fields.SingleOrDefault(field => field.Name == PressedFieldName && field.FieldType.MetadataType == MetadataType.Boolean)
            ?? throw new InvalidOperationException("DoMsg wait result field is missing");
        var moveNext = state.Methods.SingleOrDefault(method => method.Name == "MoveNext" && method.ReturnType.MetadataType == MetadataType.Boolean && method.HasBody)
            ?? throw new InvalidOperationException("DoMsg MoveNext body is missing");
        var dispose = state.Methods.SingleOrDefault(method => method.Name == "System.IDisposable.Dispose" && method.HasBody)
            ?? throw new InvalidOperationException("DoMsg Dispose body is missing");

        var instructions = moveNext.Body.Instructions;
        var skipCalls = instructions.Where(instruction =>
            instruction.OpCode.Code == Code.Callvirt &&
            instruction.Operand is MethodReference method &&
            method.Name == "get_WasSkipButtonPressed" &&
            method.DeclaringType.FullName == "InputHandler" &&
            method.ReturnType.MetadataType == MetadataType.Boolean).ToList();
        if (skipCalls.Count != 1)
            throw new InvalidOperationException("DoMsg must contain exactly one native skip-button poll");
        var skip = skipCalls[0];
        var pollStore = skip.Next;
        if (pollStore == null || pollStore.OpCode.Code != Code.Stfld || !FieldIs(pollStore.Operand, pressed) ||
            skip.Previous?.OpCode.Code != Code.Callvirt ||
            skip.Previous.Operand is not MethodReference input || input.Name != "get_inputHandler" || input.DeclaringType.FullName != "GameManager" ||
            skip.Previous.Previous?.OpCode.Code != Code.Call ||
            skip.Previous.Previous.Operand is not MethodReference game || game.Name != "get_instance" || game.DeclaringType.FullName != "GameManager" ||
            skip.Previous.Previous.Previous?.OpCode.Code != Code.Ldarg_0)
            throw new InvalidOperationException("DoMsg native skip-button poll shape changed: " +
                $"store={pollStore?.OpCode.Code}, field={(pollStore?.Operand as FieldReference)?.FullName}, " +
                $"definition={pressed.FullName}, same={ReferenceEquals(pollStore?.Operand, pressed)}, " +
                $"input={skip.Previous?.Operand}, game={skip.Previous?.Previous?.Operand}, load={skip.Previous?.Previous?.Previous?.OpCode.Code}");
        var pollStart = skip.Previous.Previous.Previous;

        var pressedStores = instructions.Where(instruction => instruction.OpCode.Code == Code.Stfld && FieldIs(instruction.Operand, pressed)).ToList();
        bool bridgeMarker = HasAnyBridgeMember(proxy);
        int expectedStores = bridgeMarker ? 3 : 2;
        var initialization = pressedStores.SingleOrDefault(instruction => instruction != pollStore && instruction.Previous?.OpCode.Code == Code.Ldc_I4_0)
            ?? throw new InvalidOperationException("DoMsg wait initialization is missing or ambiguous");
        if (pressedStores.Count != expectedStores)
            throw new InvalidOperationException("DoMsg wait field store count changed");
        var loop = instructions.SingleOrDefault(instruction =>
            instruction.OpCode.Code == Code.Brfalse_S && ReferenceEquals(instruction.Operand, pollStart) &&
            instruction.Previous?.OpCode.Code == Code.Ldfld && FieldIs(instruction.Previous.Operand, pressed));
        if (loop == null || loop.Next == null)
            throw new InvalidOperationException("DoMsg wait loop boundary changed");

        var setMessageCalls = instructions.Where(instruction =>
            (instruction.OpCode.Code == Code.Call || instruction.OpCode.Code == Code.Callvirt) &&
            instruction.Operand is MethodReference method && method.Name == "SetIsInMsg" && method.DeclaringType.FullName == ProxyName).ToList();
        if (setMessageCalls.Count != 2 ||
            setMessageCalls.SingleOrDefault(instruction => instruction.Previous?.OpCode.Code == Code.Ldc_I4_1) == null ||
            setMessageCalls.SingleOrDefault(instruction => instruction.Previous?.OpCode.Code == Code.Ldc_I4_0) == null)
            throw new InvalidOperationException("DoMsg native message ownership boundaries changed");
        var setupCalls = instructions.Where(instruction =>
            instruction.OpCode.Code == Code.Callvirt && instruction.Operand is MethodReference method &&
            method.Name == "Setup" && Declares(method, BaseName)).ToList();
        if (setupCalls.Count != 1 || instructions.IndexOf(setMessageCalls[0]) >= instructions.IndexOf(setupCalls[0]))
            throw new InvalidOperationException("DoMsg native Setup ownership boundary changed");
        var beginAfter = setupCalls[0];

        var continuation = loop.Next;
        var nativeContinuation = continuation;
        if (bridgeMarker)
        {
            if (continuation.OpCode.Code != Code.Ldloc_1 || continuation.Next?.Operand is not MethodReference end ||
                end.Name != EndName || end.DeclaringType.FullName != ProxyName || continuation.Next.Next == null)
                throw new InvalidOperationException("existing companion dismissal tail boundary is incomplete");
            nativeContinuation = continuation.Next.Next;
        }
        RequireExactNativeContinuation(moveNext, nativeContinuation);
        if (!bridgeMarker && (dispose.Body.Instructions.Count != 1 || dispose.Body.Instructions[0].OpCode.Code != Code.Ret))
            throw new InvalidOperationException("DoMsg cancellation Dispose shape changed");

        return new WaitShape
        {
            Proxy = proxy, StateMachine = state, Owner = owner, Pressed = pressed,
            MoveNext = moveNext, Dispose = dispose, BeginAfter = beginAfter,
            ArmAfter = initialization, PollStore = pollStore, Continuation = continuation,
            NativeContinuation = nativeContinuation,
        };
    }

    static bool HasAnyBridgeMember(TypeDefinition proxy)
    {
        string[] methods = { BeginName, ArmName, ObserveName, RequestName, ConsumeName, EndName };
        string[] fields = { GenerationFieldName, ArmedFieldName, RequestedFieldName };
        return proxy.Methods.Any(method => methods.Contains(method.Name)) ||
            proxy.Fields.Any(field => fields.Contains(field.Name));
    }

    static bool FieldIs(object? operand, FieldDefinition definition)
    {
        if (operand is not FieldReference reference) return false;
        try { return ReferenceEquals(reference.Resolve(), definition); }
        catch { return false; }
    }

    static bool MethodIs(object? operand, MethodDefinition definition)
    {
        if (operand is not MethodReference reference) return false;
        try { return ReferenceEquals(reference.Resolve(), definition); }
        catch { return false; }
    }

    static bool Declares(MethodReference method, string fullName) =>
        method.DeclaringType.FullName == fullName ||
        method.DeclaringType is GenericInstanceType generic && generic.ElementType.FullName == fullName;

    static string ScopeIdentity(IMetadataScope? scope) => scope switch
    {
        AssemblyNameReference assembly => "assembly:" + assembly.FullName,
        ModuleDefinition module => "module:" + (module.Assembly?.Name.FullName ?? "-") + "::" + module.Name,
        ModuleReference module => "module-ref:" + module.Name,
        null => "none",
        _ => scope.MetadataScopeType + ":" + scope.Name,
    };

    static string TypeIdentity(TypeReference type)
    {
        var element = type.GetElementType();
        return type.FullName + "@" + ScopeIdentity(element.Scope);
    }

    static void RequireExactNativeContinuation(MethodDefinition moveNext, Instruction continuation)
    {
        var fingerprint = NativeTailFingerprint(moveNext, continuation);
        if (!string.Equals(fingerprint, PINNED_NATIVE_TAIL_SHA256, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"UIMsgBase.DoMsg native continuation differs from pinned {PINNED_GAME_VERSION}: {fingerprint}");
    }

    internal static string NativeTailFingerprint(MethodDefinition method, Instruction continuation)
    {
        var body = method.Body;
        var instructions = body.Instructions;
        int start = instructions.IndexOf(continuation);
        if (start < 0) throw new InvalidOperationException("native continuation is outside MoveNext");
        var index = instructions.Select((instruction, at) => (instruction, at))
            .ToDictionary(pair => pair.instruction, pair => pair.at);
        var normalized = new List<string>
        {
            "schema=task105-uimsg-tail-v1",
            "game=" + PINNED_GAME_VERSION,
            "initLocals=" + body.InitLocals,
            "locals=" + string.Join(",", body.Variables.Select((variable, at) => at + ":" + TypeIdentity(variable.VariableType))),
            "handlers=" + body.ExceptionHandlers.Count,
        };
        foreach (var handler in body.ExceptionHandlers)
        {
            string Boundary(Instruction? boundary, bool end)
            {
                if (boundary == null) return end ? "END" : throw new InvalidOperationException("native continuation handler has a missing start");
                int at = index[boundary];
                if (at < start) throw new InvalidOperationException("native continuation exception boundary escapes pinned range");
                return (at - start).ToString(CultureInfo.InvariantCulture);
            }
            normalized.Add(string.Join("|", "EH", handler.HandlerType,
                Boundary(handler.TryStart, false), Boundary(handler.TryEnd, true),
                Boundary(handler.HandlerStart, false), Boundary(handler.HandlerEnd, true),
                handler.FilterStart == null ? "-" : Boundary(handler.FilterStart, false),
                handler.CatchType == null ? "-" : TypeIdentity(handler.CatchType)));
        }
        for (int at = start; at < instructions.Count; at++)
            normalized.Add(NormalizeInstruction(instructions[at], at - start, start, index));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", normalized)))).ToLowerInvariant();
    }

    internal static string NormalizeInstruction(Instruction instruction, int relative,
        int start, IReadOnlyDictionary<Instruction, int> index)
    {
        string Target(Instruction target)
        {
            if (!index.TryGetValue(target, out int at) || at < start)
                throw new InvalidOperationException("native continuation branch escapes pinned range");
            return (at - start).ToString(CultureInfo.InvariantCulture);
        }
        string Type(TypeReference type) => TypeIdentity(type);
        string Method(MethodReference method) => Type(method.DeclaringType) + "::" + method.Name +
            "(" + string.Join(",", method.Parameters.Select(parameter => Type(parameter.ParameterType))) + ")->" +
            Type(method.ReturnType) + "|cc=" + method.CallingConvention + "|this=" + method.HasThis + "|explicit=" + method.ExplicitThis;
        string Operand(object? operand) => operand switch
        {
            null => "-",
            Instruction target => "target:" + Target(target),
            Instruction[] targets => "targets:" + string.Join(",", targets.Select(Target)),
            MethodReference method => "method:" + Method(method),
            FieldReference field => "field:" + Type(field.DeclaringType) + "::" + field.Name + ":" + Type(field.FieldType),
            TypeReference type => "type:" + Type(type),
            VariableDefinition variable => "local:" + variable.Index + ":" + Type(variable.VariableType),
            ParameterDefinition parameter => "arg:" + parameter.Index + ":" + Type(parameter.ParameterType),
            CallSite site => "callsite:" + site.FullName,
            string value => "string:" + Convert.ToHexString(Encoding.UTF8.GetBytes(value)),
            float value => "f32:" + BitConverter.SingleToInt32Bits(value).ToString("x8", CultureInfo.InvariantCulture),
            double value => "f64:" + BitConverter.DoubleToInt64Bits(value).ToString("x16", CultureInfo.InvariantCulture),
            IFormattable value => "value:" + value.ToString(null, CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException("native continuation operand is unsupported: " + operand.GetType().FullName),
        };
        return relative.ToString(CultureInfo.InvariantCulture) + "|" + instruction.OpCode.Code + "|" + Operand(instruction.Operand);
    }

    internal static string BridgeBodyFingerprint(MethodDefinition method)
    {
        var body = method.Body;
        var instructions = body.Instructions;
        var index = instructions.Select((instruction, at) => (instruction, at))
            .ToDictionary(pair => pair.instruction, pair => pair.at);
        var normalized = new List<string>
        {
            "schema=task105-uimsg-helper-v1",
            "name=" + method.Name,
            "attributes=" + ((int)method.Attributes).ToString("x", CultureInfo.InvariantCulture),
            "implAttributes=" + ((int)method.ImplAttributes).ToString("x", CultureInfo.InvariantCulture),
            "semantics=" + ((int)method.SemanticsAttributes).ToString("x", CultureInfo.InvariantCulture),
            "callingConvention=" + ((int)method.CallingConvention).ToString("x", CultureInfo.InvariantCulture),
            "hasThis=" + method.HasThis,
            "explicitThis=" + method.ExplicitThis,
            "return=" + TypeIdentity(method.ReturnType),
            "returnAttributes=" + ((int)method.MethodReturnType.Attributes).ToString("x", CultureInfo.InvariantCulture),
            "returnCustomAttributes=" + method.MethodReturnType.CustomAttributes.Count,
            "returnHasConstant=" + method.MethodReturnType.HasConstant,
            "returnHasMarshal=" + method.MethodReturnType.HasMarshalInfo,
            "genericParameters=" + method.GenericParameters.Count,
            "customAttributes=" + method.CustomAttributes.Count,
            "securityDeclarations=" + method.SecurityDeclarations.Count,
            "overrides=" + method.Overrides.Count,
            "pinvoke=" + (method.PInvokeInfo == null ? "-" : method.PInvokeInfo.EntryPoint + "@" + method.PInvokeInfo.Module.Name),
            "parameters=" + string.Join(",", method.Parameters.Select((parameter, at) =>
                at + ":" + parameter.Name + ":" + TypeIdentity(parameter.ParameterType) + ":" +
                ((int)parameter.Attributes).ToString("x", CultureInfo.InvariantCulture) + ":ca=" + parameter.CustomAttributes.Count +
                ":constant=" + parameter.HasConstant + ":marshal=" + parameter.HasMarshalInfo)),
            "maxStack=" + body.MaxStackSize.ToString(CultureInfo.InvariantCulture),
            "initLocals=" + body.InitLocals,
            "locals=" + string.Join(",", body.Variables.Select((variable, at) =>
                at + ":" + TypeIdentity(variable.VariableType))),
            "handlers=" + body.ExceptionHandlers.Count,
        };
        foreach (var handler in body.ExceptionHandlers)
        {
            string Boundary(Instruction? boundary, bool end)
            {
                if (boundary == null) return end ? "END" : throw new InvalidOperationException("companion helper handler has a missing start");
                if (!index.TryGetValue(boundary, out int at))
                    throw new InvalidOperationException("companion helper handler escapes its method");
                return at.ToString(CultureInfo.InvariantCulture);
            }
            normalized.Add(string.Join("|", "EH", handler.HandlerType,
                Boundary(handler.TryStart, false), Boundary(handler.TryEnd, true),
                Boundary(handler.HandlerStart, false), Boundary(handler.HandlerEnd, true),
                handler.FilterStart == null ? "-" : Boundary(handler.FilterStart, false),
                handler.CatchType == null ? "-" : TypeIdentity(handler.CatchType)));
        }
        for (int at = 0; at < instructions.Count; at++)
            normalized.Add(NormalizeInstruction(instructions[at], at, 0, index));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", normalized)))).ToLowerInvariant();
    }

    static void RequireCanonicalBridgeBodies(IEnumerable<MethodDefinition> methods)
    {
        var actual = methods.ToDictionary(method => method.Name, BridgeBodyFingerprint, StringComparer.Ordinal);
        var mismatches = PinnedBridgeBodySha256
            .Where(expected => !actual.TryGetValue(expected.Key, out var fingerprint) || fingerprint != expected.Value)
            .Select(expected => expected.Key + "=" + actual.GetValueOrDefault(expected.Key, "missing"))
            .ToList();
        if (mismatches.Count != 0)
            throw new InvalidOperationException("existing companion dismissal helper body or attributes changed: " +
                string.Join(",", mismatches));
    }

    static void RequireCanonicalBridgeFields(TypeDefinition proxy, IReadOnlyList<FieldDefinition> fields)
    {
        var canonicalType = TypeIdentity(proxy.Module.TypeSystem.Int32);
        foreach (var field in fields)
        {
            if (!ReferenceEquals(field.DeclaringType, proxy) || !ReferenceEquals(field.Module, proxy.Module) ||
                field.Attributes != FieldAttributes.Private || TypeIdentity(field.FieldType) != canonicalType ||
                field.HasConstant || field.HasMarshalInfo || field.HasCustomAttributes || field.HasFieldRVA ||
                field.InitialValue is { Length: > 0 } || field.Offset >= 0)
                throw new InvalidOperationException("existing companion dismissal field metadata changed: " + field.Name);
        }
    }

    static void RequireCanonicalBridgeMethods(TypeDefinition proxy, IReadOnlyList<MethodDefinition> methods)
    {
        bool Canonical(MethodDefinition method, MetadataType result, string[] parameterNames, params MetadataType[] parameters) =>
            ReferenceEquals(method.DeclaringType, proxy) && ReferenceEquals(method.Module, proxy.Module) &&
            method.Attributes == (MethodAttributes.Public | MethodAttributes.HideBySig) &&
            method.ImplAttributes == (MethodImplAttributes.IL | MethodImplAttributes.Managed) &&
            method.SemanticsAttributes == MethodSemanticsAttributes.None &&
            method.CallingConvention == MethodCallingConvention.Default && method.HasThis && !method.ExplicitThis &&
            method.HasBody && method.ReturnType.MetadataType == result &&
            !method.HasGenericParameters && !method.HasCustomAttributes && !method.HasSecurityDeclarations &&
            !method.HasOverrides && method.PInvokeInfo == null &&
            method.MethodReturnType.Attributes == ParameterAttributes.None &&
            !method.MethodReturnType.HasCustomAttributes && !method.MethodReturnType.HasConstant &&
            !method.MethodReturnType.HasMarshalInfo &&
            method.Parameters.Select(parameter => parameter.ParameterType.MetadataType).SequenceEqual(parameters) &&
            method.Parameters.Select(parameter => parameter.Name).SequenceEqual(parameterNames) &&
            method.Parameters.All(parameter => parameter.Attributes == ParameterAttributes.None &&
                !parameter.HasCustomAttributes && !parameter.HasConstant && !parameter.HasMarshalInfo);

        if (!Canonical(methods[0], MetadataType.Void, Array.Empty<string>()) ||
            !Canonical(methods[1], MetadataType.Void, Array.Empty<string>()) ||
            !Canonical(methods[2], MetadataType.Int32, Array.Empty<string>()) ||
            !Canonical(methods[3], MetadataType.Boolean, new[] { "generation" }, MetadataType.Int32) ||
            !Canonical(methods[4], MetadataType.Boolean, Array.Empty<string>()) ||
            !Canonical(methods[5], MetadataType.Void, Array.Empty<string>()))
            throw new InvalidOperationException("existing companion dismissal method signatures or metadata changed");
    }

    static void RequireExactLocalBridgeOperands(TypeDefinition proxy, IReadOnlyList<MethodDefinition> methods,
        IReadOnlyList<FieldDefinition> fields)
    {
        var expectedFields = fields.ToDictionary(field => field.Name, StringComparer.Ordinal);
        foreach (var method in methods)
            foreach (var instruction in method.Body.Instructions)
                if (instruction.Operand is FieldReference reference)
                {
                    if (!expectedFields.TryGetValue(reference.Name, out var expected) || !FieldIs(reference, expected))
                        throw new InvalidOperationException("companion dismissal helper field operand escaped exact local definition: " + method.Name);
                }
    }

    static bool AlreadyRewritten(WaitShape shape)
    {
        string[] names = { BeginName, ArmName, ObserveName, RequestName, ConsumeName, EndName };
        var methods = names.Select(name => shape.Proxy.Methods.SingleOrDefault(method => method.Name == name)).ToArray();
        var fields = new[] { GenerationFieldName, ArmedFieldName, RequestedFieldName }
            .Select(name => shape.Proxy.Fields.SingleOrDefault(field => field.Name == name)).ToArray();
        bool any = methods.Any(method => method != null) || fields.Any(field => field != null);
        if (!any) return false;
        if (methods.Any(method => method == null) || fields.Any(field => field == null))
            throw new InvalidOperationException("partial companion dismissal bridge already exists");
        var exactMethods = methods.Select(method => method!).ToArray();
        var exactFields = fields.Select(field => field!).ToArray();
        RequireCanonicalBridgeFields(shape.Proxy, exactFields);
        RequireCanonicalBridgeMethods(shape.Proxy, exactMethods);
        RequireExactLocalBridgeOperands(shape.Proxy, exactMethods, exactFields);

        bool Call(Instruction? instruction, MethodDefinition expected) => instruction != null && MethodIs(instruction.Operand, expected);
        var beginLoad = shape.BeginAfter.Next;
        var armLoad = shape.ArmAfter.Next;
        var poll = shape.PollStore.Next;
        var continuation = shape.Continuation;
        var dispose = shape.Dispose.Body.Instructions;
        if (beginLoad?.OpCode.Code != Code.Ldloc_1 || !Call(beginLoad.Next, exactMethods[0]) ||
            armLoad?.OpCode.Code != Code.Ldloc_1 || !Call(armLoad.Next, exactMethods[1]) ||
            poll?.OpCode.Code != Code.Ldarg_0 || poll.Next?.OpCode.Code != Code.Ldarg_0 ||
            poll.Next.Next?.OpCode.Code != Code.Ldfld || !FieldIs(poll.Next.Next.Operand, shape.Pressed) ||
            poll.Next.Next.Next?.OpCode.Code != Code.Ldloc_1 || !Call(poll.Next.Next.Next.Next, exactMethods[4]) ||
            poll.Next.Next.Next.Next.Next?.OpCode.Code != Code.Or ||
            poll.Next.Next.Next.Next.Next.Next?.OpCode.Code != Code.Stfld ||
            !FieldIs(poll.Next.Next.Next.Next.Next.Next.Operand, shape.Pressed) ||
            continuation.OpCode.Code != Code.Ldloc_1 || !Call(continuation.Next, exactMethods[5]) ||
            dispose.Count != 4 || dispose[0].OpCode.Code != Code.Ldarg_0 ||
            dispose[1].OpCode.Code != Code.Ldfld || !FieldIs(dispose[1].Operand, shape.Owner) ||
            !Call(dispose[2], exactMethods[5]) || dispose[3].OpCode.Code != Code.Ret)
            throw new InvalidOperationException("existing companion dismissal call-site shape is incomplete");
        RequireCanonicalBridgeBodies(exactMethods);
        return true;
    }

    static Bridge InjectBridgeState(TypeDefinition proxy)
    {
        var module = proxy.Module;
        var generation = new FieldDefinition(GenerationFieldName, FieldAttributes.Private, module.TypeSystem.Int32);
        var armed = new FieldDefinition(ArmedFieldName, FieldAttributes.Private, module.TypeSystem.Int32);
        var requested = new FieldDefinition(RequestedFieldName, FieldAttributes.Private, module.TypeSystem.Int32);
        proxy.Fields.Add(generation); proxy.Fields.Add(armed); proxy.Fields.Add(requested);

        MethodDefinition Method(string name, TypeReference result)
        {
            var method = new MethodDefinition(name, MethodAttributes.Public | MethodAttributes.HideBySig, result);
            proxy.Methods.Add(method);
            return method;
        }
        var begin = Method(BeginName, module.TypeSystem.Void);
        var arm = Method(ArmName, module.TypeSystem.Void);
        var observe = Method(ObserveName, module.TypeSystem.Int32);
        var request = Method(RequestName, module.TypeSystem.Boolean);
        request.Parameters.Add(new ParameterDefinition("generation", ParameterAttributes.None, module.TypeSystem.Int32));
        var consume = Method(ConsumeName, module.TypeSystem.Boolean);
        var end = Method(EndName, module.TypeSystem.Void);

        EmitBegin(begin, generation, armed, requested);
        EmitArm(arm, generation, armed, requested);
        EmitObserve(observe, generation, armed);
        EmitRequest(request, generation, armed, requested);
        EmitConsume(consume, generation, armed, requested);
        EmitEnd(end, armed, requested);
        return new Bridge { Begin = begin, Arm = arm, Observe = observe, Request = request, Consume = consume, End = end };
    }

    static void EmitBegin(MethodDefinition method, FieldDefinition generation, FieldDefinition armed, FieldDefinition requested)
    {
        var il = method.Body.GetILProcessor();
        var nonzero = Instruction.Create(OpCodes.Nop);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, generation);
        il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stfld, generation);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, generation); il.Emit(OpCodes.Brtrue_S, nonzero);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Stfld, generation);
        il.Append(nonzero);
        Clear(il, armed); Clear(il, requested); il.Emit(OpCodes.Ret);
    }

    static void EmitArm(MethodDefinition method, FieldDefinition generation, FieldDefinition armed, FieldDefinition requested)
    {
        var il = method.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, generation); il.Emit(OpCodes.Stfld, armed);
        Clear(il, requested); il.Emit(OpCodes.Ret);
    }

    static void EmitObserve(MethodDefinition method, FieldDefinition generation, FieldDefinition armed)
    {
        var il = method.Body.GetILProcessor();
        var reject = Instruction.Create(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, generation); il.Emit(OpCodes.Brfalse_S, reject);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, armed); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, generation); il.Emit(OpCodes.Bne_Un_S, reject);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, generation); il.Emit(OpCodes.Ret);
        il.Append(reject); il.Emit(OpCodes.Ret);
    }

    static void EmitRequest(MethodDefinition method, FieldDefinition generation, FieldDefinition armed, FieldDefinition requested)
    {
        var il = method.Body.GetILProcessor();
        var reject = Instruction.Create(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Brfalse_S, reject);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, generation); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Bne_Un_S, reject);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, armed); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Bne_Un_S, reject);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stfld, requested);
        il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ret); il.Append(reject); il.Emit(OpCodes.Ret);
    }

    static void EmitConsume(MethodDefinition method, FieldDefinition generation, FieldDefinition armed, FieldDefinition requested)
    {
        var il = method.Body.GetILProcessor();
        var reject = Instruction.Create(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, generation); il.Emit(OpCodes.Brfalse_S, reject);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, armed); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, generation); il.Emit(OpCodes.Bne_Un_S, reject);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, requested); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, generation); il.Emit(OpCodes.Bne_Un_S, reject);
        Clear(il, armed); Clear(il, requested); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ret);
        il.Append(reject); il.Emit(OpCodes.Ret);
    }

    static void EmitEnd(MethodDefinition method, FieldDefinition armed, FieldDefinition requested)
    {
        var il = method.Body.GetILProcessor();
        Clear(il, armed); Clear(il, requested); il.Emit(OpCodes.Ret);
    }

    static void Clear(ILProcessor il, FieldDefinition field)
    {
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stfld, field);
    }

    static void RewriteMoveNext(WaitShape shape, Bridge bridge)
    {
        var il = shape.MoveNext.Body.GetILProcessor();
        InsertAfter(il, shape.BeginAfter,
            Instruction.Create(OpCodes.Ldloc_1), Instruction.Create(OpCodes.Call, bridge.Begin));
        InsertAfter(il, shape.ArmAfter,
            Instruction.Create(OpCodes.Ldloc_1), Instruction.Create(OpCodes.Call, bridge.Arm));
        InsertAfter(il, shape.PollStore,
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldfld, shape.Pressed),
            Instruction.Create(OpCodes.Ldloc_1),
            Instruction.Create(OpCodes.Call, bridge.Consume),
            Instruction.Create(OpCodes.Or),
            Instruction.Create(OpCodes.Stfld, shape.Pressed));
        var endLoad = Instruction.Create(OpCodes.Ldloc_1);
        il.InsertBefore(shape.Continuation, endLoad);
        il.InsertBefore(shape.Continuation, Instruction.Create(OpCodes.Call, bridge.End));
        shape.Continuation = endLoad;
        shape.MoveNext.Body.MaxStackSize = Math.Max(shape.MoveNext.Body.MaxStackSize, 4);
    }

    static void RewriteDispose(WaitShape shape, Bridge bridge)
    {
        var il = shape.Dispose.Body.GetILProcessor();
        var ret = shape.Dispose.Body.Instructions[0];
        il.InsertBefore(ret, Instruction.Create(OpCodes.Ldarg_0));
        il.InsertBefore(ret, Instruction.Create(OpCodes.Ldfld, shape.Owner));
        il.InsertBefore(ret, Instruction.Create(OpCodes.Call, bridge.End));
        shape.Dispose.Body.MaxStackSize = Math.Max(shape.Dispose.Body.MaxStackSize, 1);
    }

    static void InsertAfter(ILProcessor il, Instruction anchor, params Instruction[] additions)
    {
        foreach (var addition in additions)
        {
            il.InsertAfter(anchor, addition);
            anchor = addition;
        }
    }
}
