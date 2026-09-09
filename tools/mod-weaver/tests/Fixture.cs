using System.Runtime.Loader;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Reflection = System.Reflection;

namespace ModWeaver.RegressionTests;

internal sealed record Patch
{
    public string Target { get; init; } = "Run";
    public bool? Prefix { get; init; }
    public bool Postfix { get; init; } = true;
    public int State { get; init; }
    public int ArgumentDelta { get; init; }
    public int PostfixArgumentDelta { get; init; }
    public int? Result { get; init; }
    public int Digit { get; init; } = 1;
    public int PrefixDigit { get; init; }
    public bool ReturnResult { get; init; }
    public bool ArgumentByValue { get; init; }
    public bool VoidTarget { get; init; }
}

internal sealed class Fixture : IDisposable
{
    readonly string _root;
    readonly string _inputs;
    readonly AssemblyDefinition _harmony;
    readonly AssemblyDefinition _bepinex;
    readonly List<AssemblyDefinition> _plugins = new();
    readonly MethodDefinition _patchAttribute;
    readonly MethodDefinition _pluginAttribute;

    public string Staged { get; }
    public AssemblyDefinition Game { get; }
    public TypeDefinition Target => Game.MainModule.GetType("Fixture.Target");
    public TypeDefinition Api => Game.MainModule.GetType("Fixture.Api");

    public Fixture()
    {
        _root = Path.GetRelativePath(Environment.CurrentDirectory,
            Path.Combine(AppContext.BaseDirectory, "fixtures", Guid.NewGuid().ToString("N")));
        Staged = Path.Combine(_root, "staged");
        _inputs = Path.Combine(_root, "inputs");
        Directory.CreateDirectory(Staged);
        Directory.CreateDirectory(_inputs);
        File.Copy(typeof(object).Assembly.Location, Path.Combine(Staged, "System.Private.CoreLib.dll"));

        _harmony = NewAssembly("0Harmony");
        _patchAttribute = Attribute(_harmony, "HarmonyLib", "HarmonyPatch", 2);
        _harmony.Write(Path.Combine(Staged, "0Harmony.dll"));
        _bepinex = NewAssembly("BepInEx");
        _pluginAttribute = Attribute(_bepinex, "BepInEx", "BepInPlugin", 3);
        _bepinex.Write(Path.Combine(Staged, "BepInEx.dll"));

        Game = NewAssembly("Assembly-CSharp");
        BuildGame();
        Game.Write(Path.Combine(Staged, "Assembly-CSharp.dll"));
    }

    public static AssemblyDefinition NewAssembly(string name)
    {
        var assembly = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition(name, new Version(1, 0, 0, 0)), name, ModuleKind.Dll);
        var core = (AssemblyNameReference)assembly.MainModule.TypeSystem.CoreLibrary;
        var runtime = typeof(object).Assembly.GetName();
        core.Name = runtime.Name!;
        core.Version = runtime.Version!;
        core.PublicKeyToken = runtime.GetPublicKeyToken();
        return assembly;
    }

    public static TypeDefinition Type(AssemblyDefinition assembly, string name, bool isStatic = true)
    {
        var type = new TypeDefinition("Fixture", name,
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit |
            (isStatic ? TypeAttributes.Abstract | TypeAttributes.Sealed : 0),
            assembly.MainModule.TypeSystem.Object);
        assembly.MainModule.Types.Add(type);
        return type;
    }

    public static MethodDefinition Method(TypeDefinition type, string name, TypeReference result,
        bool isStatic = true)
    {
        var method = new MethodDefinition(name,
            MethodAttributes.Public | MethodAttributes.HideBySig |
            (isStatic ? MethodAttributes.Static : 0), result);
        type.Methods.Add(method);
        return method;
    }

    public static ParameterDefinition Parameter(MethodDefinition method, string name, TypeReference type)
    {
        var parameter = new ParameterDefinition(name, ParameterAttributes.None, type);
        method.Parameters.Add(parameter);
        return parameter;
    }

    public static FieldDefinition Field(TypeDefinition type, string name, TypeReference? fieldType = null,
        bool isStatic = true)
    {
        var field = new FieldDefinition(name,
            FieldAttributes.Public | (isStatic ? FieldAttributes.Static : 0),
            fieldType ?? type.Module.TypeSystem.Int32);
        type.Fields.Add(field);
        return field;
    }

    public static MethodDefinition Attribute(AssemblyDefinition assembly, string ns, string name, int strings)
    {
        var module = assembly.MainModule;
        var type = new TypeDefinition(ns, name, TypeAttributes.Public | TypeAttributes.Class,
            module.ImportReference(typeof(Attribute)));
        module.Types.Add(type);
        var ctor = Method(type, ".ctor", module.TypeSystem.Void, isStatic: false);
        ctor.IsSpecialName = ctor.IsRuntimeSpecialName = true;
        for (var i = 0; i < strings; i++) Parameter(ctor, "arg" + i, module.TypeSystem.String);
        var parent = typeof(Attribute).GetConstructor(
            Reflection.BindingFlags.Instance | Reflection.BindingFlags.NonPublic, null,
            System.Type.EmptyTypes, null)!;
        var il = ctor.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, module.ImportReference(parent));
        il.Emit(OpCodes.Ret);
        return ctor;
    }

    static void Annotate(TypeDefinition type, MethodDefinition ctor, params string[] values)
    {
        var attr = new CustomAttribute(type.Module.ImportReference(ctor));
        foreach (var value in values)
            attr.ConstructorArguments.Add(new CustomAttributeArgument(type.Module.TypeSystem.String, value));
        type.CustomAttributes.Add(attr);
    }

    void BuildGame()
    {
        var module = Game.MainModule;
        var target = Type(Game, "Target", isStatic: false);
        var seed = Field(target, "Seed", isStatic: false);
        foreach (var name in new[] { "OriginalCalls", "FinallyCalls", "Trace" }) Field(target, name);
        var ctor = Method(target, ".ctor", module.TypeSystem.Void, isStatic: false);
        ctor.IsSpecialName = ctor.IsRuntimeSpecialName = true;
        var il = ctor.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, module.ImportReference(typeof(object).GetConstructor(System.Type.EmptyTypes)!));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, 10);
        il.Emit(OpCodes.Stfld, seed);
        il.Emit(OpCodes.Ret);

        var run = TargetMethod("Run", module.TypeSystem.Int32);
        il = run.Body.GetILProcessor();
        Original(il);
        AddToArgument(il, run.Parameters[0], 2);
        var negative = Instruction.Create(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldind_I4);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, negative);
        // Exercise long skip branches and widening after repeated weaves.
        for (var i = 0; i < 160; i++) il.Emit(OpCodes.Nop);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, seed);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldind_I4);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ret);
        il.Append(negative);
        il.Emit(OpCodes.Ret);

        var touch = TargetMethod("Touch", module.TypeSystem.Void);
        il = touch.Body.GetILProcessor();
        Original(il);
        var done = Instruction.Create(OpCodes.Ret);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldind_I4);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, done);
        AddToArgument(il, touch.Parameters[0], 2);
        il.Emit(OpCodes.Ret);
        il.Append(done);

        BuildFinallyMethod();
        BuildProtectedReturn();

        var api = Type(Game, "Api");
        Field(api, "Value");
        var present = Method(api, "Present", module.TypeSystem.Int32);
        present.Body.GetILProcessor().Emit(OpCodes.Ldc_I4_7);
        present.Body.GetILProcessor().Emit(OpCodes.Ret);
        var identity = Method(api, "Identity", module.TypeSystem.Void);
        var generic = new GenericParameter("T", identity);
        identity.GenericParameters.Add(generic);
        identity.ReturnType = generic;
        Parameter(identity, "value", generic);
        identity.Body.GetILProcessor().Emit(OpCodes.Ldarg_0);
        identity.Body.GetILProcessor().Emit(OpCodes.Ret);

        var box = Type(Game, "Box`1", isStatic: false);
        var element = new GenericParameter("T", box);
        box.GenericParameters.Add(element);
        Field(box, "Value", element);
        var echo = Method(box, "Echo", element);
        Parameter(echo, "value", element);
        echo.Body.GetILProcessor().Emit(OpCodes.Ldarg_0);
        echo.Body.GetILProcessor().Emit(OpCodes.Ret);
        var derived = Type(Game, "Derived`2", isStatic: false);
        derived.GenericParameters.Add(new GenericParameter("TUnused", derived));
        var inherited = new GenericParameter("TValue", derived);
        derived.GenericParameters.Add(inherited);
        var baseType = new GenericInstanceType(box);
        baseType.GenericArguments.Add(inherited);
        derived.BaseType = baseType;
    }

    MethodDefinition TargetMethod(string name, TypeReference result)
    {
        var method = Method(Target, name, result, isStatic: false);
        Parameter(method, "value", new ByReferenceType(Game.MainModule.TypeSystem.Int32));
        return method;
    }

    void BuildFinallyMethod()
    {
        var module = Game.MainModule;
        var method = TargetMethod("WithFinally", module.TypeSystem.Int32);
        var value = new VariableDefinition(module.TypeSystem.Int32);
        method.Body.Variables.Add(value);
        method.Body.InitLocals = true;
        var il = method.Body.GetILProcessor();
        var first = Instruction.Create(OpCodes.Nop);
        var normal = Instruction.Create(OpCodes.Nop);
        var handler = Instruction.Create(OpCodes.Nop);
        var finish = Instruction.Create(OpCodes.Ldloc, value);
        il.Append(first);
        Original(il);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldind_I4);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge_S, normal);
        il.Emit(OpCodes.Newobj, module.ImportReference(
            typeof(InvalidOperationException).GetConstructor(System.Type.EmptyTypes)!));
        il.Emit(OpCodes.Throw);
        il.Append(normal);
        AddToArgument(il, method.Parameters[0], 2);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldind_I4);
        il.Emit(OpCodes.Stloc, value);
        il.Emit(OpCodes.Leave_S, finish);
        il.Append(handler);
        Increment(il, Target.Fields.Single(f => f.Name == "FinallyCalls"));
        Record(il, 6);
        il.Emit(OpCodes.Endfinally);
        il.Append(finish);
        il.Emit(OpCodes.Ret);
        method.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Finally)
        {
            TryStart = first, TryEnd = handler, HandlerStart = handler, HandlerEnd = finish,
        });
    }

    void BuildProtectedReturn()
    {
        var method = TargetMethod("ProtectedReturn", Game.MainModule.TypeSystem.Int32);
        var il = method.Body.GetILProcessor();
        var first = Instruction.Create(OpCodes.Nop);
        var handler = Instruction.Create(OpCodes.Nop);
        il.Append(first);
        Original(il);
        il.Emit(OpCodes.Ldc_I4_7);
        il.Emit(OpCodes.Ret);
        il.Append(handler);
        Increment(il, Target.Fields.Single(f => f.Name == "FinallyCalls"));
        Record(il, 6);
        il.Emit(OpCodes.Endfinally);
        // Non-C# IL may return inside a try and use an open-ended handler.
        method.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Finally)
        {
            TryStart = first, TryEnd = handler, HandlerStart = handler,
        });
    }

    void Original(ILProcessor il)
    {
        Increment(il, Target.Fields.Single(f => f.Name == "OriginalCalls"));
        Record(il, 5);
    }

    static void Increment(ILProcessor il, FieldReference field)
    {
        il.Emit(OpCodes.Ldsfld, field);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stsfld, field);
    }

    void Record(ILProcessor il, int digit)
    {
        if (digit == 0) return;
        var trace = il.Body.Method.Module.ImportReference(Target.Fields.Single(f => f.Name == "Trace"));
        il.Emit(OpCodes.Ldsfld, trace);
        il.Emit(OpCodes.Ldc_I4, 10);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Ldc_I4, digit);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stsfld, trace);
    }

    static void AddToArgument(ILProcessor il, ParameterDefinition argument, int amount)
    {
        if (amount == 0) return;
        il.Emit(OpCodes.Ldarg, argument);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldind_I4);
        il.Emit(OpCodes.Ldc_I4, amount);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stind_I4);
    }

    public AssemblyDefinition Plugin(string name, params Patch[] patches)
    {
        var plugin = NewAssembly(name);
        _plugins.Add(plugin);
        Annotate(Type(plugin, "Plugin"), _pluginAttribute, "tests." + name, name, "1.0.0");
        for (var i = 0; i < patches.Length; i++) BuildPatch(plugin, patches[i], i);
        return plugin;
    }

    void BuildPatch(AssemblyDefinition plugin, Patch spec, int index)
    {
        var module = plugin.MainModule;
        var type = Type(plugin, "Patch" + index);
        type.IsNotPublic = true;
        Annotate(type, _patchAttribute, "Fixture.Target", spec.Target);
        var fields = new[] { "PrefixCalls", "PostfixCalls", "StateSeen", "ArgumentSeen",
            "InstanceSeen", "PrefixInstanceSeen", "ResultSeen" }.ToDictionary(n => n, n => Field(type, n));

        if (spec.Prefix is bool continues)
        {
            var prefix = Method(type, "Prefix", module.TypeSystem.Boolean);
            prefix.IsPrivate = true;
            var args = PatchParameters(prefix, spec, prefix: true);
            var il = prefix.Body.GetILProcessor();
            Increment(il, fields["PrefixCalls"]);
            Record(il, spec.PrefixDigit);
            il.Emit(OpCodes.Ldarg, args["__state"]);
            il.Emit(OpCodes.Ldc_I4, spec.State);
            il.Emit(OpCodes.Stind_I4);
            ReadInstance(il, args["__instance"], fields["PrefixInstanceSeen"]);
            AddToArgument(il, args["value"], spec.ArgumentDelta);
            if (spec.Result is int result && !spec.VoidTarget)
            {
                il.Emit(OpCodes.Ldarg, args["__result"]);
                il.Emit(OpCodes.Ldc_I4, result);
                il.Emit(OpCodes.Stind_I4);
            }
            il.Emit(continues ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);
        }

        if (!spec.Postfix) return;
        var postfix = Method(type, "Postfix",
            spec.ReturnResult ? module.TypeSystem.Int32 : module.TypeSystem.Void);
        postfix.IsPrivate = true;
        var parameters = PatchParameters(postfix, spec, prefix: false);
        var code = postfix.Body.GetILProcessor();
        Increment(code, fields["PostfixCalls"]);
        Record(code, spec.Digit);
        code.Emit(OpCodes.Ldarg, parameters["__state"]);
        code.Emit(OpCodes.Stsfld, fields["StateSeen"]);
        code.Emit(OpCodes.Ldarg, parameters["value"]);
        if (!spec.ArgumentByValue) code.Emit(OpCodes.Ldind_I4);
        code.Emit(OpCodes.Stsfld, fields["ArgumentSeen"]);
        ReadInstance(code, parameters["__instance"], fields["InstanceSeen"]);
        if (!spec.ArgumentByValue) AddToArgument(code, parameters["value"], spec.PostfixArgumentDelta);
        if (!spec.VoidTarget)
        {
            code.Emit(OpCodes.Ldarg, parameters["__result"]);
            if (!spec.ReturnResult) code.Emit(OpCodes.Ldind_I4);
            code.Emit(OpCodes.Stsfld, fields["ResultSeen"]);
            code.Emit(OpCodes.Ldarg, parameters["__result"]);
            if (!spec.ReturnResult)
            {
                code.Emit(OpCodes.Dup);
                code.Emit(OpCodes.Ldind_I4);
            }
            code.Emit(OpCodes.Ldc_I4, 10);
            code.Emit(OpCodes.Mul);
            code.Emit(OpCodes.Ldc_I4, spec.Digit);
            code.Emit(OpCodes.Add);
            if (!spec.ReturnResult) code.Emit(OpCodes.Stind_I4);
        }
        code.Emit(OpCodes.Ret);
    }

    Dictionary<string, ParameterDefinition> PatchParameters(MethodDefinition method, Patch spec, bool prefix)
    {
        var module = method.Module;
        Parameter(method, "value", !prefix && spec.ArgumentByValue
            ? module.TypeSystem.Int32 : new ByReferenceType(module.TypeSystem.Int32));
        if (!spec.VoidTarget)
            Parameter(method, "__result", !prefix && spec.ReturnResult
                ? module.TypeSystem.Int32 : new ByReferenceType(module.TypeSystem.Int32));
        Parameter(method, "__state", prefix
            ? new ByReferenceType(module.TypeSystem.Int32) : module.TypeSystem.Int32);
        Parameter(method, "__instance", module.ImportReference(Target));
        return method.Parameters.ToDictionary(p => p.Name);
    }

    void ReadInstance(ILProcessor il, ParameterDefinition instance, FieldDefinition destination)
    {
        il.Emit(OpCodes.Ldarg, instance);
        il.Emit(OpCodes.Ldfld, il.Body.Method.Module.ImportReference(Target.Fields.Single(f => f.Name == "Seed")));
        il.Emit(OpCodes.Stsfld, destination);
    }

    public void MissingMember(AssemblyDefinition plugin, bool field, bool wrongSignature = false)
    {
        var module = plugin.MainModule;
        var helper = Method(plugin.MainModule.GetType("Fixture.Plugin"), "Unused", module.TypeSystem.Void);
        var il = helper.Body.GetILProcessor();
        if (field)
        {
            il.Emit(OpCodes.Ldsfld, new FieldReference(wrongSignature ? "Value" : "MissingField",
                wrongSignature ? module.TypeSystem.String : module.TypeSystem.Int32,
                module.ImportReference(Api)));
        }
        else
        {
            var member = new MethodReference(wrongSignature ? "Present" : "Missing",
                module.TypeSystem.Int32, module.ImportReference(Api));
            if (wrongSignature)
            {
                member.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
                il.Emit(OpCodes.Ldc_I4_0);
            }
            il.Emit(OpCodes.Call, member);
        }
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ret);
    }

    public static void DependsOn(AssemblyDefinition plugin, AssemblyDefinition dependency) =>
        plugin.MainModule.AssemblyReferences.Add(new AssemblyNameReference(
            dependency.Name.Name, dependency.Name.Version));

    public IReadOnlyList<PluginReport> Run(params AssemblyDefinition[] order)
    {
        foreach (var plugin in _plugins) plugin.Write(InputPath(plugin));
        return new Weaver(Staged).Run(order.Select(InputPath).ToArray());
    }

    public string InputPath(AssemblyDefinition plugin) => Path.Combine(_inputs, plugin.Name.Name + ".dll");

    public Execution Load() => new(Staged, _plugins.Select(p => p.Name.Name)
        .Where(n => File.Exists(Path.Combine(Staged, n + ".dll"))).ToArray());

    public void Dispose()
    {
        foreach (var plugin in _plugins) plugin.Dispose();
        Game.Dispose();
        _harmony.Dispose();
        _bepinex.Dispose();
        Directory.Delete(_root, recursive: true);
    }
}

internal sealed class Execution : IDisposable
{
    sealed class Context(string staged) : AssemblyLoadContext(isCollectible: true)
    {
        protected override Reflection.Assembly? Load(Reflection.AssemblyName assemblyName)
        {
            if (assemblyName.Name == typeof(object).Assembly.GetName().Name) return null;
            var path = Path.Combine(staged, assemblyName.Name + ".dll");
            return File.Exists(path) ? Open(path) : null;
        }

        public Reflection.Assembly Open(string path)
        {
            using var stream = File.OpenRead(path);
            return LoadFromStream(stream);
        }
    }

    readonly Context _context;
    readonly Dictionary<string, Reflection.Assembly> _plugins;
    readonly object _target;
    public System.Type Target { get; }

    public Execution(string staged, string[] plugins)
    {
        _context = new Context(staged);
        _context.Open(Path.Combine(staged, "0Harmony.dll"));
        _context.Open(Path.Combine(staged, "BepInEx.dll"));
        Target = _context.Open(Path.Combine(staged, "Assembly-CSharp.dll")).GetType("Fixture.Target")!;
        _target = Activator.CreateInstance(Target)!;
        _plugins = plugins.ToDictionary(n => n,
            n => _context.Open(Path.Combine(staged, n + ".dll")));
    }

    public void Reset()
    {
        foreach (var field in Target.GetFields().Where(f => f.IsStatic)) field.SetValue(null, 0);
        foreach (var plugin in _plugins.Values)
        {
            foreach (var type in plugin.GetTypes())
                foreach (var field in type.GetFields().Where(f => f.IsStatic && f.FieldType == typeof(int)))
                    field.SetValue(null, 0);
            plugin.GetType(Weaver.GateType)!.GetField(Weaver.GateField)!.SetValue(null, false);
        }
    }

    public void Enable(params string[] plugins)
    {
        foreach (var plugin in plugins)
            _plugins[plugin].GetType(Weaver.GateType)!.GetField(Weaver.GateField)!.SetValue(null, true);
    }

    public (int? Result, int Argument) Call(int value, string method = "Run")
    {
        object?[] args = { value };
        var result = Target.GetMethod(method)!.Invoke(_target, args);
        return ((int?)result, (int)args[0]!);
    }

    public int GameField(string name) => (int)Target.GetField(name)!.GetValue(null)!;

    public int PatchField(string plugin, string name, int index = 0) =>
        (int)_plugins[plugin].GetType("Fixture.Patch" + index)!.GetField(name)!.GetValue(null)!;

    public object? Invoke(string plugin, string method) =>
        _plugins[plugin].GetType("Fixture.Plugin")!.GetMethod(method)!.Invoke(null, null);

    public void Dispose() => _context.Unload();
}
