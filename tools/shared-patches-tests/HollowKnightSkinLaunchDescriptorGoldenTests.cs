using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DualSouls.Skins.HollowKnight.Core;
using Xunit;

public sealed class HollowKnightSkinLaunchDescriptorGoldenTests
{
    private const string Contract = "descriptor-empty-clear-v1";
    private static readonly string FixturePath = Path.Combine(AppContext.BaseDirectory, "skin-goldens", "v1", "launch-descriptors.json");
    private static readonly Regex Digest = new("\\A[0-9a-f]{64}\\z");
    private static readonly Regex Uuid = new("\\A[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\\z");
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static IEnumerable<object[]> Cases() => Load(File.ReadAllText(FixturePath, Utf8))
        .Select(c => new object[] { c.GetProperty("caseId").GetString(), c.GetRawText() });

    [Theory]
    [MemberData(nameof(Cases))]
    public void PinnedDescriptor(string caseId, string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Equal(caseId, document.RootElement.GetProperty("caseId").GetString());
        Verify(document.RootElement);
    }

    [Fact]
    public void HarnessRejectsWrongPositiveOutcomeAndDamagedSemanticValue()
    {
        var first = Load(File.ReadAllText(FixturePath, Utf8)).First(c => Text(c.GetProperty("expected").GetProperty("outcome")) == "OK");
        var wrong = JsonNode.Parse(first.GetRawText());
        wrong["expected"] = JsonNode.Parse("{\"outcome\":\"DOCUMENT_INVALID\",\"violation\":\"FAKE\"}");
        Assert.NotNull(Record.Exception(() => Verify(Element(wrong))));
        var damaged = JsonNode.Parse(first.GetRawText());
        damaged["expected"]["semanticLog"][0][9] = new string('1', 64);
        Assert.NotNull(Record.Exception(() => Verify(Element(damaged))));
        // Demonstrates the positive assertion kills an all-reject implementation.
        Assert.NotNull(Record.Exception(() => Verify(first, (_, _, _) => SkinLaunchDescriptorParser.Parse(Array.Empty<byte>(), new string('0', 64), null))));
    }

    [Fact]
    public void HarnessRejectsMalformedFixtureAndCorruptionBeforeParserInvocation()
    {
        string text = File.ReadAllText(FixturePath, Utf8);
        foreach (string malformed in new[] { "{}", "{\"schemaVersion\":1,\"cases\":[]}", "{\"schemaVersion\":1,\"schemaVersion\":1,\"cases\":[]}" })
            Assert.NotNull(Record.Exception(() => Load(malformed)));
        foreach (Action<JsonNode> mutate in new Action<JsonNode>[] {
            root => root["unused"] = 0,
            root => root["schemaVersion"] = true,
            root => root["cases"].AsArray().Add(root["cases"][0].DeepClone()),
            root => root["cases"][0]["oracleContract"] = "future",
            root => root["cases"][0]["unused"] = 0,
            root => root["cases"][0]["expectations"].AsObject().Remove("leaseId"),
            root => root["cases"][0]["expected"]["semanticLog"] = new JsonArray(),
        })
        {
            var root = JsonNode.Parse(text); mutate(root);
            Assert.NotNull(Record.Exception(() => Load(root.ToJsonString())));
        }
        var corrupt = JsonNode.Parse(Load(text)[0].GetRawText());
        corrupt["input"]["utf8Text"] = corrupt["input"]["utf8Text"].GetValue<string>() + "\n";
        bool called = false;
        Assert.NotNull(Record.Exception(() => Verify(Element(corrupt), (b, h, e) => { called = true; return SkinLaunchDescriptorParser.Parse(b, h, e); })));
        Assert.False(called);
    }

    [Fact]
    public void HarnessObservesEveryPackObjectTextureFieldAndRecord()
    {
        foreach (string caseId in new[] { "clear-receipt-only-retained", "interlock-armed-distinct", "interlock-rollback-distinct", "interlock-selected-not-active", "interlock-repeated-receipt-retained" })
        {
        var source = Load(File.ReadAllText(FixturePath, Utf8)).Single(c => Text(c.GetProperty("caseId")) == caseId);
        var records = source.GetProperty("expected").GetProperty("semanticLog");
        for (int i = 0; i < records.GetArrayLength(); i++)
        {
            for (int j = 0; j < records[i].GetArrayLength(); j++)
            {
                var damaged = JsonNode.Parse(source.GetRawText());
                var value = records[i][j];
                JsonNode changed = value.ValueKind == JsonValueKind.Number ? JsonValue.Create(value.GetInt32() + 1)
                    : value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False ? JsonValue.Create(!value.GetBoolean())
                    : JsonValue.Create("damaged");
                damaged["expected"]["semanticLog"][i][j] = changed;
                Assert.NotNull(Record.Exception(() => Verify(Element(damaged))));
            }
            var omitted = JsonNode.Parse(source.GetRawText());
            omitted["expected"]["semanticLog"].AsArray().RemoveAt(i);
            Assert.NotNull(Record.Exception(() => Verify(Element(omitted))));
        }
        var reordered = JsonNode.Parse(source.GetRawText());
        var log = reordered["expected"]["semanticLog"].AsArray();
        var first = log[6].DeepClone(); log[6] = log[9].DeepClone(); log[9] = first;
        Assert.NotNull(Record.Exception(() => Verify(Element(reordered))));
        var wrong = JsonNode.Parse(source.GetRawText());
        wrong["expected"] = JsonNode.Parse("{\"outcome\":\"DOCUMENT_INVALID\",\"violation\":\"FAKE\"}");
        Assert.NotNull(Record.Exception(() => Verify(Element(wrong))));
        Assert.NotNull(Record.Exception(() => Verify(source, (_, _, _) => SkinLaunchDescriptorParser.Parse(Array.Empty<byte>(), new string('0', 64), null))));
        var appended = JsonNode.Parse(source.GetRawText()); appended["expected"]["semanticLog"].AsArray().Add(new JsonArray("unknown"));
        Assert.NotNull(Record.Exception(() => Verify(Element(appended))));
        }
    }

    [Fact]
    public void InterlockLogShapeFailsBeforeParser()
    {
        var source = Load(File.ReadAllText(FixturePath, Utf8)).Single(c => Text(c.GetProperty("caseId")) == "interlock-rollback-distinct");
        foreach (var change in new (int Row, int Column, JsonNode Value)[] {
            (5, 1, JsonValue.Create("bad-uuid")), (5, 2, JsonValue.Create("UNKNOWN")), (5, 4, JsonValue.Create("bad-hash")),
            (6, 4, JsonValue.Create(91)), (8, 4, JsonValue.Create("092")), (10, 2, JsonValue.Create(0)),
            (10, 1, JsonValue.Create(" x")), (11, 1, JsonValue.Create("lower")), (11, 2, JsonValue.Create(new string('A', 129))) })
        {
            var bad = JsonNode.Parse(source.GetRawText()); bad["expected"]["semanticLog"][change.Row][change.Column] = change.Value;
            bool called = false;
            Assert.NotNull(Record.Exception(() => Verify(Element(bad), (b, h, e) => { called = true; return SkinLaunchDescriptorParser.Parse(b, h, e); })));
            Assert.False(called);
        }
    }

    [Fact]
    public void CorpusHasBothOutcomesAndNoUnhandledCases()
    {
        var cases = Load(File.ReadAllText(FixturePath, Utf8));
        Assert.True(cases.Count >= 25);
        Assert.True(cases.Count(c => Text(c.GetProperty("expected").GetProperty("outcome")) == "OK") >= 5);
        Assert.Contains(cases, c => Text(c.GetProperty("expected").GetProperty("outcome")) == "DOCUMENT_INVALID");
    }

    private static JsonElement Element(JsonNode node)
    {
        using var doc = JsonDocument.Parse(node.ToJsonString());
        return doc.RootElement.Clone();
    }

    private static List<JsonElement> Load(string text)
    {
        using var doc = JsonDocument.Parse(text);
        ClosedTree(doc.RootElement);
        var root = doc.RootElement;
        Fields(root, "schemaVersion cases");
        Number(root.GetProperty("schemaVersion"), 1);
        var cases = root.GetProperty("cases");
        Assert.Equal(JsonValueKind.Array, cases.ValueKind);
        Assert.True(cases.GetArrayLength() > 0, "No handled cases");
        var result = new List<JsonElement>(); var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in cases.EnumerateArray())
        {
            ValidateCase(item);
            Assert.True(ids.Add(Text(item.GetProperty("caseId"))), "Duplicate case ID");
            result.Add(item.Clone());
        }
        return result;
    }

    private static void ClosedTree(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                Assert.True(keys.Add(property.Name), "Duplicate decoded fixture key");
                ClosedTree(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) ClosedTree(item);
    }

    private static void Fields(JsonElement value, string names)
    {
        Assert.Equal(JsonValueKind.Object, value.ValueKind);
        Assert.Equal(names.Split(' ').OrderBy(x => x, StringComparer.Ordinal), value.EnumerateObject().Select(p => p.Name).OrderBy(x => x, StringComparer.Ordinal));
    }
    private static string Text(JsonElement value)
    {
        Assert.Equal(JsonValueKind.String, value.ValueKind);
        string result = value.GetString(); Assert.False(string.IsNullOrEmpty(result)); return result;
    }
    private static void Number(JsonElement value, int expected)
    {
        Assert.Equal(JsonValueKind.Number, value.ValueKind);
        Assert.Equal(expected.ToString(CultureInfo.InvariantCulture), value.GetRawText());
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static byte[] ValidateCase(JsonElement c)
    {
        ClosedTree(c);
        Fields(c, "caseId oracleContract input inputSha256 expectedSha256 expectations expected");
        Text(c.GetProperty("caseId")); Assert.Contains(Text(c.GetProperty("oracleContract")), new[] { Contract, "descriptor-clear-v1", "descriptor-interlock-v1" });
        var input = c.GetProperty("input"); Assert.Equal(JsonValueKind.Object, input.ValueKind);
        Assert.Single(input.EnumerateObject());
        byte[] bytes;
        if (input.TryGetProperty("utf8Text", out var text))
        {
            Fields(input, "utf8Text"); Assert.Equal(JsonValueKind.String, text.ValueKind); bytes = Utf8.GetBytes(text.GetString());
        }
        else
        {
            Fields(input, "utf8Hex"); var hex = input.GetProperty("utf8Hex"); Assert.Equal(JsonValueKind.String, hex.ValueKind);
            Assert.Matches("\\A(?:[0-9a-f]{2})*\\z", hex.GetString()); bytes = Convert.FromHexString(hex.GetString());
        }
        Assert.Matches(Digest, Text(c.GetProperty("inputSha256")));
        Assert.Equal(Text(c.GetProperty("inputSha256")), Hash(bytes));
        Assert.Matches(Digest, Text(c.GetProperty("expectedSha256")));
        var e = c.GetProperty("expectations"); Fields(e, "descriptorId profileId gameVersion catalogId catalogSha256 leaseId");
        foreach (var p in e.EnumerateObject()) Text(p.Value);
        Assert.Matches(Uuid, Text(e.GetProperty("descriptorId"))); Assert.Matches(Uuid, Text(e.GetProperty("leaseId")));
        Assert.Matches(Digest, Text(e.GetProperty("catalogSha256")));
        var expected = c.GetProperty("expected");
        string outcome = Text(expected.GetProperty("outcome"));
        if (outcome == "OK")
        {
            Fields(expected, "outcome semanticLog"); var log = expected.GetProperty("semanticLog");
            Assert.Equal(JsonValueKind.Array, log.ValueKind); Assert.True(log.GetArrayLength() >= 5);
            if (Text(c.GetProperty("oracleContract")) == Contract)
            {
            Assert.Equal(5, log.GetArrayLength());
            var lengths = new[] { 12, 4, 3, 2, 2 }; var tags = new[] { "root", "activation", "visual", "interlock", "packs" };
            for (int i = 0; i < 5; i++) { Assert.Equal(JsonValueKind.Array, log[i].ValueKind); Assert.Equal(lengths[i], log[i].GetArrayLength()); Assert.Equal(tags[i], Text(log[i][0])); }
            Number(log[0][1], 1); for (int i = 2; i < 12; i++) Text(log[0][i]);
            Assert.Contains(Text(log[1][1]), new[] { "OFF", "ON", "ROTATE" }); Assert.Equal(JsonValueKind.Null, log[1][2].ValueKind); Text(log[1][3]);
            Assert.Equal("activation", Text(log[2][1])); Assert.Equal("VANILLA", Text(log[2][2])); Assert.Equal("CLEAR", Text(log[3][1])); Number(log[4][1], 0);
            }
            else if (Text(c.GetProperty("oracleContract")) == "descriptor-interlock-v1") ValidateInterlockLog(log);
            else ValidateClearLog(log);
        }
        else
        {
            Fields(expected, "outcome violation"); Assert.Equal("DOCUMENT_INVALID", outcome); Assert.Matches("\\A[A-Z][A-Z0-9_]*\\z", Text(expected.GetProperty("violation")));
        }
        return bytes;
    }

    private static void ValidateInterlockLog(JsonElement log)
    {
        int cursor = 0;
        JsonElement Row(string tag, string kinds)
        {
            Assert.True(cursor < log.GetArrayLength(), "Missing record"); var row = log[cursor++];
            Assert.Equal(JsonValueKind.Array, row.ValueKind); Assert.Equal(kinds.Length + 1, row.GetArrayLength()); Assert.Equal(tag, Text(row[0]));
            for (int i = 0; i < kinds.Length; i++)
            {
                var value = row[i + 1];
                switch (kinds[i])
                {
                    case 's': Text(value); break;
                    case '?': if (value.ValueKind != JsonValueKind.Null) Assert.Matches("\\A[a-z0-9](?:[a-z0-9._-]{0,62}[a-z0-9])?\\z", Text(value)); break;
                    case 'i': Assert.Equal(JsonValueKind.Number, value.ValueKind); Assert.True(value.TryGetInt32(out int n) && n >= 0); Number(value, value.GetInt32()); break;
                    case 'b': Assert.True(value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False); break;
                    case 'd': string text = Text(value); Assert.Matches("\\A(?:0|[1-9][0-9]*)\\z", text); Assert.True(long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out _)); break;
                }
            }
            return row;
        }
        void Mode(JsonElement value) => Assert.Contains(Text(value), new[] { "OFF", "ON", "ROTATE" });
        void Visual(string owner)
        {
            Assert.True(cursor < log.GetArrayLength()); var next = log[cursor]; Assert.Equal(JsonValueKind.Array, next.ValueKind); Assert.True(next.GetArrayLength() >= 3);
            string kind = Text(next[2]); Assert.Contains(kind, new[] { "VANILLA", "PACK" });
            var row = Row("visual", kind == "PACK" ? "ssssss" : "ss"); Assert.Equal(owner, Text(row[1]));
            if (kind == "PACK") { Assert.Matches("\\A[a-z0-9](?:[a-z0-9._-]{0,62}[a-z0-9])?\\z", Text(row[3])); for (int j = 4; j <= 6; j++) Assert.Matches(Digest, Text(row[j])); }
        }
        var root = Row("root", "isdssssssss"); Number(root[1], 1);
        var activation = Row("activation", "s?d"); Mode(activation[1]); Visual("activation");
        string state = Text(Row("interlock", "s")[1]); Assert.Contains(state, new[] { "CLEAR", "ARMED", "ROLLBACK_FAILED" });
        int count = Row("packs", "i")[1].GetInt32(); Assert.InRange(count, 0, 64);
        if (state != "CLEAR")
        {
            var transaction = Row("transaction", "ssss"); Assert.Matches(Uuid, Text(transaction[1])); Assert.Matches(Uuid, Text(transaction[3])); Assert.Matches(Digest, Text(transaction[4]));
            Assert.Contains(Text(transaction[2]), new[] { "STARTUP_APPLY", "MODE_ON", "MODE_OFF", "DEATH_ROTATION", "REBIND_APPLY" });
            foreach (string owner in new[] { "prior", "target" }) { var snapshot = Row("snapshot", "ss?d"); Assert.Equal(owner, Text(snapshot[1])); Mode(snapshot[2]); Visual(owner); }
            string binding = Text(Row("binding", "sb")[1]);
            Assert.Equal(binding, binding.Normalize(NormalizationForm.FormKC));
            int scalars = 0; var runes = binding.EnumerateRunes().ToArray();
            foreach (var rune in runes) { scalars++; Assert.False(rune.Value < 32 || rune.Value >= 127 && rune.Value <= 159 || new[] { 0x61c, 0x200e, 0x200f, 0x202a, 0x202b, 0x202c, 0x202d, 0x202e, 0x2066, 0x2067, 0x2068, 0x2069 }.Contains(rune.Value)); }
            Assert.InRange(scalars, 1, 256);
            bool Edge(Rune r) => Rune.IsWhiteSpace(r) || r.Value >= 28 && r.Value <= 31;
            Assert.False(Edge(runes[0]) || Edge(runes[^1]));
            if (state == "ROLLBACK_FAILED") { var failures = Row("failures", "ss"); for (int i = 1; i <= 2; i++) Assert.Matches("\\A[A-Z][A-Z0-9_]{0,127}\\z", Text(failures[i])); }
        }
        for (int i = 0; i < count; i++)
        {
            var pack = Row("pack", "issssbb"); Number(pack[1], i);
            foreach (string owner in pack[7].GetBoolean() ? new[] { "current", "retained" } : new[] { "current" })
            {
                var obj = Row("object", "isssssss"); Number(obj[1], i); Assert.Equal(owner, Text(obj[2]));
                var textures = Row("textures", "isi"); Number(textures[1], i); Assert.Equal(owner, Text(textures[2])); int length = textures[3].GetInt32(); Assert.InRange(length, 1, 205);
                for (int j = 0; j < length; j++) { var texture = Row("texture", "isiisssd"); Number(texture[1], i); Assert.Equal(owner, Text(texture[2])); Number(texture[3], j); Assert.InRange(texture[4].GetInt32(), 0, 204); }
            }
        }
        Assert.Equal(log.GetArrayLength(), cursor);
    }

    private static void ValidateClearLog(JsonElement log)
    {
        // Verify compares every record and value against independent typed projection.
        var sizes = new Dictionary<string, int> { ["root"] = 12, ["activation"] = 4, ["interlock"] = 2, ["packs"] = 2, ["pack"] = 8, ["object"] = 9, ["textures"] = 4, ["texture"] = 9 };
        foreach (var row in log.EnumerateArray())
        {
            Assert.Equal(JsonValueKind.Array, row.ValueKind); Assert.True(row.GetArrayLength() > 0);
            string tag = Text(row[0]);
            if (tag == "visual") Assert.Contains(row.GetArrayLength(), new[] { 3, 7 });
            else { Assert.True(sizes.ContainsKey(tag), "Unknown log tag"); Assert.Equal(sizes[tag], row.GetArrayLength()); }
        }
    }

    private static void Verify(JsonElement c, Func<byte[], string, DescriptorExpectations, DescriptorParseResult> parse = null)
    {
        var bytes = ValidateCase(c); var e = c.GetProperty("expectations");
        var expectations = new DescriptorExpectations(Guid.ParseExact(Text(e.GetProperty("descriptorId")), "D"), Text(e.GetProperty("profileId")), Text(e.GetProperty("gameVersion")), Text(e.GetProperty("catalogId")), Text(e.GetProperty("catalogSha256")), Guid.ParseExact(Text(e.GetProperty("leaseId")), "D"));
        var result = (parse ?? SkinLaunchDescriptorParser.Parse)(bytes, Text(c.GetProperty("expectedSha256")), expectations);
        var expected = c.GetProperty("expected"); bool ok = Text(expected.GetProperty("outcome")) == "OK";
        Assert.Equal(ok, result.Success);
        if (!ok) { Assert.Equal("DOCUMENT_INVALID", result.Code); Assert.Null(result.Descriptor); return; }
        Assert.Null(result.Code); Assert.NotNull(result.Descriptor);
        var d = result.Descriptor; var a = d.Activation;
        if (Text(c.GetProperty("oracleContract")) != "descriptor-interlock-v1") Assert.Equal(InterlockState.CLEAR, a.RotationInterlock.State);
        var actual = new List<object[]> {
            new object[] { "root", d.SchemaVersion, d.DescriptorId.ToString("D"), d.SessionSequence.ToString(CultureInfo.InvariantCulture), d.ProfileId, d.GameVersion, d.CatalogId, d.CatalogSha256, d.RegistryGenerationId, d.RegistryGenerationSha256, d.LeaseId.ToString("D"), d.LeaseTokenSha256 },
            new object[] { "activation", a.Mode.ToString(), a.SelectedPackId, a.SkinStamp.ToString(CultureInfo.InvariantCulture) },
            a.Active is ActiveVisual.Pack v ? new object[] { "visual", "activation", "PACK", v.Id, v.TreeSha256, v.ContentSha256, v.ImportReceiptSha256 } : new object[] { "visual", "activation", "VANILLA" }, new object[] { "interlock", a.RotationInterlock.State.ToString() }, new object[] { "packs", d.Packs.Count }
        };
        var interlock = a.RotationInterlock;
        if (interlock.State != InterlockState.CLEAR)
        {
            actual.Add(new object[] { "transaction", interlock.TransactionId, interlock.Operation.Value.ToString(), interlock.BaseGenerationId, interlock.BaseGenerationSha256 });
            void Snapshot(ActivationSnapshot s, string owner)
            {
                actual.Add(new object[] { "snapshot", owner, s.Mode.ToString(), s.SelectedPackId, s.SkinStamp.ToString(CultureInfo.InvariantCulture) });
                actual.Add(s.Active is ActiveVisual.Pack visual ? new object[] { "visual", owner, "PACK", visual.Id, visual.TreeSha256, visual.ContentSha256, visual.ImportReceiptSha256 } : new object[] { "visual", owner, "VANILLA" });
            }
            Snapshot(interlock.Prior, "prior"); Snapshot(interlock.Target, "target");
            actual.Add(new object[] { "binding", interlock.BindingToken.Value, interlock.PriorEstablishedOnBinding.Value });
            if (interlock.State == InterlockState.ROLLBACK_FAILED) actual.Add(new object[] { "failures", interlock.OriginalFailure, interlock.RollbackFailure });
        }
        for (int i = 0; i < d.Packs.Count; i++)
        {
            var p = d.Packs[i];
            actual.Add(new object[] { "pack", i, p.Id, p.Name, p.Author, p.CandidateKey, p.RotationEligible, p.RetainedActiveObject != null });
            void ProjectObject(DescriptorObjectEnvelope o, string label)
            {
                actual.Add(new object[] { "object", i, label, o.ObjectRoot, o.ReceiptPath, o.TreeSha256, o.ContentSha256, o.ManifestSha256, o.ImportReceiptSha256 });
                actual.Add(new object[] { "textures", i, label, o.Textures.Count });
                for (int j = 0; j < o.Textures.Count; j++)
                {
                    var t = o.Textures[j];
                    actual.Add(new object[] { "texture", i, label, j, t.Ordinal, t.Target, t.SourceRelativePath, t.SourceSha256, t.Length.ToString(CultureInfo.InvariantCulture) });
                }
            }
            ProjectObject(p.CurrentObject, "current");
            if (p.RetainedActiveObject != null) ProjectObject(p.RetainedActiveObject, "retained");
        }
        var log = expected.GetProperty("semanticLog");
        Assert.Equal(actual.Count, log.GetArrayLength());
        for (int i = 0; i < actual.Count; i++)
        {
            Assert.Equal(actual[i].Length, log[i].GetArrayLength());
            for (int j = 0; j < actual[i].Length; j++)
            {
                if (actual[i][j] == null) Assert.Equal(JsonValueKind.Null, log[i][j].ValueKind);
                else if (actual[i][j] is int number) Number(log[i][j], number);
                else if (actual[i][j] is bool flag) Assert.Equal(flag ? JsonValueKind.True : JsonValueKind.False, log[i][j].ValueKind);
                else Assert.Equal((string)actual[i][j], Text(log[i][j]));
            }
        }
    }
}
