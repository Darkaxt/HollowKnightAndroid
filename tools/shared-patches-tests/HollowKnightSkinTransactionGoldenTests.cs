using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DualSouls.Skins.HollowKnight.Core;
using Xunit;

public sealed class HollowKnightSkinTransactionGoldenTests
{
    // Wire and event ceilings belong to this HARNESS, not the production reducer.
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private sealed class FixtureFormatError : Exception { public FixtureFormatError(string message) : base(message) { } }
    private sealed class OracleOutOfScope : Exception { }
    private static void Require(bool condition, string message = "Malformed transaction fixture") { if (!condition) throw new FixtureFormatError(message); }
    private static string Source() => ReadSource(Path.Combine(AppContext.BaseDirectory, "skin-goldens/v1/transactions.json"));
    // Private harness seam: Source and boundary regressions share this exact read/decode path.
    private static string ReadSource(string path, Func<string, byte[]> read = null)
    {
        try { return Utf8.GetString((read ?? File.ReadAllBytes)(path)); }
        catch (FileNotFoundException e) { throw new FixtureFormatError(e.Message); }
        catch (DirectoryNotFoundException e) { throw new FixtureFormatError(e.Message); }
        catch (DecoderFallbackException e) { throw new FixtureFormatError(e.Message); }
        // Permission failures and unexpected IO retain their original exception identity.
    }
    private static JsonNode Parse(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 32 });
            void Walk(JsonElement value)
            {
                if (value.ValueKind == JsonValueKind.Object)
                {
                    var keys = new HashSet<string>();
                    foreach (var p in value.EnumerateObject()) { Require(keys.Add(p.Name), "Duplicate decoded JSON key"); Walk(p.Value); }
                }
                else if (value.ValueKind == JsonValueKind.Array) foreach (var child in value.EnumerateArray()) Walk(child);
                else if (value.ValueKind == JsonValueKind.Number) Require(Regex.IsMatch(value.GetRawText(), @"\A(?:0|-?[1-9][0-9]*)\z"));
                else if (value.ValueKind == JsonValueKind.String) Utf8.GetBytes(value.GetString());
            }
            Walk(doc.RootElement); return JsonNode.Parse(text);
        }
        catch (Exception e) when (e is JsonException || e is EncoderFallbackException || e is InvalidOperationException) { throw new FixtureFormatError(e.Message); }
    }
    private static JsonObject Fields(JsonNode node, string names)
    {
        Require(node is JsonObject); var obj = (JsonObject)node;
        Require(obj.Select(p => p.Key).ToHashSet().SetEquals(names.Split(' '))); return obj;
    }
    private static string Text(JsonNode node)
    {
        Require(node is JsonValue v && v.GetValueKind() == JsonValueKind.String);
        var text = node.GetValue<string>(); Utf8.GetBytes(text); return text;
    }
    private static int Int(JsonNode node)
    {
        Require(node is JsonValue v && v.GetValueKind() == JsonValueKind.Number);
        Require(int.TryParse(node.ToJsonString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var result)); return result;
    }
    private static long Long(JsonNode node)
    {
        var text = Text(node); Require(Regex.IsMatch(text, @"\A(?:0|-?[1-9][0-9]*)\z"));
        Require(long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)); return value;
    }
    private static bool Bool(JsonNode node) { Require(node is JsonValue v && v.GetValueKind() is JsonValueKind.True or JsonValueKind.False); return node.GetValue<bool>(); }
    private static string OptionalText(JsonNode node) => node == null ? null : Text(node);
    private static SkinMode ReadSkinMode(JsonNode n) { var v = Text(n); Require("OFF ON ROTATE".Split(' ').Contains(v)); return Enum.Parse<SkinMode>(v); }
    private static SkinOperationKind ReadSkinOperationKind(JsonNode n) { var v = Text(n); Require("STARTUP_APPLY MODE_ON MODE_OFF DEATH_ROTATION REBIND_APPLY".Split(' ').Contains(v)); return Enum.Parse<SkinOperationKind>(v); }
    private static InterlockState ReadInterlockState(JsonNode n) { var v = Text(n); Require("CLEAR ARMED ROLLBACK_FAILED".Split(' ').Contains(v)); return Enum.Parse<InterlockState>(v); }
    private static TransactionPhase ReadTransactionPhase(JsonNode n) { var v = Text(n); Require("IDLE PREPARING PREPARED ARMED APPLIED ROLLBACK_PENDING ROLLED_BACK COMMITTED BLOCKED".Split(' ').Contains(v)); return Enum.Parse<TransactionPhase>(v); }
    private static SkinBindingToken ReadSkinBindingToken(JsonNode n) { var v = Fields(n, "value"); return new SkinBindingToken(Text(v["value"])); }
    private static JsonObject Project(SkinBindingToken v) => new JsonObject { ["value"] = v.Value };
    private static ActivationSnapshot ReadActivationSnapshot(JsonNode n) { var v = Fields(n, "mode selectedPackId active skinStamp"); return new ActivationSnapshot(ReadSkinMode(v["mode"]), (v["selectedPackId"] == null ? null : Text(v["selectedPackId"])), ReadActiveVisual(v["active"]), Long(v["skinStamp"])); }
    private static JsonObject Project(ActivationSnapshot v) => new JsonObject { ["mode"] = v.Mode.ToString(), ["selectedPackId"] = (v.SelectedPackId == null ? null : v.SelectedPackId), ["active"] = Project(v.Active), ["skinStamp"] = v.SkinStamp.ToString(CultureInfo.InvariantCulture) };
    private static TransactionEnvelope ReadTransactionEnvelope(JsonNode n) { var v = Fields(n, "transactionId operation baseGenerationId baseGenerationSha256 prior target binding priorEstablishedOnBinding"); return new TransactionEnvelope(Text(v["transactionId"]), ReadSkinOperationKind(v["operation"]), Text(v["baseGenerationId"]), Text(v["baseGenerationSha256"]), ReadActivationSnapshot(v["prior"]), ReadActivationSnapshot(v["target"]), ReadSkinBindingToken(v["binding"]), Bool(v["priorEstablishedOnBinding"])); }
    private static JsonObject Project(TransactionEnvelope v) => new JsonObject { ["transactionId"] = v.TransactionId, ["operation"] = v.Operation.ToString(), ["baseGenerationId"] = v.BaseGenerationId, ["baseGenerationSha256"] = v.BaseGenerationSha256, ["prior"] = Project(v.Prior), ["target"] = Project(v.Target), ["binding"] = Project(v.Binding), ["priorEstablishedOnBinding"] = v.PriorEstablishedOnBinding };
    private static TransactionCorrelation ReadTransactionCorrelation(JsonNode n) { var v = Fields(n, "transactionId binding"); return new TransactionCorrelation(Text(v["transactionId"]), ReadSkinBindingToken(v["binding"])); }
    private static JsonObject Project(TransactionCorrelation v) => new JsonObject { ["transactionId"] = v.TransactionId, ["binding"] = Project(v.Binding) };
    private static RegistryCommitReceipt ReadRegistryCommitReceipt(JsonNode n) { var v = Fields(n, "expectedGenerationId expectedGenerationSha256 newGenerationId newGenerationSha256"); return new RegistryCommitReceipt(Text(v["expectedGenerationId"]), Text(v["expectedGenerationSha256"]), Text(v["newGenerationId"]), Text(v["newGenerationSha256"])); }
    private static JsonObject Project(RegistryCommitReceipt v) => new JsonObject { ["expectedGenerationId"] = v.ExpectedGenerationId, ["expectedGenerationSha256"] = v.ExpectedGenerationSha256, ["newGenerationId"] = v.NewGenerationId, ["newGenerationSha256"] = v.NewGenerationSha256 };
    private static RotationInterlock ReadRotationInterlock(JsonNode n) { var v = Fields(n, "state transactionId operation baseGenerationId baseGenerationSha256 prior target bindingToken priorEstablishedOnBinding originalFailure rollbackFailure"); return new RotationInterlock(ReadInterlockState(v["state"]), (v["transactionId"] == null ? null : Text(v["transactionId"])), (v["operation"] == null ? null : (SkinOperationKind?)ReadSkinOperationKind(v["operation"])), (v["baseGenerationId"] == null ? null : Text(v["baseGenerationId"])), (v["baseGenerationSha256"] == null ? null : Text(v["baseGenerationSha256"])), (v["prior"] == null ? null : ReadActivationSnapshot(v["prior"])), (v["target"] == null ? null : ReadActivationSnapshot(v["target"])), (v["bindingToken"] == null ? null : ReadSkinBindingToken(v["bindingToken"])), (v["priorEstablishedOnBinding"] == null ? null : (bool?)Bool(v["priorEstablishedOnBinding"])), (v["originalFailure"] == null ? null : Text(v["originalFailure"])), (v["rollbackFailure"] == null ? null : Text(v["rollbackFailure"]))); }
    private static JsonObject Project(RotationInterlock v) => new JsonObject { ["state"] = v.State.ToString(), ["transactionId"] = (v.TransactionId == null ? null : v.TransactionId), ["operation"] = (v.Operation == null ? null : v.Operation.Value.ToString()), ["baseGenerationId"] = (v.BaseGenerationId == null ? null : v.BaseGenerationId), ["baseGenerationSha256"] = (v.BaseGenerationSha256 == null ? null : v.BaseGenerationSha256), ["prior"] = (v.Prior == null ? null : Project(v.Prior)), ["target"] = (v.Target == null ? null : Project(v.Target)), ["bindingToken"] = (v.BindingToken == null ? null : Project(v.BindingToken)), ["priorEstablishedOnBinding"] = (v.PriorEstablishedOnBinding == null ? null : v.PriorEstablishedOnBinding.Value), ["originalFailure"] = (v.OriginalFailure == null ? null : v.OriginalFailure), ["rollbackFailure"] = (v.RollbackFailure == null ? null : v.RollbackFailure) };
    private static VerifiedRegistryHead ReadVerifiedRegistryHead(JsonNode n) { var v = Fields(n, "generationId generationSha256 activation interlock"); return new VerifiedRegistryHead(Text(v["generationId"]), Text(v["generationSha256"]), ReadActivationSnapshot(v["activation"]), ReadRotationInterlock(v["interlock"])); }
    private static JsonObject Project(VerifiedRegistryHead v) => new JsonObject { ["generationId"] = v.GenerationId, ["generationSha256"] = v.GenerationSha256, ["activation"] = Project(v.Activation), ["interlock"] = Project(v.Interlock) };
    private static TransactionState ReadTransactionState(JsonNode n) { var v = Fields(n, "phase interlock binding armCommitReceipt pendingClosure envelope activation originalFailure rollbackFailure completionReceipt failureReceipt"); return new TransactionState(ReadTransactionPhase(v["phase"]), ReadRotationInterlock(v["interlock"]), (v["binding"] == null ? null : ReadSkinBindingToken(v["binding"])), (v["armCommitReceipt"] == null ? null : ReadRegistryCommitReceipt(v["armCommitReceipt"])), (v["pendingClosure"] == null ? null : ReadActivationSnapshot(v["pendingClosure"])), (v["envelope"] == null ? null : ReadTransactionEnvelope(v["envelope"])), (v["activation"] == null ? null : ReadActivationSnapshot(v["activation"])), (v["originalFailure"] == null ? null : Text(v["originalFailure"])), (v["rollbackFailure"] == null ? null : Text(v["rollbackFailure"])), (v["completionReceipt"] == null ? null : ReadRegistryCommitReceipt(v["completionReceipt"])), (v["failureReceipt"] == null ? null : ReadRegistryCommitReceipt(v["failureReceipt"]))); }
    private static JsonObject Project(TransactionState v) => new JsonObject { ["phase"] = v.Phase.ToString(), ["interlock"] = Project(v.Interlock), ["binding"] = (v.Binding == null ? null : Project(v.Binding)), ["armCommitReceipt"] = (v.ArmCommitReceipt == null ? null : Project(v.ArmCommitReceipt)), ["pendingClosure"] = (v.PendingClosure == null ? null : Project(v.PendingClosure)), ["envelope"] = (v.Envelope == null ? null : Project(v.Envelope)), ["activation"] = (v.Activation == null ? null : Project(v.Activation)), ["originalFailure"] = (v.OriginalFailure == null ? null : v.OriginalFailure), ["rollbackFailure"] = (v.RollbackFailure == null ? null : v.RollbackFailure), ["completionReceipt"] = (v.CompletionReceipt == null ? null : Project(v.CompletionReceipt)), ["failureReceipt"] = (v.FailureReceipt == null ? null : Project(v.FailureReceipt)) };
    private static ActiveVisual ReadActiveVisual(JsonNode n) { Require(n is JsonObject); var type = Text(n["type"]); switch(type) {
        case "Vanilla": Fields(n, "type"); return new ActiveVisual.Vanilla();
        case "Pack": Fields(n, "type id treeSha256 contentSha256 importReceiptSha256"); return new ActiveVisual.Pack(Text(n["id"]), Text(n["treeSha256"]), Text(n["contentSha256"]), Text(n["importReceiptSha256"]));
        default: throw new FixtureFormatError("Unknown discriminant"); } }
    private static JsonObject Project(ActiveVisual value) => value switch {
        ActiveVisual.Vanilla v => new JsonObject { ["type"] = "Vanilla" },
        ActiveVisual.Pack v => new JsonObject { ["type"] = "Pack", ["id"] = v.Id, ["treeSha256"] = v.TreeSha256, ["contentSha256"] = v.ContentSha256, ["importReceiptSha256"] = v.ImportReceiptSha256 },
        _ => throw new InvalidOperationException("Unhandled typed projection") };
    private static TransactionEvent ReadTransactionEvent(JsonNode n) { Require(n is JsonObject); var type = Text(n["type"]); switch(type) {
        case "Begin": Fields(n, "type envelope"); return new TransactionEvent.Begin(ReadTransactionEnvelope(n["envelope"]));
        case "Prepared": Fields(n, "type correlation"); return new TransactionEvent.Prepared(ReadTransactionCorrelation(n["correlation"]));
        case "ArmCommitted": Fields(n, "type correlation commitReceipt verifiedHead"); return new TransactionEvent.ArmCommitted(ReadTransactionCorrelation(n["correlation"]), ReadRegistryCommitReceipt(n["commitReceipt"]), ReadVerifiedRegistryHead(n["verifiedHead"]));
        case "ApplyVerified": Fields(n, "type correlation"); return new TransactionEvent.ApplyVerified(ReadTransactionCorrelation(n["correlation"]));
        case "ApplyFailed": Fields(n, "type correlation code"); return new TransactionEvent.ApplyFailed(ReadTransactionCorrelation(n["correlation"]), Text(n["code"]));
        case "RollbackVerified": Fields(n, "type correlation freshBinding"); return new TransactionEvent.RollbackVerified(ReadTransactionCorrelation(n["correlation"]), Bool(n["freshBinding"]));
        case "RollbackFailed": Fields(n, "type correlation code persistedFailureReceipt verifiedHead"); return new TransactionEvent.RollbackFailed(ReadTransactionCorrelation(n["correlation"]), Text(n["code"]), (n["persistedFailureReceipt"] == null ? null : ReadRegistryCommitReceipt(n["persistedFailureReceipt"])), (n["verifiedHead"] == null ? null : ReadVerifiedRegistryHead(n["verifiedHead"])));
        case "CompletionCommitted": Fields(n, "type correlation commitReceipt verifiedHead"); return new TransactionEvent.CompletionCommitted(ReadTransactionCorrelation(n["correlation"]), ReadRegistryCommitReceipt(n["commitReceipt"]), ReadVerifiedRegistryHead(n["verifiedHead"]));
        case "CompletionRejected": Fields(n, "type correlation code"); return new TransactionEvent.CompletionRejected(ReadTransactionCorrelation(n["correlation"]), Text(n["code"]));
        case "CompletionIndeterminate": Fields(n, "type correlation"); return new TransactionEvent.CompletionIndeterminate(ReadTransactionCorrelation(n["correlation"]));
        default: throw new FixtureFormatError("Unknown discriminant"); } }
    private static JsonObject Project(TransactionEvent value) => value switch {
        TransactionEvent.Begin v => new JsonObject { ["type"] = "Begin", ["envelope"] = Project(v.Envelope) },
        TransactionEvent.Prepared v => new JsonObject { ["type"] = "Prepared", ["correlation"] = Project(v.Correlation) },
        TransactionEvent.ArmCommitted v => new JsonObject { ["type"] = "ArmCommitted", ["correlation"] = Project(v.Correlation), ["commitReceipt"] = Project(v.CommitReceipt), ["verifiedHead"] = Project(v.VerifiedHead) },
        TransactionEvent.ApplyVerified v => new JsonObject { ["type"] = "ApplyVerified", ["correlation"] = Project(v.Correlation) },
        TransactionEvent.ApplyFailed v => new JsonObject { ["type"] = "ApplyFailed", ["correlation"] = Project(v.Correlation), ["code"] = v.Code },
        TransactionEvent.RollbackVerified v => new JsonObject { ["type"] = "RollbackVerified", ["correlation"] = Project(v.Correlation), ["freshBinding"] = v.FreshBinding },
        TransactionEvent.RollbackFailed v => new JsonObject { ["type"] = "RollbackFailed", ["correlation"] = Project(v.Correlation), ["code"] = v.Code, ["persistedFailureReceipt"] = (v.PersistedFailureReceipt == null ? null : Project(v.PersistedFailureReceipt)), ["verifiedHead"] = (v.VerifiedHead == null ? null : Project(v.VerifiedHead)) },
        TransactionEvent.CompletionCommitted v => new JsonObject { ["type"] = "CompletionCommitted", ["correlation"] = Project(v.Correlation), ["commitReceipt"] = Project(v.CommitReceipt), ["verifiedHead"] = Project(v.VerifiedHead) },
        TransactionEvent.CompletionRejected v => new JsonObject { ["type"] = "CompletionRejected", ["correlation"] = Project(v.Correlation), ["code"] = v.Code },
        TransactionEvent.CompletionIndeterminate v => new JsonObject { ["type"] = "CompletionIndeterminate", ["correlation"] = Project(v.Correlation) },
        _ => throw new InvalidOperationException("Unhandled typed projection") };
    private static SkinCommand ReadSkinCommand(JsonNode n) { Require(n is JsonObject); var type = Text(n["type"]); switch(type) {
        case "Prepare": Fields(n, "type envelope desired"); return new SkinCommand.Prepare(ReadTransactionEnvelope(n["envelope"]), ReadActiveVisual(n["desired"]));
        case "Arm": Fields(n, "type envelope"); return new SkinCommand.Arm(ReadTransactionEnvelope(n["envelope"]));
        case "Apply": Fields(n, "type correlation"); return new SkinCommand.Apply(ReadTransactionCorrelation(n["correlation"]));
        case "Rollback": Fields(n, "type correlation"); return new SkinCommand.Rollback(ReadTransactionCorrelation(n["correlation"]));
        case "Commit": Fields(n, "type correlation expectedGenerationId expectedGenerationSha256 closure"); return new SkinCommand.Commit(ReadTransactionCorrelation(n["correlation"]), Text(n["expectedGenerationId"]), Text(n["expectedGenerationSha256"]), ReadActivationSnapshot(n["closure"]));
        default: throw new FixtureFormatError("Unknown discriminant"); } }
    private static JsonObject Project(SkinCommand value) => value switch {
        SkinCommand.Prepare v => new JsonObject { ["type"] = "Prepare", ["envelope"] = Project(v.Envelope), ["desired"] = Project(v.Desired) },
        SkinCommand.Arm v => new JsonObject { ["type"] = "Arm", ["envelope"] = Project(v.Envelope) },
        SkinCommand.Apply v => new JsonObject { ["type"] = "Apply", ["correlation"] = Project(v.Correlation) },
        SkinCommand.Rollback v => new JsonObject { ["type"] = "Rollback", ["correlation"] = Project(v.Correlation) },
        SkinCommand.Commit v => new JsonObject { ["type"] = "Commit", ["correlation"] = Project(v.Correlation), ["expectedGenerationId"] = v.ExpectedGenerationId, ["expectedGenerationSha256"] = v.ExpectedGenerationSha256, ["closure"] = Project(v.Closure) },
        _ => throw new InvalidOperationException("Unhandled typed projection") };
    // Full recursive TYPE parsing precedes the cheap scope classifier and public reducer.
    private static string Sha(byte[] raw) => Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant();
    private static JsonObject Validate(JsonNode node)
    {
        var c = Fields(node, "caseId oracleContract input inputSha256 expected"); Require(Text(c["caseId"]).Length > 0); Text(c["oracleContract"]);
        Require(c["input"] is JsonObject); var input = c["input"].AsObject(); Require(input.Count == 1);
        byte[] raw;
        if (input.ContainsKey("utf8Text")) raw = Utf8.GetBytes(Text(input["utf8Text"]));
        else { Fields(input, "utf8Hex"); var hex = Text(input["utf8Hex"]); Require(Regex.IsMatch(hex, @"\A(?:[0-9a-f]{2})*\z")); raw = Convert.FromHexString(hex); }
        var hash = Text(c["inputSha256"]); Require(Regex.IsMatch(hash, @"\A[0-9a-f]{64}\z") && hash == Sha(raw));
        JsonObject data;
        try { data = Fields(Parse(Utf8.GetString(raw)), "initialState events"); }
        catch (DecoderFallbackException e) { throw new FixtureFormatError(e.Message); }
        var state = ReadTransactionState(data["initialState"]); Require(data["events"] is JsonArray); var events = data["events"].AsArray(); Require(events.Count > 0);
        foreach (var e in events) ReadTransactionEvent(e);
        Require(c["expected"] is JsonArray); var rows = c["expected"].AsArray(); Require(rows.Count == events.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            var row = Fields(rows[i], "step inputStateValid stateValid state diagnosis commands"); Require(Int(row["step"]) == i);
            Bool(row["inputStateValid"]); Bool(row["stateValid"]); ReadTransactionState(row["state"]); Text(row["diagnosis"]);
            Require(row["commands"] is JsonArray); foreach (var cmd in row["commands"].AsArray()) ReadSkinCommand(cmd);
        }
        var contract = Text(c["oracleContract"]);
        var phases = "IDLE PREPARING PREPARED ARMED APPLIED COMMITTED";
        var supportedEvents = "Begin Prepared ArmCommitted ApplyVerified CompletionCommitted";
        if (contract == "transaction-failure-blocked-v1") { phases += " ROLLBACK_PENDING ROLLED_BACK BLOCKED"; supportedEvents += " ApplyFailed RollbackVerified RollbackFailed CompletionRejected CompletionIndeterminate"; }
        else if (contract == "transaction-rollback-success-v1") { phases += " ROLLBACK_PENDING ROLLED_BACK"; supportedEvents += " ApplyFailed RollbackVerified"; }
        else if (contract != "transaction-forward-v1") throw new OracleOutOfScope();
        if (raw.Length > 65536 || events.Count > 32 || !phases.Split(' ').Contains(state.Phase.ToString()) || events.Any(e => !supportedEvents.Split(' ').Contains(Text(e["type"])))) throw new OracleOutOfScope();
        return data;
    }
    private static List<JsonObject> Load(string source)
    {
        var root = Fields(Parse(source), "schemaVersion cases"); Require(Int(root["schemaVersion"]) == 1); Require(root["cases"] is JsonArray);
        var result = new List<JsonObject>(); var ids = new HashSet<string>();
        foreach (var c in root["cases"].AsArray()) { Validate(c); Require(ids.Add(Text(c["caseId"]))); result.Add(c.AsObject()); }
        Require(result.Count > 0); return result;
    }
    private static void Verify(JsonObject c, Func<TransactionState, TransactionEvent, TransactionDecision> decide = null)
    {
        var data = Validate(c); var state = ReadTransactionState(data["initialState"]); var core = new SkinTransactionCore(); decide ??= core.Decide;
        var actual = new JsonArray();
        foreach (var e in data["events"].AsArray())
        {
            var before = core.IsStateValid(state); var d = decide(state, ReadTransactionEvent(e));
            var commands = new JsonArray(); foreach (var command in d.Commands) commands.Add(Project(command));
            actual.Add(new JsonObject { ["step"] = actual.Count, ["inputStateValid"] = before, ["stateValid"] = core.IsStateValid(d.State), ["state"] = Project(d.State), ["diagnosis"] = d.Diagnosis, ["commands"] = commands }); state = d.State;
        }
        Assert.True(JsonNode.DeepEquals(c["expected"], actual), Text(c["caseId"]) + " full ordered transaction log mismatch\n" + actual);
    }
    private static void Reinput(JsonObject c, JsonNode data)
    {
        var raw = Utf8.GetBytes(data.ToJsonString()); c["input"] = new JsonObject { ["utf8Text"] = Utf8.GetString(raw) }; c["inputSha256"] = Sha(raw);
    }
    [Fact] public void CorpusFullForwardLogs()
    {
        var cases = Load(Source()).Where(c => Text(c["oracleContract"]) == "transaction-forward-v1").ToList(); Assert.Equal(82, cases.Count); Assert.Equal(164, cases.Sum(c => c["expected"].AsArray().Count)); foreach (var c in cases) Verify(c);
    }
    [Fact] public void NoopAndInvalidSubstitutionsKillPositive()
    {
        var cases = Load(Source());
        foreach (var c in new[] { cases[0], cases.First(c => Text(c["caseId"]) == "rollback-history-unestablished-pack"), cases.First(c => Text(c["caseId"]) == "failure-history-reject-rollback-persisted"), cases.First(c => Text(c["caseId"]) == "failure-history-rolled-rejected"), cases.First(c => Text(c["caseId"]) == "failure-history-applied-indeterminate") })
            foreach (var diagnosis in new[] { "stale-phase", "invalid-state" })
                Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Verify(c, (s, e) => new TransactionDecision(s, diagnosis)));
    }
    [Fact] public void ReplayIsDeterministicAndInputImmutable()
    {
        foreach (var c in Load(Source())) { var before = c.ToJsonString(); Verify(c); Verify(c); Assert.Equal(before, c.ToJsonString()); }
    }
    private static IEnumerable<(string[] Path, JsonNode Value)> Leaves(JsonNode node, string[] path)
    {
        if (node is JsonObject o) foreach (var p in o) foreach (var leaf in Leaves(p.Value, path.Append(p.Key).ToArray())) yield return leaf;
        else if (node is JsonArray a) { for (var i = 0; i < a.Count; i++) foreach (var leaf in Leaves(a[i], path.Append(i.ToString(CultureInfo.InvariantCulture)).ToArray())) yield return leaf; }
        else yield return (path, node);
    }
    private static JsonNode Child(JsonNode n, string key) => n is JsonArray a ? a[int.Parse(key, CultureInfo.InvariantCulture)] : n[key];
    private static void Set(JsonNode n, string key, JsonNode value) { if (n is JsonArray a) a[int.Parse(key, CultureInfo.InvariantCulture)] = value; else n[key] = value; }
    [Fact] public void RecursiveStateCommandLeavesAndOrderAreObserved()
    {
        var cases = Load(Source());
        foreach (var c in new[] { cases[0], cases.First(c => Text(c["caseId"]) == "rollback-history-unestablished-pack"), cases.First(c => Text(c["caseId"]) == "failure-history-reject-rollback-persisted"), cases.First(c => Text(c["caseId"]) == "failure-history-rolled-rejected"), cases.First(c => Text(c["caseId"]) == "failure-history-applied-indeterminate") })
        {
        var pool = new Dictionary<string, JsonNode>();
        foreach (var row in cases.SelectMany(x => x["expected"].AsArray()))
        {
            void Collect(JsonNode n) { if (n is JsonObject o) foreach (var p in o) { if (p.Value != null) pool[p.Key] = p.Value.DeepClone(); Collect(p.Value); } else if (n is JsonArray a) foreach (var v in a) Collect(v); }
            Collect(row);
        }
        pool["originalFailure"] = JsonValue.Create("FAILURE"); pool["rollbackFailure"] = JsonValue.Create("ROLLBACK"); pool["failureReceipt"] = pool["completionReceipt"].DeepClone();
        foreach (var (path, value) in Leaves(c["expected"], Array.Empty<string>()))
        {
            var key = path.Last(); if (key == "step") continue;
            var bad = c.DeepClone().AsObject(); JsonNode part = bad["expected"]; foreach (var k in path.SkipLast(1)) part = Child(part, k);
            if (key == "type")
            {
                // The discriminant is not an arbitrary string: change the complete union to another representable subtype.
                JsonNode replacement = Text(value) switch { "Vanilla" => new JsonObject { ["type"] = "Pack", ["id"] = "changed", ["treeSha256"] = "x", ["contentSha256"] = "y", ["importReceiptSha256"] = "z" }, "Pack" => new JsonObject { ["type"] = "Vanilla" }, "Apply" => new JsonObject { ["type"] = "Rollback", ["correlation"] = part["correlation"].DeepClone() }, _ => new JsonObject { ["type"] = "Apply", ["correlation"] = pool["correlation"].DeepClone() } };
                JsonNode parent = bad["expected"]; foreach (var k in path.SkipLast(2)) parent = Child(parent, k); Set(parent, path[^2], replacement);
            }
            else
            {
                JsonNode replacement = value == null ? pool[key].DeepClone() : key switch {
                    "phase" => JsonValue.Create(Text(value) == "IDLE" ? "ARMED" : "IDLE"),
                    "state" => JsonValue.Create(Text(value) == "CLEAR" ? "ARMED" : "CLEAR"),
                    "mode" => JsonValue.Create(Text(value) == "OFF" ? "ON" : "OFF"),
                    "operation" => JsonValue.Create(Text(value) == "MODE_ON" ? "MODE_OFF" : "MODE_ON"),
                    "skinStamp" => JsonValue.Create(Text(value) == "7" ? "8" : "7"),
                    _ => value.GetValueKind() is JsonValueKind.True or JsonValueKind.False ? JsonValue.Create(!Bool(value)) : JsonValue.Create(Text(value) + "changed") };
                Set(part, key, replacement);
            }
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Verify(bad));
        }
        for (var i = 0; i < c["expected"].AsArray().Count; i++)
        {
            var omitted = c.DeepClone().AsObject(); omitted["expected"].AsArray().RemoveAt(i); Assert.Throws<FixtureFormatError>(() => Verify(omitted));
            if (c["expected"][i]["commands"].AsArray().Count > 0) { var bad = c.DeepClone().AsObject(); bad["expected"][i]["commands"] = new JsonArray(); Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Verify(bad)); }
        }
        var reordered = c.DeepClone().AsObject(); var rows = reordered["expected"].AsArray(); var first = rows[0].DeepClone(); rows[0] = rows[1].DeepClone(); rows[1] = first; for (var i = 0; i < rows.Count; i++) rows[i]["step"] = i;
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Verify(reordered));
        }
    }
    [Fact] public void FailureContractBoundsAndCompleteRepresentationPrecedeReducer()
    {
        var cases = Load(Source()).Where(c => Text(c["oracleContract"]) == "transaction-failure-blocked-v1").ToList();
        foreach (var c in cases) foreach (var old in new[] { "transaction-forward-v1", "transaction-rollback-success-v1" })
        {
            var excluded = c.DeepClone().AsObject(); excluded["oracleContract"] = old;
            var called = false;
            Assert.Throws<OracleOutOfScope>(() => Verify(excluded, (s, e) => { called = true; return new SkinTransactionCore().Decide(s, e); }));
            Assert.False(called);
        }
        foreach (var mode in new[] { "contract", "bytes", "events" })
        {
            var c = cases[0].DeepClone().AsObject(); var data = Validate(c);
            if (mode == "contract") c["oracleContract"] = "future";
            if (mode == "events") while (data["events"].AsArray().Count < 33)
            {
                var i = data["events"].AsArray().Count; data["events"].AsArray().Add(data["events"][0].DeepClone());
                var row = c["expected"][0].DeepClone(); row["step"] = i; c["expected"].AsArray().Add(row);
            }
            Reinput(c, data);
            if (mode == "bytes") { var raw = Utf8.GetBytes(Text(c["input"]["utf8Text"]) + new string(' ', 65537)); c["input"]["utf8Text"] = Utf8.GetString(raw); c["inputSha256"] = Sha(raw); }
            var called = false;
            Assert.Throws<OracleOutOfScope>(() => Verify(c, (s, e) => { called = true; return new SkinTransactionCore().Decide(s, e); })); Assert.False(called);
            c["expected"][0]["state"]["activation"]["skinStamp"] = "01";
            Assert.Throws<FixtureFormatError>(() => Verify(c));
        }
        foreach (var field in new[] { "freshBinding", "priorEstablishedOnBinding" })
        {
            var c = cases[0].DeepClone().AsObject(); var data = Validate(c);
            if (field == "freshBinding") data["events"][5][field] = 1; else data["events"][0]["envelope"][field] = 1;
            Reinput(c, data); Assert.Throws<FixtureFormatError>(() => Verify(c));
        }
    }
    [Fact] public void CorpusFailureBlockedLogs()
    {
        var cases = Load(Source()).Where(c => Text(c["oracleContract"]) == "transaction-failure-blocked-v1").ToList();
        Assert.Equal(23, cases.Count); Assert.Equal(136, cases.Sum(c => c["expected"].AsArray().Count));
        foreach (var c in cases) Verify(c);
    }
    [Fact] public void CorpusSuccessfulRollbackLogs()
    {
        var cases = Load(Source()).Where(c => Text(c["oracleContract"]) == "transaction-rollback-success-v1").ToList();
        Assert.Equal(33, cases.Count); Assert.Equal(77, cases.Sum(c => c["expected"].AsArray().Count));
        foreach (var c in cases) Verify(c);
    }
    [Fact] public void RollbackContractStillExcludesFailureAndBlocked()
    {
        var baseline = Load(Source()).First(c => Text(c["caseId"]) == "rollback-history-unestablished-pack");
        foreach (var mode in new[] { "RollbackFailed", "CompletionRejected", "CompletionIndeterminate", "BLOCKED", "contract", "bytes", "events" })
        {
            var c = baseline.DeepClone().AsObject(); var data = Validate(c);
            if (mode == "BLOCKED") { data["initialState"]["phase"] = mode; data["initialState"]["binding"] = null; }
            else if (mode == "contract") c["oracleContract"] = "future";
            else if (mode == "events") { while (data["events"].AsArray().Count < 33) { var i = data["events"].AsArray().Count; data["events"].AsArray().Add(data["events"][0].DeepClone()); var row = c["expected"][0].DeepClone(); row["step"] = i; c["expected"].AsArray().Add(row); } }
            else if (mode != "bytes")
            {
                var ev = new JsonObject { ["type"] = mode, ["correlation"] = data["events"][3]["correlation"].DeepClone() };
                if (mode != "CompletionIndeterminate") ev["code"] = "invalid-code";
                if (mode == "RollbackFailed") { ev["persistedFailureReceipt"] = null; ev["verifiedHead"] = null; }
                data["events"][0] = ev;
            }
            Reinput(c, data);
            if (mode == "bytes") { var raw = Utf8.GetBytes(Text(c["input"]["utf8Text"]) + new string(' ', 65537)); c["input"]["utf8Text"] = Utf8.GetString(raw); c["inputSha256"] = Sha(raw); }
            var called = false; Assert.Throws<OracleOutOfScope>(() => Verify(c, (s, e) => { called = true; return new SkinTransactionCore().Decide(s, e); })); Assert.False(called);
            data["initialState"]["activation"]["skinStamp"] = "01"; Reinput(c, data);
            Assert.Throws<FixtureFormatError>(() => Verify(c));
        }
    }
    [Fact] public void MalformedRepresentationPrecedesScopeAndReducer()
    {
        var missing = Guid.NewGuid().ToString("N");
        // Default reader exercises real missing file/path errors; injected bytes exercise strict decode.
        var missingErrors = new[] { Path.Combine(AppContext.BaseDirectory, missing + ".json"), Path.Combine(AppContext.BaseDirectory, missing, "missing.json") }
            .Select(path => Record.Exception(() => ReadSource(path))).ToList();
        missingErrors.Add(Record.Exception(() => ReadSource("isolated-bytes", _ => new byte[] { 255 })));
        Assert.All(missingErrors, error => Assert.IsType<FixtureFormatError>(error));
        var corpusBytes = Utf8.GetBytes(Source());
        Assert.Equal(Source(), ReadSource("isolated-positive", _ => corpusBytes));
        foreach (var error in new Exception[] { new IOException("unexpected read failure"), new UnauthorizedAccessException("permission denied") })
            Assert.Same(error, Record.Exception(() => ReadSource("isolated-io", _ => throw error)));
        foreach (var raw in new[] { "", "{}", "{\"schemaVersion\":1,\"cases\":[]}", "{\"schemaVersion\":1,\"schemaVersion\":1,\"cases\":[]}" }) Assert.Throws<FixtureFormatError>(() => Load(raw));
        var root = Parse(Source()); root["cases"].AsArray().Add(root["cases"][0].DeepClone()); Assert.Throws<FixtureFormatError>(() => Load(root.ToJsonString()));
        var c = Load(Source())[0];
        foreach (var stamp in new[] { "01", "+1", "-0", "9223372036854775808", "-9223372036854775809" })
        {
            var bad = c.DeepClone().AsObject(); var d = Validate(bad); d["initialState"]["activation"]["skinStamp"] = stamp; d["initialState"]["phase"] = "BLOCKED"; Reinput(bad, d);
            Assert.Throws<FixtureFormatError>(() => Verify(bad));
        }
        foreach (var change in new[] { "hash", "fields", "null", "enum", "bool", "duplicate", "unicode", "utf8", "expected" })
        {
            var bad = c.DeepClone().AsObject(); var d = Validate(bad);
            if (change == "hash") bad["inputSha256"] = new string('0', 64);
            else if (change == "expected") bad["expected"][0]["commands"][0]["desired"]["unknown"] = 0;
            else if (change == "duplicate" || change == "unicode" || change == "utf8")
            {
                var text = change == "duplicate" ? "{\"initialState\":0,\"initial\\u0053tate\":0,\"events\":[]}" : "{\"x\":\"\\ud800\"}";
                var raw = change == "utf8" ? new byte[] { 255 } : Utf8.GetBytes(text); bad["input"] = new JsonObject { ["utf8Hex"] = Convert.ToHexString(raw).ToLowerInvariant() }; bad["inputSha256"] = Sha(raw);
            }
            else { if (change == "fields") d["initialState"].AsObject().Remove("failureReceipt"); if (change == "null") d["events"][0]["envelope"] = null; if (change == "enum") d["initialState"]["phase"] = "UNKNOWN"; if (change == "bool") d["events"][0]["envelope"]["priorEstablishedOnBinding"] = 1; Reinput(bad, d); }
            var called = false; Assert.Throws<FixtureFormatError>(() => Verify(bad, (s, e) => { called = true; return new SkinTransactionCore().Decide(s, e); })); Assert.False(called);
        }
    }
    [Fact] public void ExcludedKnownTypesAndPhasesAreAlwaysScope()
    {
        var baseline = Load(Source())[0]; var correlation = Validate(baseline)["events"][1]["correlation"];
        var excluded = new[] { "ApplyFailed", "RollbackVerified", "RollbackFailed", "CompletionRejected", "CompletionIndeterminate" };
        foreach (var mode in excluded.Concat(new[] { "ROLLBACK_PENDING", "ROLLED_BACK", "BLOCKED", "contract", "bytes", "events" }))
        {
            var c = baseline.DeepClone().AsObject(); var d = Validate(c);
            if (excluded.Contains(mode))
            {
                var e = new JsonObject { ["type"] = mode, ["correlation"] = correlation.DeepClone() };
                if (mode is "ApplyFailed" or "RollbackFailed" or "CompletionRejected") e["code"] = "bad-code-is-still-representable";
                if (mode == "RollbackVerified") e["freshBinding"] = false;
                if (mode == "RollbackFailed") { e["persistedFailureReceipt"] = null; e["verifiedHead"] = null; }
                d["events"][0] = e;
            }
            else if (mode == "contract") c["oracleContract"] = "future";
            else if (mode == "events") { while (d["events"].AsArray().Count < 33) { var i = d["events"].AsArray().Count; d["events"].AsArray().Add(d["events"][0].DeepClone()); var row = c["expected"][0].DeepClone(); row["step"] = i; c["expected"].AsArray().Add(row); } }
            else if (mode != "bytes") { d["initialState"]["phase"] = mode; d["initialState"]["binding"] = null; }
            Reinput(c, d);
            if (mode == "bytes") { var raw = Utf8.GetBytes(Text(c["input"]["utf8Text"]) + new string(' ', 65537)); c["input"]["utf8Text"] = Utf8.GetString(raw); c["inputSha256"] = Sha(raw); }
            var called = false; Assert.Throws<OracleOutOfScope>(() => Verify(c, (s, e) => { called = true; return new SkinTransactionCore().Decide(s, e); })); Assert.False(called);
            // An excluded recognized event never masks a malformed nested representation.
            if (excluded.Contains(mode)) { d["events"][0]["correlation"]["binding"]["value"] = 4; Reinput(c, d); Assert.Throws<FixtureFormatError>(() => Verify(c)); }
        }
    }
}
