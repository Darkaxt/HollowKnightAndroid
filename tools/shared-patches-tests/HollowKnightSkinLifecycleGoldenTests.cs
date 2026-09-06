using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DualSouls.Skins.HollowKnight.Core;
using Xunit;

public sealed class HollowKnightSkinLifecycleGoldenTests
{
    // Wire and event ceilings belong to this HARNESS, not the production reducer.
    private const string StateFields = "armedHero pendingEpoch stableCount currentHero currentSkin lastConfirmedEpoch armedSkin armedOccurrence occurrenceHighWater";
    private const string Flags = "acceptingInput fullDamageMode canTakeDamage playable paused cutscene sceneTransition";
    private const string Diagnoses = "invalid-state same-binding rebound unbound stale-binding invalid-occurrence consumed-occurrence candidate-already-armed pending-epoch armed unarmed-after-death mismatched-occurrence death-confirmed stale-observation no-pending-epoch unstable-observation stabilizing stable-respawn epoch-overflow";
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private sealed class FixtureError : Exception { public FixtureError(string message) : base(message) { } }
    private sealed class ScopeError : Exception { }
    private static void Require(bool condition, string message = "Malformed lifecycle fixture") { if (!condition) throw new FixtureError(message); }
    private static string Source() => Utf8.GetString(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "skin-goldens/v1/lifecycle.json")));
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
        catch (Exception e) when (e is JsonException || e is EncoderFallbackException || e is InvalidOperationException) { throw new FixtureError(e.Message); }
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
    private static ulong UInt(JsonNode node)
    {
        var text = Text(node); Require(Regex.IsMatch(text, @"\A(?:0|[1-9][0-9]*)\z"));
        Require(ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var result)); return result;
    }
    private static bool Bool(JsonNode node) { Require(node is JsonValue v && v.GetValueKind() is JsonValueKind.True or JsonValueKind.False); return node.GetValue<bool>(); }
    private static string OptionalText(JsonNode node) => node == null ? null : Text(node);
    private static DeathEpoch Epoch(JsonNode node) => node == null ? null : new DeathEpoch(UInt(node));
    private static LifecycleState State(JsonNode node)
    {
        var s = Fields(node, StateFields);
        // No invariant checking here: representable bad snapshots must reach Observe.
        return new LifecycleState(s["armedHero"] == null ? null : new HeroBindingToken(Text(s["armedHero"])), Epoch(s["pendingEpoch"]), Int(s["stableCount"]),
            s["currentHero"] == null ? null : new HeroBindingToken(Text(s["currentHero"])), s["currentSkin"] == null ? null : new SkinBindingToken(Text(s["currentSkin"])),
            new DeathEpoch(UInt(s["lastConfirmedEpoch"])), s["armedSkin"] == null ? null : new SkinBindingToken(Text(s["armedSkin"])),
            s["armedOccurrence"] == null ? null : new DeathOccurrenceToken(UInt(s["armedOccurrence"])), new DeathOccurrenceToken(UInt(s["occurrenceHighWater"])));
    }
    private static LifecycleSignal Signal(JsonNode node)
    {
        Require(node is JsonObject); var type = Text(node["type"]);
        Require(new[] { "Rebind", "BeforeDeath", "AfterDeath", "Update" }.Contains(type));
        Fields(node, "type hero skin" + (type == "Update" ? " health " + Flags : type == "Rebind" ? "" : " occurrence"));
        var h = new HeroBindingToken(Text(node["hero"])); var s = new SkinBindingToken(Text(node["skin"]));
        return type switch
        {
            "Rebind" => new LifecycleSignal.Rebind(h, s),
            "BeforeDeath" => new LifecycleSignal.BeforeDeath(h, s, new DeathOccurrenceToken(UInt(node["occurrence"]))),
            "AfterDeath" => new LifecycleSignal.AfterDeath(h, s, new DeathOccurrenceToken(UInt(node["occurrence"]))),
            _ => new LifecycleSignal.Update(new HeroObservation(h, s, Bool(node["acceptingInput"]), Bool(node["fullDamageMode"]), Int(node["health"]), Bool(node["canTakeDamage"]), Bool(node["playable"]), Bool(node["paused"]), Bool(node["cutscene"]), Bool(node["sceneTransition"])))
        };
    }
    private static string Sha(byte[] raw) => Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant();
    private static JsonObject Validate(JsonNode node)
    {
        var c = Fields(node, "caseId oracleContract input inputSha256 expected"); Require(Text(c["caseId"]).Length > 0);
        if (Text(c["oracleContract"]) != "lifecycle-core-v1") throw new ScopeError();
        Require(c["input"] is JsonObject); var input = c["input"].AsObject(); Require(input.Count == 1);
        byte[] raw;
        if (input.ContainsKey("utf8Text")) raw = Utf8.GetBytes(Text(input["utf8Text"]));
        else { Fields(input, "utf8Hex"); var hex = Text(input["utf8Hex"]); Require(Regex.IsMatch(hex, @"\A(?:[0-9a-f]{2})*\z")); raw = Convert.FromHexString(hex); }
        var hash = Text(c["inputSha256"]); Require(Regex.IsMatch(hash, @"\A[0-9a-f]{64}\z") && hash == Sha(raw), "Raw lifecycle hash before decode");
        if (raw.Length > 65536) throw new ScopeError();
        JsonObject data;
        try { data = Fields(Parse(Utf8.GetString(raw)), "initialState events"); }
        catch (DecoderFallbackException e) { throw new FixtureError(e.Message); }
        State(data["initialState"]); Require(data["events"] is JsonArray); var events = data["events"].AsArray(); Require(events.Count > 0);
        if (events.Count > 32) throw new ScopeError();
        foreach (var signal in events) Signal(signal);
        Require(c["expected"] is JsonArray); var expected = c["expected"].AsArray(); Require(expected.Count == events.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            var record = Fields(expected[i], "step state diagnosis confirmedEpoch stableToken"); Require(Int(record["step"]) == i); State(record["state"]);
            Require(Diagnoses.Split(' ').Contains(Text(record["diagnosis"]))); if (record["confirmedEpoch"] != null) UInt(record["confirmedEpoch"]);
            if (record["stableToken"] != null) { var t = Fields(record["stableToken"], "deathEpoch hero skin"); UInt(t["deathEpoch"]); Text(t["hero"]); Text(t["skin"]); }
        }
        return data;
    }
    private static List<JsonObject> Load(string source)
    {
        var root = Fields(Parse(source), "schemaVersion cases"); Require(Int(root["schemaVersion"]) == 1); Require(root["cases"] is JsonArray);
        var ids = new HashSet<string>(); var result = new List<JsonObject>();
        foreach (var c in root["cases"].AsArray()) { Validate(c); Require(ids.Add(Text(c["caseId"]))); result.Add(c.AsObject()); }
        Require(result.Count > 0); return result;
    }
    private static string Decimal(ulong value) => value.ToString(CultureInfo.InvariantCulture);
    private static JsonObject Project(int step, LifecycleDecision d)
    {
        var s = d.State;
        return new JsonObject {
            ["step"] = step, ["diagnosis"] = d.Diagnosis, ["confirmedEpoch"] = d.ConfirmedEpoch == null ? null : Decimal(d.ConfirmedEpoch.Value),
            ["stableToken"] = d.StableToken == null ? null : new JsonObject { ["deathEpoch"] = Decimal(d.StableToken.DeathEpoch.Value), ["hero"] = d.StableToken.Hero.Value, ["skin"] = d.StableToken.Skin.Value },
            ["state"] = new JsonObject { ["armedHero"] = s.ArmedHero?.Value, ["pendingEpoch"] = s.PendingEpoch == null ? null : Decimal(s.PendingEpoch.Value), ["stableCount"] = s.StableCount,
                ["currentHero"] = s.CurrentHero?.Value, ["currentSkin"] = s.CurrentSkin?.Value, ["lastConfirmedEpoch"] = Decimal(s.LastConfirmedEpoch.Value), ["armedSkin"] = s.ArmedSkin?.Value,
                ["armedOccurrence"] = s.ArmedOccurrence == null ? null : Decimal(s.ArmedOccurrence.Value), ["occurrenceHighWater"] = Decimal(s.OccurrenceHighWater.Value) }
        };
    }
    private static void Verify(JsonObject c, Func<LifecycleState, LifecycleSignal, LifecycleDecision> observe = null)
    {
        var data = Validate(c); var state = State(data["initialState"]); var actual = new JsonArray(); observe ??= new SkinLifecycleCore().Observe;
        foreach (var signal in data["events"].AsArray()) { var decision = observe(state, Signal(signal)); actual.Add(Project(actual.Count, decision)); state = decision.State; }
        Assert.True(JsonNode.DeepEquals(c["expected"], actual), Text(c["caseId"]) + " full ordered lifecycle log mismatch\n" + actual);
    }
    private static void Reinput(JsonObject c, JsonNode data)
    {
        var raw = Utf8.GetBytes(data.ToJsonString()); c["input"] = new JsonObject { ["utf8Text"] = Utf8.GetString(raw) }; c["inputSha256"] = Sha(raw);
    }
    [Fact] public void CorpusHashesFullLogsPositiveAndNoop()
    {
        var cases = Load(Source()); Assert.Equal(36, cases.Count); Assert.Equal(152, cases.Sum(c => c["expected"].AsArray().Count));
        foreach (var c in cases) Verify(c);
        Assert.Contains(cases.SelectMany(c => c["expected"].AsArray()), r => r["stableToken"] != null);
        Assert.Contains(cases.SelectMany(c => c["expected"].AsArray()), r => Text(r["diagnosis"]) == "same-binding");
    }
    [Fact] public void EveryFieldAndRecordOrderIsObserved()
    {
        var c = Load(Source())[0];
        IEnumerable<string[]> Leaves(JsonNode n, string[] path)
        {
            if (n is JsonObject o) foreach (var p in o) foreach (var leaf in Leaves(p.Value, path.Append(p.Key).ToArray())) yield return leaf;
            else yield return path;
        }
        for (var i = 0; i < c["expected"].AsArray().Count; i++)
        {
            foreach (var path in Leaves(c["expected"][i], Array.Empty<string>()))
            {
                var bad = c.DeepClone().AsObject(); var part = bad["expected"][i]; foreach (var key in path.SkipLast(1)) part = part[key];
                var keyLast = path.Last(); var old = part[keyLast];
                part[keyLast] = keyLast switch {
                    "stableToken" => new JsonObject { ["deathEpoch"] = "7", ["hero"] = "damaged", ["skin"] = "damaged" },
                    "diagnosis" => JsonValue.Create(Text(old) == "armed" ? "same-binding" : "armed"),
                    "pendingEpoch" or "lastConfirmedEpoch" or "armedOccurrence" or "occurrenceHighWater" or "confirmedEpoch" or "deathEpoch" => JsonValue.Create("7"),
                    _ => old is JsonValue v && v.GetValueKind() == JsonValueKind.Number ? JsonValue.Create(Int(old) + 1) : JsonValue.Create("damaged")
                };
                if (keyLast == "step") Assert.Throws<FixtureError>(() => Verify(bad));
                else Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Verify(bad));
            }
            foreach (var change in new[] { "omit", "extra", "reorder" })
            {
                var bad = c.DeepClone().AsObject(); var log = bad["expected"].AsArray();
                if (change == "omit") log.RemoveAt(i); else if (change == "extra") log[i]["unknown"] = 1;
                else { var j = (i + 1) % log.Count; var a = log[i].DeepClone(); log[i] = log[j].DeepClone(); log[j] = a; }
                Assert.Throws<FixtureError>(() => Verify(bad));
            }
        }
        var reordered = c.DeepClone().AsObject(); var records = reordered["expected"].AsArray();
        var first = records[3].DeepClone(); records[3] = records[5].DeepClone(); records[5] = first;
        for (var i = 0; i < records.Count; i++) records[i]["step"] = i;
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Verify(reordered));
    }
    [Fact] public void NoopAndInvalidSubstitutionsKillPositive()
    {
        foreach (var diagnosis in new[] { "same-binding", "invalid-state" })
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Verify(Load(Source())[0], (state, signal) => new LifecycleDecision(state, null, null, diagnosis)));
    }
    [Fact] public void InvalidFixturesFailBeforeReducer()
    {
        foreach (var text in new[] { "{}", "", "{\"schemaVersion\":1,\"cases\":[]}", "{\"schemaVersion\":1,\"schemaVersion\":1,\"cases\":[]}" }) Assert.Throws<FixtureError>(() => Load(text));
        var root = Parse(Source()); root["cases"].AsArray().Add(root["cases"][0].DeepClone()); Assert.Throws<FixtureError>(() => Load(root.ToJsonString()));
        var c = Load(Source())[0];
        var mutations = new List<Action<JsonObject>> { b => b["unknown"] = 1, b => b["inputSha256"] = new string('0', 64), b => b["expected"] = new JsonArray(), b => b["input"]["utf8Hex"] = "00" };
        foreach (var pair in new (string Field, JsonNode Value)[] { ("stableCount", JsonValue.Create(true)), ("stableCount", JsonValue.Create(2147483648L)), ("lastConfirmedEpoch", JsonValue.Create("01")), ("lastConfirmedEpoch", JsonValue.Create("18446744073709551616")), ("lastConfirmedEpoch", null), ("currentHero", JsonValue.Create(3)), ("unknown", JsonValue.Create(0)) })
            mutations.Add(b => { var d = Parse(Text(b["input"]["utf8Text"])); d["initialState"][pair.Field] = pair.Value?.DeepClone(); Reinput(b, d); });
        foreach (var pair in new (string Field, JsonNode Value)[] { ("type", JsonValue.Create("Unknown")), ("hero", null), ("health", JsonValue.Create(true)), ("health", JsonValue.Create(1.5)), ("health", JsonValue.Create(-2147483649L)), ("paused", JsonValue.Create(1)), ("unknown", JsonValue.Create(0)) })
            mutations.Add(b => { var d = Parse(Text(b["input"]["utf8Text"])); d["events"][0][pair.Field] = pair.Value?.DeepClone(); Reinput(b, d); });
        foreach (var mutation in mutations) { var bad = c.DeepClone().AsObject(); mutation(bad); var called = false; Assert.Throws<FixtureError>(() => Verify(bad, (s, e) => { called = true; return new SkinLifecycleCore().Observe(s, e); })); Assert.False(called); }
    }
    [Fact] public void UnsupportedScopeIsNotReducerDiagnosis()
    {
        foreach (var mode in new[] { "contract", "bytes", "events" })
        {
            var c = Load(Source())[0];
            if (mode == "contract") c["oracleContract"] = "future";
            else if (mode == "bytes") { var raw = Utf8.GetBytes(new string(' ', 65537)); c["input"] = new JsonObject { ["utf8Text"] = Utf8.GetString(raw) }; c["inputSha256"] = Sha(raw); }
            else { var d = Validate(c); var events = d["events"].AsArray(); while (events.Count < 33) events.Add(events[0].DeepClone()); Reinput(c, d); }
            var called = false; Assert.Throws<ScopeError>(() => Verify(c, (s, e) => { called = true; return new SkinLifecycleCore().Observe(s, e); })); Assert.False(called);
        }
    }
    [Fact] public void ReplayIsDeterministicAndImmutable()
    {
        foreach (var c in Load(Source()))
        {
            var before = c.ToJsonString(); Verify(c); Verify(c); Assert.Equal(before, c.ToJsonString());
            var d = Validate(c); var state = State(d["initialState"]); var signal = Signal(d["events"][0]); var copy = state with { };
            Assert.Equal(new SkinLifecycleCore().Observe(state, signal), new SkinLifecycleCore().Observe(state, signal)); Assert.Equal(copy, state);
        }
    }
}
