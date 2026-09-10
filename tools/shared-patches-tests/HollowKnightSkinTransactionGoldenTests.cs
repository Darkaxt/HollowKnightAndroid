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
        var cases = Load(Source()).Take(262).Where(c => Text(c["oracleContract"]) == "transaction-forward-v1").ToList(); Assert.Equal(82, cases.Count); Assert.Equal(164, cases.Sum(c => c["expected"].AsArray().Count)); foreach (var c in cases) Verify(c);
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
        foreach (var c in new[] { cases[0], cases.First(c => Text(c["caseId"]) == "rollback-history-unestablished-pack"), cases.First(c => Text(c["caseId"]) == "failure-history-reject-rollback-persisted"), cases.First(c => Text(c["caseId"]) == "failure-history-rolled-rejected"), cases.First(c => Text(c["caseId"]) == "failure-history-applied-indeterminate"), cases.First(c => Text(c["caseId"]) == "failure-proof-missing-head-repair"), cases.First(c => Text(c["caseId"]) == "failure-proof-blocked-target-closure-negative"), cases.First(c => Text(c["caseId"]) == "failure-proof-blocked-prior-closure-repair") })
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
        // Identical terminal/invalid observations cannot demonstrate order sensitivity.
        if (!JsonNode.DeepEquals(c["expected"], reordered["expected"])) Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Verify(reordered));
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
        // Original 23-case partition is also pinned byte-for-byte by the proof corpus test.
        var cases = Load(Source()).Take(138).Where(c => Text(c["oracleContract"]) == "transaction-failure-blocked-v1").ToList();
        Assert.Equal(23, cases.Count); Assert.Equal(136, cases.Sum(c => c["expected"].AsArray().Count));
        foreach (var c in cases) Verify(c);
    }
    [Fact] public void CorpusFailureProofAndBlockedValidatorLogs()
    {
        var raw = Utf8.GetBytes(Source());
        Assert.Equal("ce51b6aed12113dbdf457edaa8c0518b4ae668883978c541392d886dcbe4b482", Sha(raw.Take(2373974).ToArray()));
        var all = Load(Source()).Take(262).ToList(); Assert.Equal(262, all.Count);
        var cases = all.Where(c => Text(c["caseId"]).StartsWith("failure-proof-", StringComparison.Ordinal)).ToList();
        Assert.Equal(124, cases.Count); Assert.Equal(248, cases.Sum(c => c["expected"].AsArray().Count));
        foreach (var c in cases) { Assert.Equal("transaction-failure-blocked-v1", Text(c["oracleContract"])); Verify(c); }
    }
    [Fact] public void FailureProofRepairedPositivesKillSubstitutions()
    {
        foreach (var c in Load(Source()).Where(c => Text(c["caseId"]).StartsWith("failure-proof-", StringComparison.Ordinal) && Text(c["caseId"]).EndsWith("-repair", StringComparison.Ordinal)))
            foreach (var diagnosis in new[] { "stale-phase", "invalid-state" })
                Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Verify(c, (s, e) => new TransactionDecision(s, diagnosis)));
    }
    [Fact] public void CorpusSuccessfulRollbackLogs()
    {
        var cases = Load(Source()).Take(262).Where(c => Text(c["oracleContract"]) == "transaction-rollback-success-v1").ToList();
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

    // Task87: literal canonical witnesses; contract admission alone is not coverage.
    private const string DispatchMap = @"IDLE|Begin|prepare|mode-on-zero|0
IDLE|Prepared|stale-correlation|wrong-correlation-idle|0
IDLE|ArmCommitted|stale-correlation|dispatch-idle-armcommitted|0
IDLE|ApplyVerified|stale-correlation|dispatch-idle-applyverified|0
IDLE|ApplyFailed|stale-correlation|dispatch-idle-applyfailed|0
IDLE|RollbackVerified|stale-correlation|dispatch-idle-rollbackverified|0
IDLE|RollbackFailed|stale-correlation|dispatch-idle-rollbackfailed|0
IDLE|CompletionCommitted|stale-correlation|dispatch-idle-completioncommitted|0
IDLE|CompletionRejected|stale-correlation|dispatch-idle-completionrejected|0
IDLE|CompletionIndeterminate|stale-correlation|dispatch-idle-completionindeterminate|0
PREPARING|Begin|transaction-in-progress|repair-state-preparing-arm|0
PREPARING|Prepared|arm|mode-on-zero|1
PREPARING|ArmCommitted|stale-phase|dispatch-preparing-armcommitted|0
PREPARING|ApplyVerified|stale-phase|wrong-phase-preparing|0
PREPARING|ApplyFailed|stale-phase|dispatch-preparing-applyfailed|0
PREPARING|RollbackVerified|stale-phase|dispatch-preparing-rollbackverified|0
PREPARING|RollbackFailed|stale-phase|dispatch-preparing-rollbackfailed|0
PREPARING|CompletionCommitted|stale-phase|dispatch-preparing-completioncommitted|0
PREPARING|CompletionRejected|stale-phase|dispatch-preparing-completionrejected|0
PREPARING|CompletionIndeterminate|stale-phase|dispatch-preparing-completionindeterminate|0
PREPARED|Begin|transaction-in-progress|begin-in-progress|0
PREPARED|Prepared|stale-phase|wrong-phase-prepared|0
PREPARED|ArmCommitted|apply|mode-on-zero|2
PREPARED|ApplyVerified|stale-phase|dispatch-prepared-applyverified|0
PREPARED|ApplyFailed|stale-phase|rollback-phase-applyfailed-prepared|0
PREPARED|RollbackVerified|stale-phase|dispatch-prepared-rollbackverified|0
PREPARED|RollbackFailed|stale-phase|dispatch-prepared-rollbackfailed|0
PREPARED|CompletionCommitted|stale-phase|dispatch-prepared-completioncommitted|0
PREPARED|CompletionRejected|stale-phase|dispatch-prepared-completionrejected|0
PREPARED|CompletionIndeterminate|stale-phase|dispatch-prepared-completionindeterminate|0
ARMED|Begin|transaction-in-progress|repair-state-armed-clear|0
ARMED|Prepared|stale-phase|dispatch-armed-prepared|0
ARMED|ArmCommitted|stale-phase|wrong-phase-armed|0
ARMED|ApplyVerified|commit-closure|mode-on-zero|3
ARMED|ApplyFailed|rollback|rollback-history-established-pack|3
ARMED|RollbackVerified|stale-phase|rollback-phase-rollbackverified-armed|0
ARMED|RollbackFailed|stale-phase|dispatch-armed-rollbackfailed|0
ARMED|CompletionCommitted|stale-phase|dispatch-armed-completioncommitted|0
ARMED|CompletionRejected|stale-phase|dispatch-armed-completionrejected|0
ARMED|CompletionIndeterminate|stale-phase|dispatch-armed-completionindeterminate|0
APPLIED|Begin|transaction-in-progress|repair-state-applied-no-closure|0
APPLIED|Prepared|stale-phase|dispatch-applied-prepared|0
APPLIED|ArmCommitted|stale-phase|dispatch-applied-armcommitted|0
APPLIED|ApplyVerified|stale-phase|wrong-phase-applied|0
APPLIED|ApplyFailed|stale-phase|rollback-phase-applyfailed-applied|0
APPLIED|RollbackVerified|stale-phase|dispatch-applied-rollbackverified|0
APPLIED|RollbackFailed|stale-phase|dispatch-applied-rollbackfailed|0
APPLIED|CompletionCommitted|committed|mode-on-zero|4
APPLIED|CompletionRejected|rollback|failure-history-reject-restored-established-pack|4
APPLIED|CompletionIndeterminate|completion-indeterminate|failure-history-applied-indeterminate|4
ROLLBACK_PENDING|Begin|transaction-in-progress|dispatch-rollback-pending-begin|0
ROLLBACK_PENDING|Prepared|stale-phase|dispatch-rollback-pending-prepared|0
ROLLBACK_PENDING|ArmCommitted|stale-phase|dispatch-rollback-pending-armcommitted|0
ROLLBACK_PENDING|ApplyVerified|stale-phase|dispatch-rollback-pending-applyverified|0
ROLLBACK_PENDING|ApplyFailed|stale-phase|dispatch-rollback-pending-applyfailed|0
ROLLBACK_PENDING|RollbackVerified|commit-closure|rollback-history-established-pack|4
ROLLBACK_PENDING|RollbackFailed|rollback-failed|failure-history-reject-rollback-persisted|5
ROLLBACK_PENDING|CompletionCommitted|stale-phase|dispatch-rollback-pending-completioncommitted|0
ROLLBACK_PENDING|CompletionRejected|stale-phase|dispatch-rollback-pending-completionrejected|0
ROLLBACK_PENDING|CompletionIndeterminate|stale-phase|dispatch-rollback-pending-completionindeterminate|0
ROLLED_BACK|Begin|transaction-in-progress|dispatch-rolled-back-begin|0
ROLLED_BACK|Prepared|stale-phase|dispatch-rolled-back-prepared|0
ROLLED_BACK|ArmCommitted|stale-phase|dispatch-rolled-back-armcommitted|0
ROLLED_BACK|ApplyVerified|stale-phase|dispatch-rolled-back-applyverified|0
ROLLED_BACK|ApplyFailed|stale-phase|dispatch-rolled-back-applyfailed|0
ROLLED_BACK|RollbackVerified|stale-phase|rollback-phase-rollbackverified-rolled|0
ROLLED_BACK|RollbackFailed|stale-phase|dispatch-rolled-back-rollbackfailed|0
ROLLED_BACK|CompletionCommitted|committed|rollback-history-established-pack|5
ROLLED_BACK|CompletionRejected|rollback-closure-rejected|failure-history-rolled-rejected|6
ROLLED_BACK|CompletionIndeterminate|completion-indeterminate|failure-history-rolled-indeterminate|6
COMMITTED|Begin|terminal|committed-original-failure-valid|0
COMMITTED|Prepared|terminal|dispatch-committed-prepared|0
COMMITTED|ArmCommitted|terminal|dispatch-committed-armcommitted|0
COMMITTED|ApplyVerified|terminal|dispatch-committed-applyverified|0
COMMITTED|ApplyFailed|terminal|dispatch-committed-applyfailed|0
COMMITTED|RollbackVerified|terminal|dispatch-committed-rollbackverified|0
COMMITTED|RollbackFailed|terminal|dispatch-committed-rollbackfailed|0
COMMITTED|CompletionCommitted|terminal|mode-on-zero|5
COMMITTED|CompletionRejected|terminal|dispatch-committed-completionrejected|0
COMMITTED|CompletionIndeterminate|terminal|dispatch-committed-completionindeterminate|0
BLOCKED|Begin|terminal|failure-terminal-persisted|0
BLOCKED|Prepared|terminal|failure-terminal-persisted|2
BLOCKED|ArmCommitted|terminal|dispatch-blocked-armcommitted|0
BLOCKED|ApplyVerified|terminal|dispatch-blocked-applyverified|0
BLOCKED|ApplyFailed|terminal|failure-terminal-persisted|3
BLOCKED|RollbackVerified|terminal|dispatch-blocked-rollbackverified|0
BLOCKED|RollbackFailed|terminal|failure-terminal-persisted|4
BLOCKED|CompletionCommitted|terminal|dispatch-blocked-completioncommitted|0
BLOCKED|CompletionRejected|terminal|failure-terminal-persisted|5
BLOCKED|CompletionIndeterminate|terminal|failure-terminal-persisted|6";
    [Fact] public void CorpusCanonicalDispatchLogs()
    {
        var all = Load(Source()).Take(318).ToList(); Assert.Equal(318, all.Count);
        Assert.Equal("a898f1621701a4617279e483255de89089d2e9fc270852a399eb23e8a2b33839", Sha(Utf8.GetBytes(Source()).Take(4632889).ToArray()));
        var added = all.Skip(262).ToList(); Assert.Equal(56, added.Count);
        Assert.Equal(681, all.Sum(c => c["expected"].AsArray().Count));
        foreach (var c in added) {
            var before = c.ToJsonString(); var data = Validate(c);
            Assert.Single(data["events"].AsArray()); Assert.Single(c["expected"].AsArray());
            Assert.True(Bool(c["expected"][0]["inputStateValid"])); Assert.True(Bool(c["expected"][0]["stateValid"]));
            Assert.True(JsonNode.DeepEquals(data["initialState"], c["expected"][0]["state"])); Assert.Empty(c["expected"][0]["commands"].AsArray());
            Verify(c); Verify(c); Assert.Equal(before, c.ToJsonString());
        }
        var cells = new HashSet<string>(); var actionCells = 0;
        foreach (var line in DispatchMap.Split((char)10)) {
            var p = line.Trim().Split('|'); Assert.True(cells.Add(p[0] + "|" + p[1]));
            var c = all.Single(c => Text(c["caseId"]) == p[3]); var data = Validate(c); var step = int.Parse(p[4], CultureInfo.InvariantCulture);
            var input = step == 0 ? data["initialState"] : c["expected"][step - 1]["state"]; var row = c["expected"][step];
            Assert.Equal(p[0], Text(input["phase"])); Assert.Equal(p[1], Text(data["events"][step]["type"]));
            Assert.True(Bool(row["inputStateValid"])); Assert.True(Bool(row["stateValid"])); Assert.Equal(p[2], Text(row["diagnosis"]));
            Verify(c); // Full real replay protects the precise witness row and command payloads.
            if (!new[] { "terminal", "stale-phase", "stale-correlation", "transaction-in-progress" }.Contains(p[2])) {
                actionCells++; Assert.False(JsonNode.DeepEquals(input, row["state"]));
            }
        }
        Assert.Equal(90, cells.Count); Assert.Equal(13, actionCells);
        foreach (var phase in "IDLE PREPARING PREPARED ARMED APPLIED ROLLBACK_PENDING ROLLED_BACK COMMITTED BLOCKED".Split(' '))
            foreach (var ev in "Begin Prepared ArmCommitted ApplyVerified ApplyFailed RollbackVerified RollbackFailed CompletionCommitted CompletionRejected CompletionIndeterminate".Split(' ')) Assert.Contains(phase + "|" + ev, cells);
        foreach (var pair in new[] { ("transaction-forward-v1",14), ("transaction-rollback-success-v1",19), ("transaction-failure-blocked-v1",23) })
            Assert.Equal(pair.Item2, added.Count(c => Text(c["oracleContract"]) == pair.Item1));
    }
    [Fact] public void DispatchObserverDiagnosisPhaseAndDefaultSubstitutions()
    {
        foreach (var c in Load(Source()).Skip(262).Take(56)) {
            foreach (var field in new[] { "diagnosis", "phase" }) {
                var bad = c.DeepClone().AsObject();
                if (field == "diagnosis") bad["expected"][0][field] = "observer-damaged";
                else bad["expected"][0]["state"][field] = Text(bad["expected"][0]["state"][field]) == "IDLE" ? "ARMED" : "IDLE";
                Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Verify(bad));
            }
            foreach (var diagnosis in new[] { "stale-phase", "invalid-state" }) {
                // Stale-phase no-op rows do not independently kill the same no-op substitution.
                if (Text(c["expected"][0]["diagnosis"]) == diagnosis) Verify(c, (s,e) => new TransactionDecision(s, diagnosis));
                else Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Verify(c, (s,e) => new TransactionDecision(s, diagnosis)));
            }
        }
    }
    [Fact] public void DispatchRecursiveLeavesAreObserved()
    {
        var cases = Load(Source());
        foreach (var c in cases.Skip(262).Take(56))
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
        }
    }
    // Task88: representative axes only. Invalid syntax also mismatches valid state identity.
    private const string CorrelationMap = @"PREPARING|Prepared|uuid-syntax|correlation-preparing-prepared-uuid-syntax|0|correlation-preparing-prepared-uuid-syntax|1|arm
PREPARING|Prepared|uuid-equality|wrong-correlation-preparing|0|mode-on-zero|1|arm
PREPARING|Prepared|token-syntax|correlation-preparing-prepared-token-syntax|0|correlation-preparing-prepared-token-syntax|1|arm
PREPARING|Prepared|token-equality|correlation-preparing-prepared-token-equality|0|correlation-preparing-prepared-token-equality|1|arm
PREPARED|ArmCommitted|uuid-syntax|correlation-prepared-armcommitted-uuid-syntax|0|correlation-prepared-armcommitted-uuid-syntax|1|apply
PREPARED|ArmCommitted|uuid-equality|correlation-prepared-armcommitted-uuid-equality|0|correlation-prepared-armcommitted-uuid-equality|1|apply
PREPARED|ArmCommitted|token-syntax|correlation-prepared-armcommitted-token-syntax|0|correlation-prepared-armcommitted-token-syntax|1|apply
PREPARED|ArmCommitted|token-equality|correlation-prepared-armcommitted-token-equality|0|correlation-prepared-armcommitted-token-equality|1|apply
ARMED|ApplyVerified|uuid-syntax|correlation-armed-applyverified-uuid-syntax|0|correlation-armed-applyverified-uuid-syntax|1|commit-closure
ARMED|ApplyVerified|uuid-equality|correlation-armed-applyverified-uuid-equality|0|correlation-armed-applyverified-uuid-equality|1|commit-closure
ARMED|ApplyVerified|token-syntax|correlation-armed-applyverified-token-syntax|0|correlation-armed-applyverified-token-syntax|1|commit-closure
ARMED|ApplyVerified|token-equality|correlation-armed-applyverified-token-equality|0|correlation-armed-applyverified-token-equality|1|commit-closure
ARMED|ApplyFailed|uuid-syntax|rollback-correlation-applyfailed-uuid-syntax|0|rollback-history-unestablished-pack|3|rollback
ARMED|ApplyFailed|uuid-equality|rollback-correlation-applyfailed-uuid-mismatch|0|rollback-history-unestablished-pack|3|rollback
ARMED|ApplyFailed|token-syntax|rollback-correlation-applyfailed-token-syntax|0|rollback-history-unestablished-pack|3|rollback
ARMED|ApplyFailed|token-equality|rollback-correlation-applyfailed-token-mismatch|0|rollback-history-unestablished-pack|3|rollback
ROLLBACK_PENDING|RollbackVerified|uuid-syntax|rollback-correlation-rollbackverified-uuid-syntax|0|rollback-history-unestablished-pack|4|commit-closure
ROLLBACK_PENDING|RollbackVerified|uuid-equality|rollback-correlation-rollbackverified-uuid-mismatch|0|rollback-history-unestablished-pack|4|commit-closure
ROLLBACK_PENDING|RollbackVerified|token-syntax|rollback-correlation-rollbackverified-token-syntax|0|rollback-history-unestablished-pack|4|commit-closure
ROLLBACK_PENDING|RollbackVerified|token-equality|rollback-correlation-rollbackverified-token-mismatch|0|rollback-history-unestablished-pack|4|commit-closure
ROLLBACK_PENDING|RollbackFailed|uuid-syntax|failure-correlation-rollbackfailed|0|failure-history-reject-rollback-unpersisted|5|rollback-failed
ROLLBACK_PENDING|RollbackFailed|uuid-equality|failure-correlation-rollbackfailed|1|failure-history-reject-rollback-unpersisted|5|rollback-failed
ROLLBACK_PENDING|RollbackFailed|token-syntax|failure-correlation-rollbackfailed|2|failure-history-reject-rollback-unpersisted|5|rollback-failed
ROLLBACK_PENDING|RollbackFailed|token-equality|failure-correlation-rollbackfailed|3|failure-history-reject-rollback-unpersisted|5|rollback-failed
APPLIED|CompletionCommitted|uuid-syntax|correlation-applied-completioncommitted-uuid-syntax|0|correlation-applied-completioncommitted-repair|0|committed
APPLIED|CompletionCommitted|uuid-equality|correlation-applied-completioncommitted-uuid-equality|0|correlation-applied-completioncommitted-repair|0|committed
APPLIED|CompletionCommitted|token-syntax|correlation-applied-completioncommitted-token-syntax|0|correlation-applied-completioncommitted-repair|0|committed
APPLIED|CompletionCommitted|token-equality|correlation-applied-completioncommitted-token-equality|0|correlation-applied-completioncommitted-repair|0|committed
ROLLED_BACK|CompletionCommitted|uuid-syntax|correlation-rolled-back-completioncommitted-uuid-syntax|0|correlation-rolled-back-completioncommitted-repair|0|committed
ROLLED_BACK|CompletionCommitted|uuid-equality|correlation-rolled-back-completioncommitted-uuid-equality|0|correlation-rolled-back-completioncommitted-repair|0|committed
ROLLED_BACK|CompletionCommitted|token-syntax|correlation-rolled-back-completioncommitted-token-syntax|0|correlation-rolled-back-completioncommitted-repair|0|committed
ROLLED_BACK|CompletionCommitted|token-equality|correlation-rolled-back-completioncommitted-token-equality|0|correlation-rolled-back-completioncommitted-repair|0|committed
APPLIED|CompletionRejected|uuid-syntax|failure-correlation-applied-rejected|0|failure-history-reject-restored-unestablished-pack|4|rollback
APPLIED|CompletionRejected|uuid-equality|failure-correlation-applied-rejected|1|failure-history-reject-restored-unestablished-pack|4|rollback
APPLIED|CompletionRejected|token-syntax|failure-correlation-applied-rejected|2|failure-history-reject-restored-unestablished-pack|4|rollback
APPLIED|CompletionRejected|token-equality|failure-correlation-applied-rejected|3|failure-history-reject-restored-unestablished-pack|4|rollback
ROLLED_BACK|CompletionRejected|uuid-syntax|failure-correlation-rolled-rejected|0|failure-history-rolled-rejected|6|rollback-closure-rejected
ROLLED_BACK|CompletionRejected|uuid-equality|failure-correlation-rolled-rejected|1|failure-history-rolled-rejected|6|rollback-closure-rejected
ROLLED_BACK|CompletionRejected|token-syntax|failure-correlation-rolled-rejected|2|failure-history-rolled-rejected|6|rollback-closure-rejected
ROLLED_BACK|CompletionRejected|token-equality|failure-correlation-rolled-rejected|3|failure-history-rolled-rejected|6|rollback-closure-rejected
APPLIED|CompletionIndeterminate|uuid-syntax|failure-correlation-applied-indeterminate|0|failure-history-applied-indeterminate|4|completion-indeterminate
APPLIED|CompletionIndeterminate|uuid-equality|failure-correlation-applied-indeterminate|1|failure-history-applied-indeterminate|4|completion-indeterminate
APPLIED|CompletionIndeterminate|token-syntax|failure-correlation-applied-indeterminate|2|failure-history-applied-indeterminate|4|completion-indeterminate
APPLIED|CompletionIndeterminate|token-equality|failure-correlation-applied-indeterminate|3|failure-history-applied-indeterminate|4|completion-indeterminate
ROLLED_BACK|CompletionIndeterminate|uuid-syntax|failure-correlation-rolled-indeterminate|0|failure-history-rolled-indeterminate|6|completion-indeterminate
ROLLED_BACK|CompletionIndeterminate|uuid-equality|failure-correlation-rolled-indeterminate|1|failure-history-rolled-indeterminate|6|completion-indeterminate
ROLLED_BACK|CompletionIndeterminate|token-syntax|failure-correlation-rolled-indeterminate|2|failure-history-rolled-indeterminate|6|completion-indeterminate
ROLLED_BACK|CompletionIndeterminate|token-equality|failure-correlation-rolled-indeterminate|3|failure-history-rolled-indeterminate|6|completion-indeterminate";
    [Fact] public void CorpusCorrelationAxisLogs()
    {
        var all = Load(Source()).Take(339).ToList(); Assert.Equal(339, all.Count); Assert.Equal(713, all.Sum(c => c["expected"].AsArray().Count));
        Assert.Equal("e346800246c19ef81d59174bc0ad29af1ccd5c1de09e9addb3080511d5a2ec78", Sha(Utf8.GetBytes(Source()).Take(5014066).ToArray()));
        var added = all.Skip(318).ToList(); Assert.Equal(21, added.Count); Assert.Equal(32, added.Sum(c => c["expected"].AsArray().Count));
        foreach (var pair in new[] { ("transaction-forward-v1",112,205), ("transaction-rollback-success-v1",57,101), ("transaction-failure-blocked-v1",170,407) }) {
            var partition = all.Where(c => Text(c["oracleContract"]) == pair.Item1).ToList(); Assert.Equal(pair.Item2, partition.Count); Assert.Equal(pair.Item3, partition.Sum(c => c["expected"].AsArray().Count));
        }
        foreach (var c in added) { var before = c.ToJsonString(); Verify(c); Verify(c); Assert.Equal(before, c.ToJsonString()); }
    }
    [Fact] public void CorpusAccepted318Logs()
    {
        var old = Load(Source()).Take(318).ToList(); Assert.Equal(318, old.Count); Assert.Equal(681, old.Sum(c => c["expected"].AsArray().Count));
        foreach (var c in old) Verify(c);
    }
    [Fact] public void CorrelationExact48AxesAndSameSnapshotRepairs()
    {
        var all = Load(Source()).ToDictionary(c => Text(c["caseId"])); var cells = new HashSet<string>(); var reused = 0;
        JsonNode Input(JsonObject c, JsonObject d, int i) => i == 0 ? d["initialState"] : c["expected"][i-1]["state"];
        foreach (var line in CorrelationMap.Split((char)10)) {
            var p = line.Trim().Split('|'); Assert.True(cells.Add(string.Join("|", p.Take(3))));
            var c = all[p[3]]; var d = Validate(c); var i = int.Parse(p[4], CultureInfo.InvariantCulture); var s = Input(c,d,i); var ev = d["events"][i]; var row = c["expected"][i]; var corr = ev["correlation"];
            Assert.Equal(p[0], Text(s["phase"])); Assert.Equal(p[1], Text(ev["type"])); Assert.True(new SkinTransactionCore().IsStateValid(ReadTransactionState(s)));
            Assert.True(Bool(row["inputStateValid"])); Assert.True(Bool(row["stateValid"])); Assert.Equal("stale-correlation",Text(row["diagnosis"])); Assert.True(JsonNode.DeepEquals(s,row["state"])); Assert.Empty(row["commands"].AsArray());
            var idAxis = p[2].StartsWith("uuid-",StringComparison.Ordinal); var syntax = p[2].EndsWith("-syntax",StringComparison.Ordinal);
            if (idAxis) {
                Assert.True(JsonNode.DeepEquals(s["binding"],corr["binding"])); Assert.NotEqual(Text(s["envelope"]["transactionId"]),Text(corr["transactionId"]));
                Assert.Equal(!syntax,Regex.IsMatch(Text(corr["transactionId"]),@"\A[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\z"));
            } else {
                Assert.Equal(Text(s["envelope"]["transactionId"]),Text(corr["transactionId"])); Assert.False(JsonNode.DeepEquals(s["binding"],corr["binding"]));
                // The bounded lexical witnesses are leading-space invalid versus ASCII valid tokens.
                if (syntax) Assert.Equal(" bad",Text(corr["binding"]["value"])); else Assert.Matches(@"\A[A-Za-z0-9-]{1,256}\z",Text(corr["binding"]["value"]));
            }
            var positive = all[p[5]]; var pd = Validate(positive); var pi = int.Parse(p[6],CultureInfo.InvariantCulture); var ps = Input(positive,pd,pi); var pe = pd["events"][pi]; var pr = positive["expected"][pi];
            Assert.True(JsonNode.DeepEquals(s,ps)); var repaired = ev.DeepClone(); repaired["correlation"] = new JsonObject { ["transactionId"] = s["envelope"]["transactionId"].DeepClone(), ["binding"] = s["binding"].DeepClone() };
            Assert.True(JsonNode.DeepEquals(repaired,pe)); Assert.True(Bool(pr["inputStateValid"])); Assert.True(Bool(pr["stateValid"])); Assert.Equal(p[7],Text(pr["diagnosis"])); Assert.False(JsonNode.DeepEquals(s,pr["state"]));
            Verify(c); Verify(positive);
            if (!p[3].StartsWith("correlation-",StringComparison.Ordinal)) reused++;
            else if (p[1] == "CompletionCommitted") { Assert.NotEqual(p[3],p[5]); Assert.Single(d["events"].AsArray()); Assert.Equal(0,pi); }
            else { Assert.Equal(p[3],p[5]); Assert.Equal(1,pi); }
        }
        Assert.Equal(48,cells.Count); Assert.Equal(29,reused);
        foreach (var context in new[] { "PREPARING|Prepared", "PREPARED|ArmCommitted", "ARMED|ApplyVerified", "ARMED|ApplyFailed", "ROLLBACK_PENDING|RollbackVerified", "ROLLBACK_PENDING|RollbackFailed", "APPLIED|CompletionCommitted", "ROLLED_BACK|CompletionCommitted", "APPLIED|CompletionRejected", "ROLLED_BACK|CompletionRejected", "APPLIED|CompletionIndeterminate", "ROLLED_BACK|CompletionIndeterminate" })
            foreach (var axis in new[] { "uuid-syntax","uuid-equality","token-syntax","token-equality" }) Assert.Contains(context+"|"+axis,cells);
    }
    [Fact] public void CorrelationObserverDiagnosisPhaseAndNoopInvalidSubstitutions()
    {
        foreach (var c in Load(Source()).Skip(318).Take(21)) {
            foreach (var diagnosis in new[] { "stale-phase", "invalid-state" }) Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Verify(c,(s,e) => new TransactionDecision(s,diagnosis)));
            for (var i = 0; i < c["expected"].AsArray().Count; i++) {
                foreach (var field in new[] { "diagnosis", "phase" }) {
                    var bad = c.DeepClone().AsObject(); if (field == "diagnosis") bad["expected"][i][field] = "observer-damaged";
                    else bad["expected"][i]["state"][field] = "IDLE";
                    Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Verify(bad));
                }
            }
            if (c["expected"].AsArray().Count == 2) {
                var bad = c.DeepClone().AsObject(); var first = bad["expected"][0].DeepClone(); bad["expected"][0] = bad["expected"][1].DeepClone(); bad["expected"][1] = first; bad["expected"][0]["step"] = 0; bad["expected"][1]["step"] = 1;
                Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Verify(bad));
            }
        }
    }
    [Fact] public void CorrelationRecursiveLeavesAreObserved()
    {
        var cases = Load(Source());
        foreach (var c in cases.Skip(318).Take(21))
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
        }
    }
    // Task89 code-only lexical witnesses: no UUID/token, precedence, or broad guard closure.
    private static readonly string[] CodeNegative = { "0A", "_A", "@A", "[A", "Aa", "A/", "A:", "A[", "A^", "A`", "A-", " A", "A ", "A A", "A\0", "A\t", "A\n", "A\r", "A\u007f", "A\u009f", "Ａ", "Aé", "A\U0001f600", "A\u202e" };
    private static readonly string[] CodeContexts = { "applyfailed", "rollbackfailed", "applied-rejected", "rolled-rejected" };
    [Fact] public void CorpusAccepted339Logs()
    {
        var old = Load(Source()).Take(339).ToList(); Assert.Equal(339,old.Count); Assert.Equal(713,old.Sum(c => c["expected"].AsArray().Count));
        Assert.Equal("25d9edf7294e4f53bbed4fbc9b5579fac9db93e873edbc4d8d478c32f976047c",Sha(Utf8.GetBytes(Source()).Take(5228164).ToArray()));
        foreach (var c in old) Verify(c);
    }
    [Fact] public void CorpusCodeLexicalLogs()
    {
        var all=Load(Source()).Take(359).ToList(); Assert.Equal(359,all.Count); Assert.Equal(829,all.Sum(c => c["expected"].AsArray().Count));
        var added=all.Skip(339).ToList(); Assert.Equal(20,added.Count); Assert.Equal(116,added.Sum(c => c["expected"].AsArray().Count));
        foreach (var p in new[] { ("transaction-forward-v1",112,205),("transaction-rollback-success-v1",62,130),("transaction-failure-blocked-v1",185,494) }) {
            var part=all.Where(c => Text(c["oracleContract"])==p.Item1).ToList(); Assert.Equal(p.Item2,part.Count); Assert.Equal(p.Item3,part.Sum(c => c["expected"].AsArray().Count));
        }
        foreach (var c in added) { var before=c.ToJsonString(); Verify(c); Verify(c); Assert.Equal(before,c.ToJsonString()); }
    }
    [Fact] public void CodeExactPanelsAndOriginalSnapshotRepairs()
    {
        var all=Load(Source()).ToDictionary(c => Text(c["caseId"])); var seeds=new[] { "rollback-code-empty","failure-code-rollbackfailed","failure-code-applied-rejected","failure-code-rolled-rejected" };
        var phases=new[] { "ARMED","ROLLBACK_PENDING","APPLIED","ROLLED_BACK" }; var suffixes=new[] { "negative-panel-and-min-repair","uppercase-last","min-plus-one","body-classes","max-minus-one" };
        var panels=new[] { CodeNegative.Concat(new[] { "A" }).ToArray(),new[] { "Z" },new[] { "A0" },new[] { "AZ09_" },new[] { new string('A',127) } }; var count=0;
        for(var n=0;n<4;n++) {
            var seed=Validate(all[seeds[n]]); var s=seed["initialState"]; var original=seed["events"][0]; Assert.Equal(phases[n],Text(s["phase"]));
            for(var p=0;p<5;p++) {
                var c=all["lex-code-"+CodeContexts[n]+"-"+suffixes[p]]; var d=Validate(c); Assert.True(JsonNode.DeepEquals(s,d["initialState"])); Assert.Equal(panels[p],d["events"].AsArray().Select(e => Text(e["code"])).ToArray());
                for(var i=0;i<panels[p].Length;i++) {
                    var ev=d["events"][i]; var row=c["expected"][i]; var expectedEvent=original.DeepClone(); expectedEvent["code"]=panels[p][i]; Assert.True(JsonNode.DeepEquals(expectedEvent,ev));
                    Assert.True(Bool(row["inputStateValid"])); Assert.True(Bool(row["stateValid"]));
                    if(p==0 && i<24) {
                        Assert.Equal("invalid-code",Text(row["diagnosis"])); Assert.True(JsonNode.DeepEquals(s,row["state"])); Assert.Empty(row["commands"].AsArray());
                        var repaired=ev.DeepClone(); repaired["code"]="A"; Assert.True(JsonNode.DeepEquals(repaired,d["events"][24]));
                        var core=new SkinTransactionCore(); var decision=core.Decide(ReadTransactionState(s),ReadTransactionEvent(repaired)); Assert.True(core.IsStateValid(decision.State)); Assert.Equal(Text(c["expected"][24]["diagnosis"]),decision.Diagnosis);
                    } else {
                        var expected=s.DeepClone(); var reverse=n==0 || n==2; expected["phase"]=reverse?"ROLLBACK_PENDING":"BLOCKED";
                        if(reverse) { expected["pendingClosure"]=null; expected["originalFailure"]=panels[p][i]; } else expected["rollbackFailure"]=panels[p][i];
                        Assert.True(JsonNode.DeepEquals(expected,row["state"])); Assert.False(JsonNode.DeepEquals(s,row["state"])); Assert.Equal(reverse?"rollback":n==1?"rollback-failed":"rollback-closure-rejected",Text(row["diagnosis"]));
                        var commands=new JsonArray(); if(reverse) commands.Add(new JsonObject { ["type"]="Rollback",["correlation"]=ev["correlation"].DeepClone() }); Assert.True(JsonNode.DeepEquals(commands,row["commands"]));
                    }
                }
                Verify(c); count++;
            }
        }
        Assert.Equal(20,count);
    }
    [Fact] public void CodeNamed32MalformedControlsPrecedeReducer()
    {
        var all=Load(Source()).ToDictionary(c => Text(c["caseId"])); var names=new HashSet<string>();
        foreach(var context in CodeContexts) foreach(var damage in new[] { "null","bool","integer","array","object","missing","high","low" }) {
            var bad=all["lex-code-"+context+"-uppercase-last"].DeepClone().AsObject(); var d=Validate(bad); var ev=d["events"][0].AsObject();
            if(damage=="missing") ev.Remove("code"); else ev["code"]=damage switch { "null" => null,"bool" => JsonValue.Create(true),"integer" => JsonValue.Create(0),"array" => new JsonArray(),"object" => new JsonObject(),_ => JsonValue.Create("SURROGATE_SENTINEL") };
            var text=d.ToJsonString(); if(damage=="high" || damage=="low") text=text.Replace("SURROGATE_SENTINEL",damage=="high"?"\\ud800":"\\udc00");
            bad["input"]=new JsonObject { ["utf8Text"]=text }; bad["inputSha256"]=Sha(Utf8.GetBytes(text)); var calls=0;
            Assert.Throws<FixtureFormatError>(() => Verify(bad,(s,e) => { calls++; return new SkinTransactionCore().Decide(s,e); })); Assert.Equal(0,calls); Assert.True(names.Add(context+"|"+damage));
        }
        Assert.Equal(32,names.Count);
    }
    [Fact] public void CodeObserverDiagnosisPhaseNoopInvalidAndDistinctFinalOrder()
    {
        foreach(var c in Load(Source()).Skip(339).Take(20)) {
            foreach(var diagnosis in new[] { "stale-phase","invalid-state" }) Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Verify(c,(s,e) => new TransactionDecision(s,diagnosis)));
            if(c["expected"].AsArray().Count==25) {
                var bad=c.DeepClone().AsObject(); var first=bad["expected"][0].DeepClone(); bad["expected"][0]=bad["expected"][24].DeepClone(); bad["expected"][24]=first; bad["expected"][0]["step"]=0; bad["expected"][24]["step"]=24;
                Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Verify(bad));
            }
            for(var i=0;i<c["expected"].AsArray().Count;i++) {
                foreach(var field in new[] { "diagnosis","phase" }) { var bad=c.DeepClone().AsObject(); if(field=="diagnosis") bad["expected"][i][field]="observer-damaged"; else bad["expected"][i]["state"][field]="IDLE"; Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Verify(bad)); }
                var commands=c["expected"][i]["commands"].AsArray(); if(commands.Count>0) { var bad=c.DeepClone().AsObject(); bad["expected"][i]["commands"].AsArray().Add(commands[0].DeepClone()); Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Verify(bad)); }
            }
        }
        // Identical negative rows cannot prove order; the changing final positive can. No multi-command ordering claim.
    }

    [Fact] public void CodeRecursiveLeavesAreObserved()
    {
        var cases = Load(Source());
        foreach (var c in cases.Skip(339).Take(20))
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
        }
    }
    // Task90: UUID shape only in PREPARING/Prepared; identity also rejects invalid syntax.
    private static readonly string[][] UuidPanels = new[] { new[] { "","11111111-1111-1111-1111-11111111111","11111111-1111-1111-1111-1111111111111","111111111111-1111-1111-111111111111","11111111--1111-1111-1111-111111111111","1111111-11111-1111-1111-111111111111","11111111_1111-1111-1111-111111111111","11111111111111111111111111111111","{11111111-1111-1111-1111-111111111111}","urn:uuid:11111111-1111-1111-1111-111111111111" },new[] { "A1111111-1111-1111-1111-111111111111","F1111111-1111-1111-1111-111111111111","AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA","/1111111-1111-1111-1111-111111111111",":1111111-1111-1111-1111-111111111111","`1111111-1111-1111-1111-111111111111","g1111111-1111-1111-1111-111111111111","G1111111-1111-1111-1111-111111111111" },new[] { " 11111111-1111-1111-1111-111111111111","11111111-1111-1111-1111-111111111111 ","1111 111-1111-1111-1111-111111111111","\t11111111-1111-1111-1111-111111111111","11111111-1111-1111-1111-111111111111\t","1111\t111-1111-1111-1111-111111111111","\n11111111-1111-1111-1111-111111111111","11111111-1111-1111-1111-111111111111\n","1111\n111-1111-1111-1111-111111111111","11111111-1111-1111-1111-111111111111\r","11111111-1111-1111-1111-111111111111\r\n","1111\u0000111-1111-1111-1111-111111111111","1111\u007f111-1111-1111-1111-111111111111","1111\u0085111-1111-1111-1111-111111111111","\u00a011111111-1111-1111-1111-111111111111","11111111-1111-1111-1111-111111111111\u00a0","1111\u2028111-1111-1111-1111-111111111111","11111111-1111-1111-1111-111111111111\u2028" },new[] { "\u06601111111-1111-1111-1111-111111111111","\uff101111111-1111-1111-1111-111111111111","\uff411111111-1111-1111-1111-111111111111","\u03b11111111-1111-1111-1111-111111111111","\u04301111111-1111-1111-1111-111111111111","11111111\u20101111-1111-1111-111111111111","\ud83d\ude001111111-1111-1111-1111-111111111111","\ud835\udfce1111111-1111-1111-1111-111111111111" } };
    private static readonly string[] UuidPanelNames = { "shape", "ascii", "whitespace", "unicode" };
    private static readonly string[] UuidPositiveNames = { "zero", "nine", "a", "f" };
    private static readonly string[] UuidPositiveValues = new[] { "00000000-0000-0000-0000-000000000000","99999999-9999-9999-9999-999999999999","aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","ffffffff-ffff-ffff-ffff-ffffffffffff" };
    [Fact] public void CorpusAccepted359Logs()
    {
        var old=Load(Source()).Take(359).ToList(); Assert.Equal(359,old.Count); Assert.Equal(829,old.Sum(c=>c["expected"].AsArray().Count));
        Assert.Equal("88dc3230cbe4fc346120b26c003aa2e716d71d3ca7b27f2eb66098d9716c5b2d",Sha(Utf8.GetBytes(Source()).Take(5937735).ToArray()));
        foreach(var c in old) Verify(c);
    }
    [Fact] public void CorpusUuidLexicalLogs()
    {
        var all=Load(Source()); Assert.Equal(367,all.Count); Assert.Equal(881,all.Sum(c=>c["expected"].AsArray().Count));
        Assert.Equal(8,all.Skip(359).Count()); Assert.Equal(52,all.Skip(359).Sum(c=>c["expected"].AsArray().Count));
        foreach(var p in new[] { ("transaction-forward-v1",120,257),("transaction-rollback-success-v1",62,130),("transaction-failure-blocked-v1",185,494) }) {
            var part=all.Where(c=>Text(c["oracleContract"])==p.Item1).ToList(); Assert.Equal(p.Item2,part.Count); Assert.Equal(p.Item3,part.Sum(c=>c["expected"].AsArray().Count));
        }
        foreach(var c in all) { var before=c.ToJsonString(); Verify(c); Assert.Equal(before,c.ToJsonString()); }
    }
    [Fact] public void UuidExactPanelsAndOriginalSnapshotRepairs()
    {
        var all=Load(Source()).ToDictionary(c=>Text(c["caseId"])); var donor=Validate(all["correlation-preparing-prepared-uuid-syntax"]); var original=donor["events"][1]; var originalState=donor["initialState"]; var negatives=0;
        for(var p=0;p<8;p++) {
            var positive=p>=4; var name=positive?"positive-"+UuidPositiveNames[p-4]:UuidPanelNames[p]; var values=positive?new[]{UuidPositiveValues[p-4]}:UuidPanels[p].Concat(new[]{Text(original["correlation"]["transactionId"])}).ToArray();
            var c=all["lex-uuid-preparing-prepared-"+name]; var d=Validate(c); var s=d["initialState"]; var initial=originalState.DeepClone(); if(positive) initial["envelope"]["transactionId"]=values[0];
            Assert.True(JsonNode.DeepEquals(initial,s)); Assert.True(new SkinTransactionCore().IsStateValid(ReadTransactionState(s))); Assert.Equal("transaction-forward-v1",Text(c["oracleContract"]));
            Assert.Equal(values,d["events"].AsArray().Select(e=>Text(e["correlation"]["transactionId"])).ToArray());
            for(var i=0;i<values.Length;i++) {
                var ev=d["events"][i]; var row=c["expected"][i]; var expectedEvent=original.DeepClone(); expectedEvent["correlation"]["transactionId"]=values[i]; Assert.True(JsonNode.DeepEquals(expectedEvent,ev));
                Assert.True(Bool(row["inputStateValid"])); Assert.True(Bool(row["stateValid"]));
                if(!positive && i<values.Length-1) {
                    negatives++; Assert.Equal("stale-correlation",Text(row["diagnosis"])); Assert.True(JsonNode.DeepEquals(s,row["state"])); Assert.Empty(row["commands"].AsArray());
                    var repair=ev.DeepClone(); repair["correlation"]["transactionId"]=Text(original["correlation"]["transactionId"]); Assert.True(JsonNode.DeepEquals(original,repair));
                    var core=new SkinTransactionCore(); var result=core.Decide(ReadTransactionState(originalState),ReadTransactionEvent(repair)); var outState=originalState.DeepClone(); outState["phase"]="PREPARED";
                    Assert.Equal("arm",result.Diagnosis); Assert.True(core.IsStateValid(result.State)); Assert.True(JsonNode.DeepEquals(outState,Project(result.State)));
                    Assert.Single(result.Commands); Assert.True(JsonNode.DeepEquals(new JsonObject { ["type"]="Arm",["envelope"]=originalState["envelope"].DeepClone() },Project(result.Commands[0])));
                } else {
                    var expected=s.DeepClone(); expected["phase"]="PREPARED"; Assert.True(JsonNode.DeepEquals(expected,row["state"])); Assert.Equal("arm",Text(row["diagnosis"]));
                    Assert.True(JsonNode.DeepEquals(new JsonArray(new JsonObject { ["type"]="Arm",["envelope"]=s["envelope"].DeepClone() }),row["commands"]));
                }
            }
            Verify(c);
        }
        Assert.Equal(44,negatives);
    }
    [Fact] public void UuidNamed19RepresentationAndScopeControls()
    {
        var cases=Load(Source()); var basis=cases.Single(c=>Text(c["caseId"])=="correlation-preparing-prepared-uuid-syntax"); var names=new HashSet<string>();
        foreach(var damage in new[] { "null","bool","integer","array","object","missing","high","low","reversed" }) {
            var bad=basis.DeepClone().AsObject(); var d=Validate(bad); var correlation=d["events"][0]["correlation"].AsObject();
            if(damage=="missing") correlation.Remove("transactionId"); else correlation["transactionId"]=damage switch { "null"=>null,"bool"=>JsonValue.Create(true),"integer"=>JsonValue.Create(1),"array"=>new JsonArray(),"object"=>new JsonObject(),_=>JsonValue.Create("SURROGATE_SENTINEL") };
            var text=d.ToJsonString(); if(damage=="high" || damage=="low" || damage=="reversed") text=text.Replace("SURROGATE_SENTINEL",damage=="high"?"\\ud800":damage=="low"?"\\udc00":"\\udc00\\ud800");
            bad["input"]=new JsonObject { ["utf8Text"]=text }; bad["inputSha256"]=Sha(Utf8.GetBytes(text)); var calls=0;
            Assert.Throws<FixtureFormatError>(()=>Verify(bad,(s,e)=>{calls++;return new SkinTransactionCore().Decide(s,e);})); Assert.Equal(0,calls); Assert.True(names.Add("Prepared|"+damage));
        }
        foreach(var kind in new[] { "ApplyFailed","RollbackVerified","RollbackFailed","CompletionRejected","CompletionIndeterminate" }) {
            var seed=cases.SelectMany(c=>Validate(c)["events"].AsArray()).First(e=>Text(e["type"])==kind);
            foreach(var malformed in new[]{false,true}) {
                var bad=basis.DeepClone().AsObject(); var d=Validate(bad); var ev=seed.DeepClone(); ev["correlation"]["transactionId"]=malformed?null:JsonValue.Create(""); d["events"][0]=ev; Reinput(bad,d); var calls=0;
                Action run=()=>Verify(bad,(s,e)=>{calls++;return new SkinTransactionCore().Decide(s,e);});
                if(malformed) Assert.Throws<FixtureFormatError>(run); else Assert.Throws<OracleOutOfScope>(run); Assert.Equal(0,calls); Assert.True(names.Add(kind+"|"+malformed));
            }
        }
        Assert.Equal(19,names.Count);
    }
    [Fact] public void UuidContractsReplayAndInputImmutability()
    {
        foreach(var c in Load(Source()).Skip(359).Take(8)) {
            var before=c.ToJsonString(); Verify(c); Verify(c); Assert.Equal(before,c.ToJsonString());
            foreach(var contract in new[]{"transaction-forward-v1","transaction-rollback-success-v1","transaction-failure-blocked-v1"}) { var other=c.DeepClone().AsObject(); other["oracleContract"]=contract; Verify(other); }
            var data=Validate(c); var input=data.ToJsonString(); var s=ReadTransactionState(data["initialState"]); var core=new SkinTransactionCore();
            foreach(var ev in data["events"].AsArray()) { var e=ReadTransactionEvent(ev); var a=core.Decide(s,e); var b=core.Decide(s,e); Assert.Equal(a.Diagnosis,b.Diagnosis); Assert.True(JsonNode.DeepEquals(Project(a.State),Project(b.State))); Assert.Equal(a.Commands,b.Commands); s=a.State; }
            Assert.Equal(input,data.ToJsonString());
        }
    }

    [Fact] public void UuidObserverDiagnosisPhaseNoopInvalidAndDistinctFinalOrder()
    {
        foreach(var c in Load(Source()).Skip(359).Take(8)) {
            foreach(var diagnosis in new[] { "stale-phase","invalid-state" }) Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Verify(c,(s,e) => new TransactionDecision(s,diagnosis)));
            if(c["expected"].AsArray().Count>1) {
                var last=c["expected"].AsArray().Count-1; var bad=c.DeepClone().AsObject(); var first=bad["expected"][0].DeepClone(); bad["expected"][0]=bad["expected"][last].DeepClone(); bad["expected"][last]=first; bad["expected"][0]["step"]=0; bad["expected"][last]["step"]=last;
                Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Verify(bad));
            }
            for(var i=0;i<c["expected"].AsArray().Count;i++) {
                foreach(var field in new[] { "diagnosis","phase" }) { var bad=c.DeepClone().AsObject(); if(field=="diagnosis") bad["expected"][i][field]="observer-damaged"; else bad["expected"][i]["state"][field]="IDLE"; Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Verify(bad)); }
                var commands=c["expected"][i]["commands"].AsArray(); if(commands.Count>0) { var bad=c.DeepClone().AsObject(); bad["expected"][i]["commands"].AsArray().Add(commands[0].DeepClone()); Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Verify(bad)); }
            }
        }
        // Identical negative rows cannot prove order; the changing final positive can. No multi-command ordering claim.
    }

    [Fact] public void UuidRecursiveLeavesAreObserved()
    {
        var cases = Load(Source()); var observed=0;
        foreach (var c in cases.Skip(359).Take(8))
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
            var key = path.Last(); if (key == "step") continue; observed++;
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
        }
        Assert.Equal(2440,observed);
    }

}
