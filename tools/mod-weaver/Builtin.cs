using Mono.Cecil;
using Mono.Cecil.Cil;

namespace ModWeaver;

/// <summary>
/// The port's own weaves: patches that are part of the build rather than part
/// of somebody's mod folder.
///
/// ── why this exists at all ─────────────────────────────────────────────────
///
/// Silksong clamps the shape it will render into. ForceCameraAspect:
///
///     AutoScaleViewportShared:
///         MinMaxFloat(1.6f, 2.3916667f).GetClampedBetween(w / (float)h)
///
/// Anything narrower than 1.6 : 1 is letterboxed by the game itself, on every
/// platform. That is Team Cherry's framing decision and on a 16:9 monitor it
/// never comes up -- but Android is not 16:9. A Galaxy Z Fold's inner screen is
/// 2160x1856, which is 1.164 : 1, and a 4:3 handheld is 1.333 : 1. Both sit
/// below the floor, so both keep black bars no matter how perfectly the window
/// and the render target are matched. Measured on a Fold 6: 253 px top and
/// bottom, 13.6%, exactly (1 - 1.164/1.6) / 2.
///
/// ── why it is woven rather than patched at runtime ─────────────────────────
///
/// The constant is baked into IL, and by the time the game runs there is no IL
/// left -- il2cpp has turned it into C++ and then into a .so. The only moment
/// it can be changed is the one this tool already owns: after the depot's
/// assemblies are staged and before il2cpp is handed them.
///
/// ── why it is still a runtime toggle ───────────────────────────────────────
///
/// Because a constant is not the only thing IL can hold. Rather than writing a
/// different number in, the load is replaced with a CALL to a gate injected
/// beside it, and the gate answers from a field the game's own C# can set at
/// startup. So the weave is unconditional and free, and turning the feature on
/// and off costs a relaunch rather than the twenty minutes an il2cpp
/// conversion costs. That is the same bargain Mods.gates strikes for plugins,
/// and for the same reason.
///
/// ── how it fails ───────────────────────────────────────────────────────────
///
/// Safely, in both directions. An unset gate reads zero, and the gate treats
/// anything below 1.0 as "no answer" and returns the game's own 1.6 -- so a
/// build whose C# side never runs behaves exactly like an unwoven one. And if
/// the anchor below is ever not found, because the game was updated and the
/// method moved or the constants changed, nothing is written and the build
/// continues on stock behaviour. A missing frill must never cost somebody a
/// game that boots.
/// </summary>
internal static class Builtin
{
    /// The type injected into Assembly-CSharp. Deliberately in the global
    /// namespace, as the game's own classes are, and deliberately named so
    /// that it is obvious in a stack trace that it is not Team Cherry's.
    public const string GateType = "SilksongAspectGate";

    /// The field the game's C# writes, and the method the woven call reads it
    /// through. Named on the patch side too -- see AspectGate.cs.
    public const string FloorField = "Floor";
    public const string FloorMethod = "GetFloor";
    public const string CeilingField = "Ceiling";
    public const string CeilingMethod = "GetCeiling";

    /// What the game asks for, and what it means.
    ///
    /// The pair is the anchor rather than either number alone: 1.6f appears
    /// nine times in Assembly-CSharp and only once immediately before
    /// 2.3916667f. CameraAspectScaler builds a MinMaxFloat too, but from
    /// 1.7777778f, so matching the pair cannot hit it by accident -- and
    /// because BOTH ends are replaced from the one anchor, the ceiling is
    /// found positionally rather than by searching for its value, which is
    /// what keeps CameraAspectScaler's copy of it out of this.
    const float StockFloor = 1.6f;
    const float StockCeiling = 2.3916667f;

    const string TargetType = "ForceCameraAspect";
    const string TargetMethod = "AutoScaleViewportShared";

    const string HollowKnightPatchAssembly = "HollowKnightPatches.dll";
    const string SilksongPatchAssembly = "SilksongPatches.dll";
    const string HealthManagerType = "HealthManager";
    const string HitInstanceType = "HitInstance";
    const string HitMethod = "Hit";
    const string OneHitPatchType =
        "DualSouls.Mods.HollowKnight.HollowKnightOneHitDamagePatch";
    const string OneHitPatchMethod = "BeforeHit";

    /// <summary>
    /// Applies every built-in weave to the staged assembly set.
    ///
    /// Returns what happened, one line per weave, for the build log. Optional
    /// presentation weaves remain best-effort; mandatory Silksong gameplay hooks
    /// throw before any write when their exact contract is missing or partial.
    /// </summary>
    public static List<string> Apply(string assemblies)
    {
        var notes = new List<string>();
        var path = Path.Combine(assemblies, "Assembly-CSharp.dll");
        if (!File.Exists(path))
        {
            notes.Add("builtins: no Assembly-CSharp.dll in the staged set; skipped");
            return notes;
        }

        var resolver = new StagedResolver(assemblies);
        AssemblyDefinition assembly;
        try
        {
            assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters
            {
                // In memory, so the file can be written back to the same path.
                InMemory = true,
                AssemblyResolver = resolver,
            });
        }
        catch (Exception e)
        {
            notes.Add($"builtins: cannot read Assembly-CSharp.dll ({e.GetType().Name}); skipped");
            return notes;
        }

        var changed = false;
        var mandatorySilksongGameplayChanged = false;
        var silksongPatchPath = Path.Combine(assemblies, SilksongPatchAssembly);
        if (File.Exists(silksongPatchPath))
        {
            try
            {
                changed |= WeaveAspectFloor(assembly, notes);
            }
            catch (Exception e)
            {
                notes.Add($"aspect floor: not applied ({e.GetType().Name}: {e.Message})");
            }

            using var silksongPatches = AssemblyDefinition.ReadAssembly(silksongPatchPath, new ReaderParameters
            {
                InMemory = true,
                AssemblyResolver = resolver,
            });
            const string silksongHooks = "DualSouls.Mods.Silksong.SilksongGameplayHooks";
            if (assembly.MainModule.GetType(HealthManagerType) is not null ||
                silksongPatches.MainModule.GetType(silksongHooks) is not null)
            {
                mandatorySilksongGameplayChanged =
                    WeaveSilksongGameplayHooks(assembly, silksongPatches, notes);
                changed |= mandatorySilksongGameplayChanged;
            }
        }

        var patchPath = Path.Combine(assemblies, HollowKnightPatchAssembly);
        if (File.Exists(patchPath))
        {
            try
            {
                using var patches = AssemblyDefinition.ReadAssembly(patchPath, new ReaderParameters
                {
                    InMemory = true,
                    AssemblyResolver = resolver,
                });
                try
                {
                    changed |= WeaveHollowKnightOneHit(assembly, patches, notes);
                }
                catch (Exception e)
                {
                    notes.Add($"one-hit damage: not applied ({e.GetType().Name}: {e.Message})");
                }
                try
                {
                    changed |= WeaveHollowKnightGameplayHooks(assembly, patches, notes);
                }
                catch (Exception e)
                {
                    notes.Add($"gameplay hooks: not applied ({e.GetType().Name}: {e.Message})");
                }
            }
            catch (Exception e)
            {
                notes.Add($"one-hit damage: not applied ({e.GetType().Name}: {e.Message})");
            }
        }

        if (!changed) return notes;

        try
        {
            assembly.Write(path);
            notes.Add("builtins: Assembly-CSharp.dll rewritten");
        }
        catch (Exception e)
        {
            if (mandatorySilksongGameplayChanged)
                throw new InvalidOperationException(
                    "Mandatory Silksong gameplay hooks could not be written", e);
            notes.Add($"builtins: could not write Assembly-CSharp.dll ({e.GetType().Name}: {e.Message})");
        }
        return notes;
    }

    /// <summary>
    /// Replaces the floor constant with a call to an injected gate.
    ///
    /// One instruction changes. Everything else about the method -- the
    /// ceiling, the clamp, the viewport arithmetic, the height multiplier --
    /// is left exactly as Team Cherry wrote it, because the goal is to let the
    /// game's own code run on a number it was never given rather than to
    /// reimplement any part of it.
    /// </summary>
    static bool WeaveAspectFloor(AssemblyDefinition assembly, List<string> notes)
    {
        var module = assembly.MainModule;

        var type = module.GetType(TargetType);
        if (type is null)
        {
            notes.Add($"aspect floor: no {TargetType} in this build; skipped");
            return false;
        }

        var method = type.Methods.FirstOrDefault(m => m.Name == TargetMethod && m.HasBody);
        if (method is null)
        {
            notes.Add($"aspect floor: {TargetType}.{TargetMethod} not found; skipped");
            return false;
        }

        // The anchor: ldc.r4 1.6 immediately followed by ldc.r4 2.3916667.
        var il = method.Body.Instructions;
        Instruction? floor = null;
        for (var i = 0; i + 1 < il.Count; i++)
        {
            if (!IsLoad(il[i], StockFloor)) continue;
            if (!IsLoad(il[i + 1], StockCeiling)) continue;
            if (floor is not null)
            {
                notes.Add($"aspect floor: {TargetType}.{TargetMethod} has more than one "
                          + "candidate pair; skipped");
                return false;
            }
            floor = il[i];
        }

        if (floor is null)
        {
            // Already woven is not a failure: it is what a second run looks
            // like, and saying so is more useful than saying nothing.
            var already = module.GetType(GateType) is not null;
            notes.Add(already
                ? "aspect floor: already woven; nothing to do"
                : $"aspect floor: the {StockFloor}/{StockCeiling} pair is not in "
                  + $"{TargetType}.{TargetMethod}; skipped (game updated?)");
            return false;
        }

        // Taken before anything is replaced: the ceiling is the instruction
        // after the floor, and once the floor is swapped out that relationship
        // is gone.
        var ceiling = floor.Next;

        var gate = module.GetType(GateType) ?? InjectGate(module);
        var floorGetter = gate.Methods.First(m => m.Name == FloorMethod);
        var ceilingGetter = gate.Methods.First(m => m.Name == CeilingMethod);

        var processor = method.Body.GetILProcessor();
        processor.Replace(floor, processor.Create(OpCodes.Call, floorGetter));
        processor.Replace(ceiling, processor.Create(OpCodes.Call, ceilingGetter));

        notes.Add($"aspect range: {TargetType}.{TargetMethod} now asks {GateType} "
                  + $"instead of loading {StockFloor}/{StockCeiling}");
        return true;
    }

    static bool WeaveHollowKnightOneHit(
        AssemblyDefinition game, AssemblyDefinition patches, List<string> notes)
    {
        var gameModule = game.MainModule;
        var healthManager = gameModule.GetType(HealthManagerType);
        var hitInstance = gameModule.GetType(HitInstanceType);
        if (healthManager is null || hitInstance is null)
        {
            notes.Add("one-hit damage: HealthManager/HitInstance not found; skipped");
            return false;
        }

        var hits = healthManager.Methods.Where(m =>
            m.Name == HitMethod && m.HasBody && !m.IsStatic
            && m.ReturnType.MetadataType == MetadataType.Void
            && m.Parameters.Count == 1
            && !m.Parameters[0].ParameterType.IsByReference
            && m.Parameters[0].ParameterType.FullName == hitInstance.FullName).ToList();
        if (hits.Count != 1)
        {
            notes.Add($"one-hit damage: expected one {HealthManagerType}.{HitMethod}"
                      + $"({HitInstanceType}), found {hits.Count}; skipped");
            return false;
        }

        var patchType = patches.MainModule.GetType(OneHitPatchType);
        var prefixes = patchType?.Methods.Where(m =>
            m.Name == OneHitPatchMethod && m.IsPublic && m.IsStatic
            && m.ReturnType.MetadataType == MetadataType.Void
            && m.Parameters.Count == 2
            && m.Parameters[0].ParameterType.FullName == healthManager.FullName
            && m.Parameters[1].ParameterType is ByReferenceType byReference
            && byReference.ElementType.FullName == hitInstance.FullName).ToList()
            ?? new List<MethodDefinition>();
        if (prefixes.Count != 1)
        {
            notes.Add($"one-hit damage: exact {OneHitPatchType}.{OneHitPatchMethod} prefix"
                      + $" not found; skipped");
            return false;
        }

        var hit = hits[0];
        if (hit.Body.Instructions.Any(i =>
                i.OpCode == OpCodes.Call
                && i.Operand is MethodReference called
                && called.DeclaringType.FullName == OneHitPatchType
                && called.Name == OneHitPatchMethod))
        {
            notes.Add("one-hit damage: already woven; nothing to do");
            return false;
        }

        var processor = hit.Body.GetILProcessor();
        var first = hit.Body.Instructions[0];
        processor.InsertBefore(first, processor.Create(OpCodes.Ldarg_0));
        processor.InsertBefore(first, processor.Create(OpCodes.Ldarga_S, hit.Parameters[0]));
        processor.InsertBefore(first,
            processor.Create(OpCodes.Call, gameModule.ImportReference(prefixes[0])));
        notes.Add($"one-hit damage: {HealthManagerType}.{HitMethod} now calls managed prefix");
        return true;
    }

    static bool WeaveHollowKnightGameplayHooks(
        AssemblyDefinition game, AssemblyDefinition patches, List<string> notes)
    {
        const string hookTypeName =
            "DualSouls.Mods.HollowKnight.HollowKnightGameplayHooks";
        var module = game.MainModule;
        var hero = module.GetType("HeroController");
        var player = module.GetType("PlayerData");
        var hooks = patches.MainModule.GetType(hookTypeName);
        if (hero is null || player is null || hooks is null)
        {
            notes.Add("gameplay hooks: exact types are unavailable; skipped");
            return false;
        }

        MethodDefinition? Exact(TypeDefinition type, string name, int parameterCount) =>
            type.Methods.SingleOrDefault(m => m.Name == name && m.HasBody && !m.IsStatic &&
                m.Parameters.Count == parameterCount);
        MethodDefinition? Hook(string name, params string[] parameters) =>
            hooks.Methods.SingleOrDefault(m => m.Name == name && m.IsPublic && m.IsStatic &&
                m.ReturnType.MetadataType == MetadataType.Void &&
                m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(parameters));

        var takeDamage = Exact(hero, "TakeDamage", 4);
        var addGeo = Exact(hero, "AddGeo", 1);
        var addGeoQuietly = Exact(hero, "AddGeoQuietly", 1);
        var die = Exact(hero, "Die", 0);
        var setInt = Exact(player, "SetInt", 2);
        var beforeDamage = Hook("BeforeTakeDamage", "System.Int32&");
        var beforeGeo = Hook("BeforeAddGeo", "System.Int32&");
        var beforeJournal = Hook("BeforeJournalSetInt", "PlayerData", "System.String", "System.Int32&");
        var beforeDeath = Hook("BeforeDeath");
        if (takeDamage is null || takeDamage.Parameters[2].ParameterType.MetadataType != MetadataType.Int32 ||
            addGeo is null || addGeo.Parameters[0].ParameterType.MetadataType != MetadataType.Int32 ||
            addGeoQuietly is null || addGeoQuietly.Parameters[0].ParameterType.MetadataType != MetadataType.Int32 ||
            die is null || setInt is null ||
            setInt.Parameters[0].ParameterType.MetadataType != MetadataType.String ||
            setInt.Parameters[1].ParameterType.MetadataType != MetadataType.Int32 ||
            beforeDamage is null || beforeGeo is null || beforeJournal is null ||
            beforeDeath is null)
        {
            notes.Add("gameplay hooks: an exact target or patch signature is unavailable; skipped");
            return false;
        }

        bool Calls(MethodDefinition method, string hookName) => method.Body.Instructions.Any(i =>
            i.OpCode == OpCodes.Call && i.Operand is MethodReference called &&
            called.DeclaringType.FullName == hookTypeName && called.Name == hookName);
        bool already = Calls(takeDamage, "BeforeTakeDamage") && Calls(addGeo, "BeforeAddGeo") &&
            Calls(addGeoQuietly, "BeforeAddGeo") && Calls(setInt, "BeforeJournalSetInt") &&
            Calls(die, "BeforeDeath");
        if (already)
        {
            notes.Add("gameplay hooks: already woven; nothing to do");
            return false;
        }
        if (Calls(takeDamage, "BeforeTakeDamage") || Calls(addGeo, "BeforeAddGeo") ||
            Calls(addGeoQuietly, "BeforeAddGeo") || Calls(setInt, "BeforeJournalSetInt") ||
            Calls(die, "BeforeDeath"))
        {
            notes.Add("gameplay hooks: partial prior weave found; skipped");
            return false;
        }

        void Prefix(MethodDefinition target, MethodDefinition hook, params Instruction[] loads)
        {
            var processor = target.Body.GetILProcessor();
            var first = target.Body.Instructions[0];
            foreach (Instruction load in loads) processor.InsertBefore(first, load);
            processor.InsertBefore(first, processor.Create(OpCodes.Call, module.ImportReference(hook)));
        }

        Prefix(takeDamage, beforeDamage,
            Instruction.Create(OpCodes.Ldarga, takeDamage.Parameters[2]));
        Prefix(addGeo, beforeGeo,
            Instruction.Create(OpCodes.Ldarga, addGeo.Parameters[0]));
        Prefix(addGeoQuietly, beforeGeo,
            Instruction.Create(OpCodes.Ldarga, addGeoQuietly.Parameters[0]));
        Prefix(setInt, beforeJournal,
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldarg, setInt.Parameters[0]),
            Instruction.Create(OpCodes.Ldarga, setInt.Parameters[1]));
        Prefix(die, beforeDeath);
        notes.Add("gameplay hooks: damage cap, Geo multiplier, journal, and death handling woven");
        return true;
    }

    static bool WeaveSilksongGameplayHooks(
        AssemblyDefinition game, AssemblyDefinition patches, List<string> notes)
    {
        const string hookTypeName = "DualSouls.Mods.Silksong.SilksongGameplayHooks";
        var module = game.MainModule;
        var hooks = patches.MainModule.GetType(hookTypeName)
            ?? throw new InvalidOperationException("Silksong gameplay hook type is missing");

        TypeDefinition Type(string name) => module.GetType(name)
            ?? throw new InvalidOperationException("Silksong gameplay target type is missing: " + name);
        MethodDefinition Exact(TypeDefinition type, string name, bool isStatic,
            string returns, params string[] parameters)
        {
            var matches = type.Methods.Where(method => method.Name == name && method.HasBody &&
                method.IsStatic == isStatic && method.ReturnType.FullName == returns &&
                method.Parameters.Select(parameter => parameter.ParameterType.FullName)
                    .SequenceEqual(parameters)).ToList();
            return matches.Count == 1 ? matches[0] : throw new InvalidOperationException(
                $"Silksong gameplay target signature is missing or ambiguous: {type.FullName}.{name}");
        }
        MethodDefinition Hook(string name, string returns, params string[] parameters)
        {
            var matches = hooks.Methods.Where(method => method.Name == name && method.IsPublic &&
                method.IsStatic && method.ReturnType.FullName == returns &&
                method.Parameters.Select(parameter => parameter.ParameterType.FullName)
                    .SequenceEqual(parameters)).ToList();
            return matches.Count == 1 ? matches[0] : throw new InvalidOperationException(
                "Silksong gameplay patch signature is missing or ambiguous: " + name);
        }

        var health = Type("HealthManager");
        var hero = Type("HeroController");
        var currency = Type("CurrencyManager");
        var journal = Type("EnemyJournalManager");
        var tool = Type("ToolItem");
        var toolManager = Type("ToolItemManager");
        var player = Type("PlayerData");
        var gameMap = Type("GameMap");
        var crestSlot = Type("InventoryToolCrestSlot");
        var hit = Exact(health, "Hit", false, "IHitResponder/HitResponse", "HitInstance");
        var takeDamage = Exact(hero, "TakeDamage", false, "System.Void", "UnityEngine.GameObject",
            "GlobalEnums.CollisionSide", "System.Int32", "GlobalEnums.HazardType", "GlobalEnums.DamagePropertyFlags");
        var die = Exact(hero, "Die", false, "System.Collections.IEnumerator", "System.Boolean", "System.Boolean");
        var changeCurrency = Exact(currency, "ChangeCurrency", true, "System.Void",
            "System.Int32", "CurrencyType", "System.Boolean");
        var addGeoQuietly = Exact(currency, "AddGeoQuietly", true, "System.Void", "System.Int32");
        var recordKill = Exact(journal, "RecordKill", true, "System.Void",
            "EnemyJournalRecord", "System.Boolean", "System.Boolean");
        var isEquipped = Exact(tool, "get_IsEquipped", false, "System.Boolean");
        var replenish = Exact(toolManager, "TryReplenishTools", true, "System.Boolean",
            "System.Boolean", "ToolItemManager/ReplenishMethod");
        var setBool = Exact(player, "SetBool", false, "System.Void", "System.String", "System.Boolean");
        var addSilk = Exact(player, "AddSilk", false, "System.Boolean", "System.Int32");
        var addSilkParts = Exact(hero, "AddSilkParts", false, "System.Void", "System.Int32");
        var heroPlayerDataFields = hero.Fields.Where(field => field.Name == "playerData" &&
            !field.IsStatic && field.FieldType.FullName == "PlayerData").ToList();
        var heroPlayerData = heroPlayerDataFields.Count == 1 ? heroPlayerDataFields[0] :
            throw new InvalidOperationException("Silksong HeroController.playerData field is missing or ambiguous");
        var updateMap = Exact(gameMap, "UpdateGameMap", false, "System.Boolean");
        var setCrestSlot = Exact(crestSlot, "set_SaveData", false, "System.Void", "ToolCrestsData/SlotData");

        var beforeHit = Hook("BeforeEnemyHit", "System.Void", "HealthManager", "HitInstance&");
        var beforeDamage = Hook("BeforeHeroDamage", "System.Void", "System.Int32&");
        var beforeDeath = Hook("BeforeHeroDeath", "System.Void", "System.Boolean");
        var beforeCurrency = Hook("BeforeCurrencyChange", "System.Void", "System.Int32&", "CurrencyType");
        var beforeQuietGeo = Hook("BeforeAddGeoQuietly", "System.Void", "System.Int32&");
        var beforeJournal = Hook("BeforeJournalKill", "System.Void", "EnemyJournalRecord");
        var forceCompass = Hook("ShouldForceToolEquipped", "System.Boolean", "ToolItem");
        var freeCost = Hook("BeforeToolReplenishCost", "System.Void", "System.Single&");
        var authoritativeBool = Hook("BeforeAuthoritativeBoolSet", "System.Void",
            "PlayerData", "System.String", "System.Boolean");
        var beginSilkGrant = Hook("BeginAuthoritativeSilkGrant", "System.Void", "PlayerData");
        var endSilkGrant = Hook("EndAuthoritativeSilkGrant", "System.Void", "PlayerData");
        var beginSilkPartsGrant = Hook("BeginAuthoritativeSilkPartsGrant", "System.Void", "PlayerData");
        var endSilkPartsGrant = Hook("EndAuthoritativeSilkPartsGrant", "System.Void", "PlayerData");
        var beginMapUpdate = Hook("BeginAuthoritativeMapUpdate", "System.Void");
        var endMapUpdate = Hook("EndAuthoritativeMapUpdate", "System.Void");
        var authoritativeCrestSlot = Hook("BeforeAuthoritativeCrestSlotSet", "System.Void",
            "InventoryToolCrestSlot", "ToolCrestsData/SlotData");

        int mapReturnCount = updateMap.Body.Instructions.Count(i => i.OpCode == OpCodes.Ret);
        if (mapReturnCount == 0)
            throw new InvalidOperationException("Silksong GameMap.UpdateGameMap has no return instruction");
        if (addSilk.Body.Instructions.All(i => i.OpCode != OpCodes.Ret))
            throw new InvalidOperationException("Silksong PlayerData.AddSilk has no return instruction");
        if (addSilkParts.Body.Instructions.All(i => i.OpCode != OpCodes.Ret))
            throw new InvalidOperationException("Silksong HeroController.AddSilkParts has no return instruction");
        var expected = new[]
        {
            (hit, beforeHit, 1),
            (takeDamage, beforeDamage, 1), (die, beforeDeath, 1),
            (changeCurrency, beforeCurrency, 1), (addGeoQuietly, beforeQuietGeo, 1),
            (recordKill, beforeJournal, 1), (isEquipped, forceCompass, 1),
            (replenish, freeCost, 1), (setBool, authoritativeBool, 1),
            (addSilk, beginSilkGrant, 1), (addSilk, endSilkGrant, 1),
            (addSilkParts, beginSilkPartsGrant, 1), (addSilkParts, endSilkPartsGrant, 1),
            (updateMap, beginMapUpdate, 1), (updateMap, endMapUpdate, 1),
            (setCrestSlot, authoritativeCrestSlot, 1),
        };
        int CallCount(MethodDefinition target, MethodDefinition hook) => target.Body.Instructions.Count(i =>
            i.OpCode == OpCodes.Call && i.Operand is MethodReference called &&
            called.DeclaringType.FullName == hookTypeName && called.Name == hook.Name &&
            called.Parameters.Select(parameter => parameter.ParameterType.FullName)
                .SequenceEqual(hook.Parameters.Select(parameter => parameter.ParameterType.FullName)));
        int woven = expected.Count(pair => CallCount(pair.Item1, pair.Item2) == pair.Item3);
        bool malformed = expected.Any(pair => CallCount(pair.Item1, pair.Item2) > pair.Item3);
        if (woven == expected.Length && !malformed)
        {
            notes.Add("Silksong gameplay hooks: already woven; nothing to do");
            return false;
        }
        if (woven != 0 || malformed)
            throw new InvalidOperationException("Silksong gameplay hooks contain a partial prior weave");
        if (expected.Any(pair => pair.Item1.Body.Instructions.Any(i => i.OpCode == OpCodes.Call &&
            i.Operand is MethodReference called && called.DeclaringType.FullName == hookTypeName)))
            throw new InvalidOperationException("Silksong gameplay hooks contain an unknown prior weave");
        if (addSilk.Body.ExceptionHandlers.Count != 0 || addSilkParts.Body.ExceptionHandlers.Count != 0)
            throw new InvalidOperationException("Silksong Silk grant targets contain unsupported exception handlers");

        var costAnchors = replenish.Body.Instructions.Where(instruction =>
            instruction.OpCode.Code is Code.Stloc or Code.Stloc_S &&
            instruction.Operand is VariableDefinition && instruction.Previous?.OpCode == OpCodes.Mul &&
            instruction.Previous.Previous?.OpCode == OpCodes.Callvirt &&
            instruction.Previous.Previous.Operand is MethodReference called &&
            called.DeclaringType.FullName == "ToolItem" && called.Name == "get_ReplenishUsageMultiplier" &&
            called.ReturnType.MetadataType == MetadataType.Single && called.Parameters.Count == 0).ToList();
        if (costAnchors.Count != 1)
            throw new InvalidOperationException("Silksong tool replenishment cost anchor is missing or ambiguous");

        void Prefix(MethodDefinition target, MethodDefinition hook, params Instruction[] loads)
        {
            var processor = target.Body.GetILProcessor();
            var first = target.Body.Instructions[0];
            foreach (var load in loads) processor.InsertBefore(first, load);
            processor.InsertBefore(first, processor.Create(OpCodes.Call, module.ImportReference(hook)));
        }
        void GuardedFinally(MethodDefinition target, MethodDefinition hook, params Instruction[] loads)
        {
            var processor = target.Body.GetILProcessor();
            var tryStart = target.Body.Instructions[0];
            var originalReturns = target.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToList();
            VariableDefinition? result = null;
            if (target.ReturnType.MetadataType != MetadataType.Void)
            {
                target.Body.InitLocals = true;
                result = new VariableDefinition(module.ImportReference(target.ReturnType));
                target.Body.Variables.Add(result);
            }

            Instruction handlerStart = loads.Length == 0
                ? processor.Create(OpCodes.Call, module.ImportReference(hook))
                : loads[0];
            foreach (var load in loads) processor.Append(load);
            if (loads.Length == 0) processor.Append(handlerStart);
            else processor.Append(processor.Create(OpCodes.Call, module.ImportReference(hook)));
            processor.Append(processor.Create(OpCodes.Endfinally));
            Instruction continuation = result == null
                ? processor.Create(OpCodes.Ret)
                : processor.Create(OpCodes.Ldloc, result);
            processor.Append(continuation);
            if (result != null) processor.Append(processor.Create(OpCodes.Ret));

            foreach (var originalReturn in originalReturns)
            {
                if (result == null)
                {
                    originalReturn.OpCode = OpCodes.Leave;
                    originalReturn.Operand = continuation;
                }
                else
                {
                    originalReturn.OpCode = OpCodes.Stloc;
                    originalReturn.Operand = result;
                    processor.InsertAfter(originalReturn, processor.Create(OpCodes.Leave, continuation));
                }
            }
            target.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Finally)
            {
                TryStart = tryStart,
                TryEnd = handlerStart,
                HandlerStart = handlerStart,
                HandlerEnd = continuation,
            });
        }
        Prefix(hit, beforeHit, Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldarga, hit.Parameters[0]));
        Prefix(takeDamage, beforeDamage,
            Instruction.Create(OpCodes.Ldarga, takeDamage.Parameters[2]));
        Prefix(die, beforeDeath,
            Instruction.Create(OpCodes.Ldarg, die.Parameters[0]));
        Prefix(changeCurrency, beforeCurrency,
            Instruction.Create(OpCodes.Ldarga, changeCurrency.Parameters[0]),
            Instruction.Create(OpCodes.Ldarg, changeCurrency.Parameters[1]));
        Prefix(addGeoQuietly, beforeQuietGeo,
            Instruction.Create(OpCodes.Ldarga, addGeoQuietly.Parameters[0]));
        Prefix(recordKill, beforeJournal,
            Instruction.Create(OpCodes.Ldarg, recordKill.Parameters[0]));
        Prefix(setBool, authoritativeBool,
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldarg, setBool.Parameters[0]),
            Instruction.Create(OpCodes.Ldarg, setBool.Parameters[1]));
        GuardedFinally(addSilk, endSilkGrant, Instruction.Create(OpCodes.Ldarg_0));
        Prefix(addSilk, beginSilkGrant, Instruction.Create(OpCodes.Ldarg_0));
        GuardedFinally(addSilkParts, endSilkPartsGrant,
            Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldfld, heroPlayerData));
        Prefix(addSilkParts, beginSilkPartsGrant,
            Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldfld, heroPlayerData));
        Prefix(updateMap, beginMapUpdate);
        {
            updateMap.Body.InitLocals = true;
            var result = new VariableDefinition(module.TypeSystem.Boolean);
            updateMap.Body.Variables.Add(result);
            var processor = updateMap.Body.GetILProcessor();
            var returns = updateMap.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToList();
            var end = processor.Create(OpCodes.Call, module.ImportReference(endMapUpdate));
            processor.Append(end);
            processor.Append(processor.Create(OpCodes.Ldloc, result));
            processor.Append(processor.Create(OpCodes.Ret));
            foreach (var instruction in returns)
            {
                instruction.OpCode = OpCodes.Stloc;
                instruction.Operand = result;
                processor.InsertAfter(instruction, processor.Create(OpCodes.Br, end));
            }
        }
        Prefix(setCrestSlot, authoritativeCrestSlot,
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldarg, setCrestSlot.Parameters[0]));

        {
            var processor = isEquipped.Body.GetILProcessor();
            var first = isEquipped.Body.Instructions[0];
            processor.InsertBefore(first, processor.Create(OpCodes.Ldarg_0));
            processor.InsertBefore(first, processor.Create(OpCodes.Call, module.ImportReference(forceCompass)));
            processor.InsertBefore(first, processor.Create(OpCodes.Brfalse, first));
            processor.InsertBefore(first, processor.Create(OpCodes.Ldc_I4_1));
            processor.InsertBefore(first, processor.Create(OpCodes.Ret));
        }
        {
            var anchor = costAnchors[0];
            var variable = (VariableDefinition)anchor.Operand;
            var processor = replenish.Body.GetILProcessor();
            var load = processor.Create(OpCodes.Ldloca, variable);
            processor.InsertAfter(anchor, load);
            processor.InsertAfter(load, processor.Create(OpCodes.Call, module.ImportReference(freeCost)));
        }

        notes.Add("Silksong gameplay hooks: needle, damage cap, Silk grants, compass, tool costs, Rosaries, journal, and death handling woven");
        return true;
    }

    static bool IsLoad(Instruction instruction, float value) =>
        instruction.OpCode == OpCodes.Ldc_R4
        && instruction.Operand is float f
        && f.Equals(value);

    /// <summary>
    /// Injects <c>public static class SilksongAspectGate</c>.
    ///
    /// <code>
    ///     public static float Floor;                  // 0 until the game sets it
    ///     public static float Ceiling;
    ///     public static float GetFloor()              // what the woven calls read
    ///         => Floor   &lt; 1f ? 1.6f       : Floor;
    ///     public static float GetCeiling()
    ///         => Ceiling &lt; 1f ? 2.3916667f : Ceiling;
    /// </code>
    ///
    /// There is deliberately no static constructor. Fields that default to
    /// zero and getters that treat zero as "nobody said" need no
    /// initialisation order to be correct, and cannot be defeated by the game
    /// reading the value before our C# has run -- which, since this is read
    /// from Update, it otherwise could be.
    /// </summary>
    static TypeDefinition InjectGate(ModuleDefinition module)
    {
        var type = new TypeDefinition(
            "", GateType,
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed
                | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            module.TypeSystem.Object);

        AddGate(module, type, FloorField, FloorMethod, StockFloor);
        AddGate(module, type, CeilingField, CeilingMethod, StockCeiling);

        module.Types.Add(type);
        return type;
    }

    /// <summary>One field and the getter that defends it.</summary>
    static void AddGate(
        ModuleDefinition module, TypeDefinition type,
        string fieldName, string methodName, float stock)
    {
        var single = module.TypeSystem.Single;

        var field = new FieldDefinition(
            fieldName, FieldAttributes.Public | FieldAttributes.Static, single);
        type.Fields.Add(field);

        var getter = new MethodDefinition(
            methodName,
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            single);

        var il = getter.Body.GetILProcessor();
        var fallback = il.Create(OpCodes.Ldc_R4, stock);

        // Below 1.0 -- and, because blt.un is the unordered comparison, an
        // unset NaN too -- means nobody has answered; use the game's own. An
        // aspect ratio below 1.0 is a portrait window, which this must never
        // hand the game whatever it is asked for.
        il.Append(il.Create(OpCodes.Ldsfld, field));
        il.Append(il.Create(OpCodes.Ldc_R4, 1f));
        il.Append(il.Create(OpCodes.Blt_Un_S, fallback));
        il.Append(il.Create(OpCodes.Ldsfld, field));
        il.Append(il.Create(OpCodes.Ret));
        il.Append(fallback);
        il.Append(il.Create(OpCodes.Ret));

        type.Methods.Add(getter);
    }
}
