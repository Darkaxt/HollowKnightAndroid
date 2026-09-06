using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DualSouls.Skins.HollowKnight.Core;
using Xunit;

namespace SharedPatches.Tests;

// Hand-built canonical fixtures: compatibility cases, not independent cross-language goldens.
public sealed class HollowKnightSkinLaunchDescriptorTests
{
    private const string CatalogId = "hk-custom-knight-v3.5.0-205";
    private const string CatalogSha = "258a7fa2b3a1a94d114eb73c39259dfa6853139017afced53ca3afa668a1372a";
    private const string Id = "12345678-1234-4234-8234-123456789abc";
    private const string Lease = "32345678-1234-4234-8234-123456789abc";
    private const string Generation = "22345678-1234-4234-8234-123456789abc";
    private static string H(char c) => new(c, 64);
    private static DescriptorExpectations Expected() => new(Guid.Parse(Id), "hollow-knight", "1.5.12620", CatalogId, CatalogSha, Guid.Parse(Lease));
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string Q(string value)
    {
        var b = new StringBuilder("\"");
        foreach (var c in value)
            b.Append(c switch { '"' => "\\\"", '\\' => "\\\\", '\b' => "\\b", '\t' => "\\t", '\n' => "\\n", '\f' => "\\f", '\r' => "\\r", _ => c < 32 ? "\\u" + ((int)c).ToString("x4") : c.ToString() });
        return b.Append('"').ToString();
    }
    private static string O(params (string Key, string Value)[] values) => "{" + string.Join(",", values.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => Q(x.Key) + ":" + x.Value)) + "}";
    private static string A(params string[] values) => "[" + string.Join(",", values) + "]";
    private static string Vanilla() => O(("kind", Q("VANILLA")));
    private static string Visual(string id = "alpha", char tree = 'a', char content = 'b', char receipt = 'c') => O(("kind", Q("PACK")), ("id", Q(id)), ("treeSha256", Q(H(tree))), ("contentSha256", Q(H(content))), ("importReceiptSha256", Q(H(receipt))));
    private static string Snapshot(string active = null, string selected = null, string stamp = "7", string mode = "ON")
    {
        var fields = new List<(string, string)> { ("active", active ?? Vanilla()), ("mode", Q(mode)), ("skinStamp", Q(stamp)) };
        if (selected != null) fields.Add(("selectedPackId", Q(selected)));
        return O(fields.ToArray());
    }
    private static string Lock(string prior, string target, string state = "ARMED", string binding = "binding/token\\allowed", string operation = "MODE_ON")
    {
        var fields = new List<(string, string)> { ("state", Q(state)), ("transactionId", Q(Id)), ("operation", Q(operation)), ("baseGenerationId", Q(Lease)), ("baseGenerationSha256", Q(H('8'))), ("prior", prior), ("target", target), ("bindingToken", Q(binding)), ("priorEstablishedOnBinding", "true") };
        if (state == "ROLLBACK_FAILED") { fields.Add(("originalFailure", Q("APPLY_FAILED"))); fields.Add(("rollbackFailure", Q("ROLLBACK_FAILED"))); }
        return O(fields.ToArray());
    }
    private static string Activation(string snapshot = null, string interlock = null)
    {
        snapshot ??= Snapshot(stamp: "0", mode: "OFF");
        var insert = "\"rotationInterlock\":" + (interlock ?? O(("state", Q("CLEAR")))) + ",";
        // rotationInterlock precedes selectedPackId/skinStamp, after mode.
        var pos = snapshot.IndexOf("\"selectedPackId\":", StringComparison.Ordinal);
        if (pos < 0) pos = snapshot.IndexOf("\"skinStamp\":", StringComparison.Ordinal);
        return snapshot.Insert(pos, insert);
    }
    // Known vector: SHA256 bytes all zero -> 52 'a' base32 digits.
    private static string Texture(int ordinal = 0, string target = "Knight.png", string length = "67") => O(("ordinal", ordinal.ToString(CultureInfo.InvariantCulture)), ("target", Q(target)), ("sourceRelativePath", Q("pack/assets/" + new string('a', 52))), ("sourceSha256", Q(H('0'))), ("length", Q(length)));
    private static string Envelope(char tree = 'a', char content = 'b', char receipt = 'c', string textures = null) => O(("objectRoot", Q("objects/sha256/" + new string(tree, 2) + "/" + H(tree))), ("receiptPath", Q("import-receipts/sha256/" + new string(receipt, 2) + "/" + H(receipt))), ("treeSha256", Q(H(tree))), ("contentSha256", Q(H(content))), ("manifestSha256", Q(H('d'))), ("importReceiptSha256", Q(H(receipt))), ("textures", textures ?? A(Texture())));
    private static string Pack(string id = "alpha", string name = "Alpha", string author = "Unknown", bool eligible = true, string current = null, string retained = null, string candidate = null)
    {
        var fields = new List<(string, string)> { ("id", Q(id)), ("name", Q(name)), ("author", Q(author)), ("candidateKey", Q(candidate ?? H('7'))), ("rotationEligible", eligible ? "true" : "false"), ("currentObject", current ?? Envelope()) };
        if (retained != null) fields.Add(("retainedActiveObject", retained));
        return O(fields.ToArray());
    }
    private static string Document(string packs = "[]", string activation = null, string sequence = "9") => O(("schemaVersion", "1"), ("descriptorId", Q(Id)), ("sessionSequence", Q(sequence)), ("profileId", Q("hollow-knight")), ("gameVersion", Q("1.5.12620")), ("catalogId", Q(CatalogId)), ("catalogSha256", Q(CatalogSha)), ("registryGenerationId", Q(Generation)), ("registryGenerationSha256", Q(H('6'))), ("activation", activation ?? Activation()), ("packs", packs), ("leaseId", Q(Lease)), ("leaseTokenSha256", Q(H('f'))));
    private static DescriptorParseResult Parse(string text, DescriptorExpectations expected = null)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return SkinLaunchDescriptorParser.Parse(bytes, Hash(bytes), expected ?? Expected());
    }
    private static SkinLaunchDescriptor Good(string text)
    {
        var result = Parse(text);
        Assert.True(result.Success);
        Assert.Null(result.Code);
        return Assert.IsType<SkinLaunchDescriptor>(result.Descriptor);
    }
    private static void Bad(string text)
    {
        var result = Parse(text);
        Assert.False(result.Success);
        Assert.Null(result.Descriptor);
        Assert.Equal("DOCUMENT_INVALID", result.Code);
    }

    [Fact] public void EmptyVanillaPreservesEveryRootField()
    {
        var d = Good(Document());
        Assert.Equal(1, d.SchemaVersion); Assert.Equal(Guid.Parse(Id), d.DescriptorId);
        Assert.Equal(9, d.SessionSequence); Assert.Equal("hollow-knight", d.ProfileId);
        Assert.Equal("1.5.12620", d.GameVersion); Assert.Equal(CatalogId, d.CatalogId); Assert.Equal(CatalogSha, d.CatalogSha256);
        Assert.Equal(Generation, d.RegistryGenerationId); Assert.Equal(H('6'), d.RegistryGenerationSha256);
        Assert.Equal(Guid.Parse(Lease), d.LeaseId); Assert.Equal(H('f'), d.LeaseTokenSha256);
        Assert.Empty(d.Packs); Assert.Equal(SkinMode.OFF, d.Activation.Mode); Assert.Null(d.Activation.SelectedPackId);
        Assert.IsType<ActiveVisual.Vanilla>(d.Activation.Active); Assert.Equal(0, d.Activation.SkinStamp); Assert.Equal(RotationInterlock.Clear(), d.Activation.RotationInterlock);
    }
    [Theory] [InlineData("ARMED")] [InlineData("ROLLBACK_FAILED")]
    public void CompleteInterlockAndRetainedEnvelope(string state)
    {
        var prior = Snapshot(Visual(receipt: '3'), "alpha");
        var target = Snapshot(Visual(), "alpha", "8", "ROTATE");
        var d = Good(Document(A(Pack(retained: Envelope(receipt: '3'))), Activation(prior, Lock(prior, target, state))));
        var l = d.Activation.RotationInterlock;
        Assert.Equal(state, l.State.ToString()); Assert.Equal(Id, l.TransactionId); Assert.Equal(Lease, l.BaseGenerationId);
        Assert.Equal(H('8'), l.BaseGenerationSha256); Assert.Equal(SkinOperationKind.MODE_ON, l.Operation);
        Assert.Equal("binding/token\\allowed", l.BindingToken.Value); Assert.True(l.PriorEstablishedOnBinding);
        Assert.Equal(7, l.Prior.SkinStamp); Assert.Equal(8, l.Target.SkinStamp);
        Assert.Equal(state == "ARMED" ? null : "APPLY_FAILED", l.OriginalFailure);
        Assert.Equal(state == "ARMED" ? null : "ROLLBACK_FAILED", l.RollbackFailure);
        var p = Assert.Single(d.Packs); Assert.Equal("alpha", p.Id); Assert.Equal("Alpha", p.Name); Assert.Equal("Unknown", p.Author);
        Assert.True(p.RotationEligible); Assert.Equal(H('7'), p.CandidateKey); Assert.Equal(H('3'), p.RetainedActiveObject.ImportReceiptSha256);
        Assert.Equal(H('d'), p.CurrentObject.ManifestSha256); Assert.Equal(H('b'), p.CurrentObject.ContentSha256);
        var t = Assert.Single(p.CurrentObject.Textures); Assert.Equal(0, t.Ordinal); Assert.Equal("Knight.png", t.Target); Assert.Equal(67, t.Length); Assert.Equal(H('0'), t.SourceSha256);
        Assert.Equal("pack/assets/" + new string('a', 52), t.SourceRelativePath);
    }
    [Fact] public void SnapshotOnlySelectionAndHistoryAreNotExecutionProof()
    {
        var prior = Snapshot(Visual(), "alpha", mode: "OFF");
        var target = Snapshot(Visual("beta"), "gamma", "8", "OFF");
        var packs = A(Pack(eligible: false), Pack("beta", "Beta", eligible: false, candidate: H('8')), Pack("gamma", "Gamma", eligible: false, candidate: H('9')));
        Good(Document(packs, Activation(prior, Lock(prior, target))));
        Bad(Document(A(Pack(eligible: false), Pack("beta", "Beta", eligible: false, candidate: H('8'))), Activation(prior, Lock(prior, target))));
        Good(Document(activation: Activation(Snapshot(mode: "ON"))));
    }
    [Theory] [InlineData("OFF")] [InlineData("ON")] [InlineData("ROTATE")]
    public void ModesAcceptVanillaHistory(string mode) => Good(Document(activation: Activation(Snapshot(mode: mode))));
    [Theory] [InlineData("STARTUP_APPLY")] [InlineData("MODE_ON")] [InlineData("MODE_OFF")] [InlineData("DEATH_ROTATION")] [InlineData("REBIND_APPLY")]
    public void EveryExactOperation(string operation)
    {
        var p = Snapshot(); Good(Document(activation: Activation(p, Lock(p, Snapshot(stamp: "8"), operation: operation))));
    }
    [Fact] public void AllSixExpectationsFailClosed()
    {
        var e = Expected(); var bytes = Encoding.UTF8.GetBytes(Document());
        var bad = new[] { new DescriptorExpectations(Guid.Empty,e.ProfileId,e.GameVersion,e.CatalogId,e.CatalogSha256,e.LeaseId), new(e.DescriptorId,"other",e.GameVersion,e.CatalogId,e.CatalogSha256,e.LeaseId), new(e.DescriptorId,e.ProfileId,"other",e.CatalogId,e.CatalogSha256,e.LeaseId), new(e.DescriptorId,e.ProfileId,e.GameVersion,"other",e.CatalogSha256,e.LeaseId), new(e.DescriptorId,e.ProfileId,e.GameVersion,e.CatalogId,H('0'),e.LeaseId), new(e.DescriptorId,e.ProfileId,e.GameVersion,e.CatalogId,e.CatalogSha256,Guid.Empty), new(e.DescriptorId,null,e.GameVersion,e.CatalogId,e.CatalogSha256,e.LeaseId), new(e.DescriptorId,e.ProfileId,null,e.CatalogId,e.CatalogSha256,e.LeaseId), new(e.DescriptorId,e.ProfileId,e.GameVersion,null,e.CatalogSha256,e.LeaseId), new(e.DescriptorId,e.ProfileId,e.GameVersion,e.CatalogId,null,e.LeaseId), null };
        foreach (var expectation in bad) { var r = SkinLaunchDescriptorParser.Parse(bytes, Hash(bytes), expectation); Assert.False(r.Success); Assert.Null(r.Descriptor); Assert.Equal("DOCUMENT_INVALID",r.Code); }
    }
    [Theory] [InlineData("0", true)] [InlineData("9223372036854775807", true)] [InlineData("9223372036854775808", false)] [InlineData("-1", false)] [InlineData("01", false)] [InlineData("+1", false)] [InlineData("1.0", false)] [InlineData("", false)]
    public void DecimalStrings(string value, bool accepted)
    {
        var text = Document(sequence: value); if (accepted) Good(text); else Bad(text);
        text = Document(activation: Activation(Snapshot(stamp: value))); if (accepted) Good(text); else Bad(text);
    }
    [Theory] [InlineData("1", true)] [InlineData("16777216", true)] [InlineData("0", false)] [InlineData("16777217", false)] [InlineData("9223372036854775807", false)]
    public void TextureLength(string length, bool accepted)
    { var text = Document(A(Pack(current: Envelope(textures: A(Texture(length: length)))))); if (accepted) Good(text); else Bad(text); }
    [Fact] public void StampIncrementAndOuterEquality()
    {
        var p = Snapshot(stamp: "9223372036854775806");
        Good(Document(activation: Activation(p, Lock(p, Snapshot(stamp: "9223372036854775807")))));
        p = Snapshot(stamp: "9223372036854775807"); Bad(Document(activation: Activation(p, Lock(p, Snapshot(stamp: "0")))));
        p = Snapshot(); Bad(Document(activation: Activation(p, Lock(p, Snapshot(stamp: "9")))));
        Bad(Document(activation: Activation(Snapshot(mode: "OFF"), Lock(p, Snapshot(stamp: "8")))));
    }
    [Theory] [InlineData("00000000-0000-0000-0000-000000000000")] [InlineData("ffffffff-ffff-ffff-ffff-ffffffffffff")]
    public void CanonicalUuidHasNoVersionOrNonzeroRestriction(string uuid)
    {
        var text = Document().Replace(Id,uuid).Replace(Lease,uuid).Replace(Generation,uuid);
        var r = Parse(text,new DescriptorExpectations(Guid.Parse(uuid),"hollow-knight","1.5.12620",CatalogId,CatalogSha,Guid.Parse(uuid)));
        Assert.True(r.Success);
    }
    [Fact] public void UppercaseAndShortUuidAreRejected()
    { Bad(Document().Replace(Id,Id.ToUpperInvariant())); Bad(Document().Replace(Generation,"1-1-1-1-1")); }
    [Fact] public void PackBoundOwnershipAndOrdering()
    {
        string[] Packs(int n) => Enumerable.Range(0,n).Select(i=>Pack("p"+i.ToString("D2"),"P"+i.ToString("D2"),candidate:i.ToString("x64"))).ToArray();
        Good(Document(A(Packs(64)))); Bad(Document(A(Packs(65))));
        Bad(Document(A(Pack(),Pack())));
        Bad(Document(A(Pack(),Pack("beta","Beta"))));
        Bad(Document(A(Pack("beta","Beta",candidate:H('8')),Pack())));
        Good(Document(A(Pack(name:"Same"),Pack("beta","Same",candidate:H('8')))));
        Bad(Document(A(Pack("beta","Same",candidate:H('8')),Pack(name:"Same"))));
    }
    [Fact] public void ExactUnionAndRetainedIdentityTriple()
    {
        Bad(Document(A(Pack(eligible:false))));
        Bad(Document(activation:Activation(Snapshot(selected:"missing"))));
        Bad(Document(activation:Activation(Snapshot(Visual()))));
        Bad(Document(A(Pack(retained:Envelope()))));
        Bad(Document(A(Pack(retained:Envelope(receipt:'3')))));
        var prior=Snapshot(Visual(receipt:'3')); var target=Snapshot(Visual(),stamp:"8");
        Bad(Document(A(Pack()),Activation(prior,Lock(prior,target))));
        Bad(Document(A(Pack(retained:Envelope(receipt:'4'))),Activation(prior,Lock(prior,target))));
        Good(Document(A(Pack(retained:Envelope(receipt:'3'))),Activation(prior,Lock(prior,target))));
        target=Snapshot(Visual(receipt:'4'),stamp:"8");
        Bad(Document(A(Pack(retained:Envelope(receipt:'3'))),Activation(prior,Lock(prior,target))));
    }
    [Fact] public void SparseAndCompletePinnedCatalog()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName,"docs/superpowers/specs/data/hollow-knight-skin-catalog-v1.txt"))) directory=directory.Parent;
        Assert.NotNull(directory);
        var bytes=File.ReadAllBytes(Path.Combine(directory.FullName,"docs/superpowers/specs/data/hollow-knight-skin-catalog-v1.txt"));
        // Repository checkout may use CRLF; normative bytes are explicitly LF including final LF.
        var source=Encoding.UTF8.GetString(bytes).Replace("\r\n","\n"); Assert.EndsWith("\n",source); Assert.Equal(CatalogSha,Hash(Encoding.UTF8.GetBytes(source)));
        var paths=source.Split('\n').SkipLast(1).ToArray(); Assert.Equal(205,paths.Length); Assert.Equal(205,paths.Distinct().Count());
        var textures=paths.Select((path,i)=>Texture(i,path)).ToArray();
        var parsed=Good(Document(A(Pack(current:Envelope(textures:A(textures))))));
        Assert.Equal(paths,parsed.Packs[0].CurrentObject.Textures.Select(x=>x.Target));
        Bad(Document(A(Pack(current:Envelope(textures:A(textures.Append(Texture()).ToArray()))))));
        Good(Document(A(Pack(current:Envelope(textures:A(Texture(),Texture(2,paths[2])))))));
        Bad(Document(A(Pack(current:Envelope(textures:A(Texture(),Texture(1,paths[2])))))));
        Bad(Document(A(Pack(current:Envelope(textures:A(Texture(2,paths[2]),Texture()))))));
        Bad(Document(A(Pack(current:Envelope(textures:"[]")))));
        Bad(Document(A(Pack(current:Envelope(textures:A(Texture(),Texture()))))));
    }
    [Theory] [InlineData("", false)] [InlineData(" Alpha", false)] [InlineData("Alpha ", false)] [InlineData("A/B", false)] [InlineData("A\\B", false)] [InlineData("Ａ", false)] [InlineData("é", false)] [InlineData("é", true)] [InlineData("A‮B", false)]    public void DisplayNormalization(string name,bool accepted)
    { var text=Document(A(Pack(name:name))); if(accepted)Good(text);else Bad(text); text=Document(A(Pack(author:name)));if(accepted)Good(text);else Bad(text); }
    [Fact] public void ScalarsNoncharactersAndUnsignedUtf8Order()
    {
        var scalar=char.ConvertFromUtf32(0x1f600); var eighty=string.Concat(Enumerable.Repeat(scalar,80));
        Good(Document(A(Pack(name:eighty,author:eighty)))); Bad(Document(A(Pack(name:eighty+scalar)))); Bad(Document(A(Pack(author:eighty+scalar))));
        var noncharacters=Enumerable.Range(0xfdd0,32).Concat(Enumerable.Range(0,17).SelectMany(p=>new[]{(p<<16)|0xfffe,(p<<16)|0xffff})).ToArray();
        Assert.Equal(66,noncharacters.Length);
        foreach(var cp in noncharacters) { var text="A"+char.ConvertFromUtf32(cp)+"B"; Good(Document(A(Pack(name:text,author:text)))); var p=Snapshot();Good(Document(activation:Activation(p,Lock(p,Snapshot(stamp:"8"),binding:text)))); }
        var bmp="";var supplementary=char.ConvertFromUtf32(0x10000);
        Good(Document(A(Pack(name:bmp),Pack("beta",supplementary,candidate:H('8')))));
        Bad(Document(A(Pack("beta",supplementary,candidate:H('8')),Pack(name:bmp))));
    }
    [Fact] public void ImmutableOwnedCollectionsAndInputSnapshot()
    {
        var bytes=Encoding.UTF8.GetBytes(Document(A(Pack())));var result=SkinLaunchDescriptorParser.Parse(bytes,Hash(bytes),Expected());Assert.True(result.Success);
        Array.Fill(bytes,(byte)'x');var d=result.Descriptor;Assert.Equal("alpha",d.Packs[0].Id);
        Assert.Throws<NotSupportedException>(()=>((IList<DescriptorPackEnvelope>)d.Packs).Clear());
        Assert.Throws<NotSupportedException>(()=>((IList<DescriptorTextureEnvelope>)d.Packs[0].CurrentObject.Textures).Clear());
        foreach(var type in new[]{typeof(SkinLaunchDescriptor),typeof(DescriptorActivation),typeof(DescriptorPackEnvelope),typeof(DescriptorObjectEnvelope),typeof(DescriptorTextureEnvelope),typeof(DescriptorParseResult)})
        { Assert.Empty(type.GetConstructors());Assert.All(type.GetProperties(),p=>Assert.Null(p.SetMethod)); }
    }
    private static string Canonical(System.Text.Json.Nodes.JsonNode node)
    {
        if (node == null) return "null";
        if (node is System.Text.Json.Nodes.JsonObject obj) return O(obj.Select(p => (p.Key, Canonical(p.Value))).ToArray());
        if (node is System.Text.Json.Nodes.JsonArray array) return A(array.Select(Canonical).ToArray());
        if (node.GetValueKind() == System.Text.Json.JsonValueKind.String) return Q(node.GetValue<string>());
        return node.ToJsonString();
    }
    private static System.Text.Json.Nodes.JsonObject At(System.Text.Json.Nodes.JsonNode root, string path)
    {
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
            root = root is System.Text.Json.Nodes.JsonArray ? root[int.Parse(part)] : root[part];
        return (System.Text.Json.Nodes.JsonObject)root;
    }
    private static string Mutate(string text, string path, Action<System.Text.Json.Nodes.JsonObject> change)
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(text); change(At(root,path)); return Canonical(root);
    }
    private static string FullFixture()
    {
        var prior=Snapshot(Visual(receipt:'3'),"alpha");var target=Snapshot(Visual(),"alpha","8");
        return Document(A(Pack(retained:Envelope(receipt:'3'))),Activation(prior,Lock(prior,target,"ROLLBACK_FAILED")));
    }
    public static IEnumerable<object[]> ClosedSchemaCases()
    {
        var text=FullFixture();
        var paths=new[]{"", "activation", "activation/active", "activation/rotationInterlock", "activation/rotationInterlock/prior", "activation/rotationInterlock/prior/active", "activation/rotationInterlock/target", "activation/rotationInterlock/target/active", "packs/0", "packs/0/currentObject", "packs/0/currentObject/textures/0", "packs/0/retainedActiveObject", "packs/0/retainedActiveObject/textures/0"};
        foreach(var path in paths)
        {
            yield return new object[]{path+" unknown",Mutate(text,path,o=>o["unknown"]=true)};
            foreach(var key in At(System.Text.Json.Nodes.JsonNode.Parse(text),path).Select(p=>p.Key).ToArray())
            {
                if(key!="selectedPackId" && key!="retainedActiveObject") yield return new object[]{path+" missing "+key,Mutate(text,path,o=>o.Remove(key))};
                yield return new object[]{path+" null "+key,Mutate(text,path,o=>o[key]=null)};
                yield return new object[]{path+" wrongtype "+key,Mutate(text,path,o=>o[key]=o[key] is System.Text.Json.Nodes.JsonValue v && v.GetValueKind()==System.Text.Json.JsonValueKind.True ? System.Text.Json.Nodes.JsonValue.Create("true") : System.Text.Json.Nodes.JsonValue.Create(false))};
            }
        }
    }
    [Theory] [MemberData(nameof(ClosedSchemaCases))]
    public void EveryNestedObjectIsClosedAndStrictlyTyped(string label,string invalid)
    { Assert.NotEmpty(label);Bad(invalid); }
    [Fact] public void ExactEnumsFailuresAndInterlockShapes()
    {
        var full=FullFixture();Good(full);
        foreach(var mode in new[]{"on","1","ON, OFF","UNKNOWN"}) Bad(Mutate(full,"activation",o=>o["mode"]=mode));
        foreach(var op in new[]{"mode_on","1","MODE_ON, MODE_OFF","UNKNOWN"}) Bad(Mutate(full,"activation/rotationInterlock",o=>o["operation"]=op));
        foreach(var state in new[]{"clear","0","UNKNOWN"}) Bad(Mutate(full,"activation/rotationInterlock",o=>o["state"]=state));
        foreach(var code in new[]{"", "lower", "A-B", "1CODE", "CODE\n", new string('A',129)})
        { Bad(Mutate(full,"activation/rotationInterlock",o=>o["originalFailure"]=code));Bad(Mutate(full,"activation/rotationInterlock",o=>o["rollbackFailure"]=code)); }
        Good(Mutate(full,"activation/rotationInterlock",o=>{o["originalFailure"]=new string('A',128);o["rollbackFailure"]="A0_";}));
        Bad(Mutate(full,"activation/rotationInterlock",o=>o["state"]="ARMED"));
        Bad(Mutate(full,"activation/rotationInterlock",o=>o["state"]="CLEAR"));
        Bad(Mutate(Document(),"activation/rotationInterlock",o=>o["target"]=System.Text.Json.Nodes.JsonNode.Parse(Snapshot())));
        Bad(Mutate(Document(),"activation/active",o=>o["id"]="alpha"));
        Bad(Mutate(full,"activation/active",o=>o["kind"]="pack"));
        Bad(Mutate(full,"activation/rotationInterlock/target",o=>o["skinStamp"]=8));
    }
    [Fact] public void PathsDigestsIdsAndBindingAreExact()
    {
        var text=Document(A(Pack()));
        foreach(var path in new[]{"/objects/sha256/aa/"+H('a'), "objects/sha256/bb/"+H('a'), "objects/sha256/aa/../aa/"+H('a'), "import-receipts/sha256/aa/"+H('a'), "objects\\sha256\\aa\\"+H('a')})
            Bad(Mutate(text,"packs/0/currentObject",o=>o["objectRoot"]=path));
        Bad(Mutate(text,"packs/0/currentObject",o=>o["receiptPath"]="objects/sha256/cc/"+H('c')));
        foreach(var source in new[]{"pack/assets/"+new string('a',52)+".png", "pack/assets/"+new string('A',52), "pack/assets/"+new string('a',51)+"b", "pack/assets/"+new string('a',52)+"=", "../pack/assets/"+new string('a',52)})
            Bad(Mutate(text,"packs/0/currentObject/textures/0",o=>o["sourceRelativePath"]=source));
        foreach(var target in new[]{"knight.png","../Knight.png","Knight.PNG","Charms\\Charm_0.png"}) Bad(Mutate(text,"packs/0/currentObject/textures/0",o=>o["target"]=target));
        foreach(var ordinal in new[]{-1,205,int.MaxValue}) Bad(Mutate(text,"packs/0/currentObject/textures/0",o=>o["ordinal"]=ordinal));
        foreach(var digest in new[]{H('A'),H('g'),new string('a',63),new string('a',65),H('a')+"\n"})
        {
            Bad(Mutate(text,"packs/0",o=>o["candidateKey"]=digest));
            foreach(var field in new[]{"treeSha256","contentSha256","manifestSha256","importReceiptSha256"}) Bad(Mutate(text,"packs/0/currentObject",o=>o[field]=digest));
            Bad(Mutate(text,"packs/0/currentObject/textures/0",o=>o["sourceSha256"]=digest));
            foreach(var field in new[]{"registryGenerationSha256","leaseTokenSha256","catalogSha256"}) Bad(Mutate(text,"",o=>o[field]=digest));
        }
        foreach(var id in new[]{"", "A", "-a", "a-", "a/b",new string('a',65),"a\n"}) Bad(Document(A(Pack(id:id))));
        foreach(var id in new[]{"a",new string('a',64),"a._-z"}) Good(Document(A(Pack(id:id))));
        foreach(var binding in new[]{"",new string('a',257)," x","x ","Ａ","A"+(char)0x85+"B","A"+(char)0x202e+"B"})
            Bad(Mutate(FullFixture(),"activation/rotationInterlock",o=>o["bindingToken"]=binding));
        Good(Mutate(FullFixture(),"activation/rotationInterlock",o=>o["bindingToken"]=new string('a',256)));
        Bad(Document(A(Pack(name:"A"+(char)0x85+"B"))));
        Bad(Mutate(text,"",o=>o["schemaVersion"]="1"));Bad(Mutate(text,"",o=>o["sessionSequence"]=9));
        Bad(Mutate(text,"packs/0/currentObject/textures/0",o=>o["length"]=67));
    }

    [Theory]
    [InlineData("000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", "aaaqeayeaudaocajbifqydiob4ibceqtcqkrmfyydenbwha5dypq")]
    [InlineData("ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", "777777777777777777777777777777777777777777777777777q")]
    [InlineData("eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee", "53xo53xo53xo53xo53xo53xo53xo53xo53xo53xo53xo53xo53xa")]
    public void NonzeroBase32DigestVectors(string digest, string encoded)
    {
        var text=Mutate(Document(A(Pack())),"packs/0/currentObject/textures/0",o=>{o["sourceSha256"]=digest;o["sourceRelativePath"]="pack/assets/"+encoded;});
        Good(text);
        Bad(Mutate(text,"packs/0/currentObject/textures/0",o=>o["sourceRelativePath"]="pack/assets/"+new string('a',52)));
        Bad(Mutate(text,"packs/0/currentObject/textures/0",o=>o["sourceRelativePath"]="pack/assets/"+encoded.Substring(0,51)+"r"));
    }
    [Fact] public void OuterActivationEqualsEveryPriorField()
    {
        var text=FullFixture();
        Bad(Mutate(text,"activation",o=>o["skinStamp"]="6"));
        Bad(Mutate(text,"activation",o=>o.Remove("selectedPackId")));
        Bad(Mutate(text,"activation/active",o=>o["importReceiptSha256"]=H('c')));
        Bad(Mutate(text,"activation/rotationInterlock/prior",o=>o["mode"]="ROTATE"));
        // Selected need not equal active even in the outer/prior history.
        var prior=Snapshot(Visual(),"beta");var target=Snapshot(Visual(),"beta","8");
        Good(Document(A(Pack(eligible:false),Pack("beta","Beta",eligible:false,candidate:H('8'))),Activation(prior,Lock(prior,target))));
    }
    [Fact] public void WireHistoryDoesNotInventObjectOrSourceUniqueness()
    {
        var textures=A(Texture(),Texture(2,"Unn.png"));
        var sameObject=Envelope(textures:textures);
        var d=Good(Document(A(Pack(current:sameObject),Pack("beta","Beta",current:sameObject,candidate:H('8')))));
        Assert.Equal(d.Packs[0].CurrentObject.TreeSha256,d.Packs[1].CurrentObject.TreeSha256);
        Assert.Equal(d.Packs[0].CurrentObject.Textures[0].SourceSha256,d.Packs[0].CurrentObject.Textures[1].SourceSha256);
    }
    [Fact] public void FixedAuthorityCannotBeReplacedByMatchingExpectations()
    {
        var e=Expected();
        foreach(var field in new[]{"profileId","gameVersion","catalogId","catalogSha256"})
        {
            var value=field=="catalogSha256"?H('0'):"other";
            var text=Mutate(Document(),"",o=>o[field]=value);
            var expected=new DescriptorExpectations(e.DescriptorId,field=="profileId"?value:e.ProfileId,field=="gameVersion"?value:e.GameVersion,field=="catalogId"?value:e.CatalogId,field=="catalogSha256"?value:e.CatalogSha256,e.LeaseId);
            var result=Parse(text,expected);Assert.False(result.Success);Assert.Null(result.Descriptor);Assert.Equal("DOCUMENT_INVALID",result.Code);
        }
    }
    [Fact] public void UuidAndBooleanInterlockControls()
    {
        var text=FullFixture();
        foreach(var field in new[]{"transactionId","baseGenerationId"})
        {
            Good(Mutate(text,"activation/rotationInterlock",o=>o[field]=Guid.Empty.ToString("D")));
            Good(Mutate(text,"activation/rotationInterlock",o=>o[field]="ffffffff-ffff-ffff-ffff-ffffffffffff"));
            Bad(Mutate(text,"activation/rotationInterlock",o=>o[field]=Id.ToUpperInvariant()));
        }
        Good(Mutate(text,"activation/rotationInterlock",o=>o["priorEstablishedOnBinding"]=false));
        var scalar=char.ConvertFromUtf32(0x1f600);var binding=string.Concat(Enumerable.Repeat(scalar,256));
        Good(Mutate(text,"activation/rotationInterlock",o=>o["bindingToken"]=binding));
        Bad(Mutate(text,"activation/rotationInterlock",o=>o["bindingToken"]=binding+scalar));
    }

    [Fact] public void ByteHashCanonicalBoundary()
    {
        var text=Document();var bytes=Encoding.UTF8.GetBytes(text);
        foreach(var hash in new[]{null,"",H('f'),Hash(bytes).ToUpperInvariant()}) {var r=SkinLaunchDescriptorParser.Parse(bytes,hash,Expected());Assert.False(r.Success);Assert.Equal("DOCUMENT_INVALID",r.Code);Assert.Null(r.Descriptor);}
        foreach(var input in new[]{(byte[])null,new byte[8388609],new byte[]{0xff},Encoding.UTF8.GetBytes("﻿"+text),Encoding.UTF8.GetBytes(text+"\n")})
        {var r=SkinLaunchDescriptorParser.Parse(input,input==null?H('0'):Hash(input),Expected());Assert.False(r.Success);Assert.Null(r.Descriptor);Assert.Equal("DOCUMENT_INVALID",r.Code);}
        Bad(text.Replace("\"schemaVersion\":1","\"schemaVersion\":1,\"schemaVersion\":1"));
        Bad(text.Replace("hollow-knight","hollow\\u002dknight"));
    }
}
