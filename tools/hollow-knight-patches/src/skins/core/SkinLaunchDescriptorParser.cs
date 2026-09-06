using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DualSouls.Skins.HollowKnight.Core
{
    // Pure wire validation. No content IO, live binding, durable receipt or runtime catalog authority.
    public static class SkinLaunchDescriptorParser
    {
        private const string ProfileId = "hollow-knight";
        private const string GameVersion = "1.5.12620";
        private const string CatalogId = "hk-custom-knight-v3.5.0-205";
        private const string CatalogSha256 = "258a7fa2b3a1a94d114eb73c39259dfa6853139017afced53ca3afa668a1372a";
        private static readonly Regex FailureCode = new Regex("\\A[A-Z][A-Z0-9_]{0,127}\\z");
        private static readonly IReadOnlyList<string> CatalogPaths = CreateCatalogPaths();

        public static DescriptorParseResult Parse(byte[] bytes, string expectedSha256, DescriptorExpectations expected)
        {
            if (expected == null || expected.ProfileId != ProfileId || expected.GameVersion != GameVersion ||
                expected.CatalogId != CatalogId || expected.CatalogSha256 != CatalogSha256 || CatalogPaths == null ||
                !StrictJson.TryParseCanonical(bytes, expectedSha256, out var json, out _) || json.ContainsNull)
                return new DescriptorParseResult(null);
            try
            {
                var descriptor = DecodeDescriptor(json);
                Require(descriptor.DescriptorId == expected.DescriptorId && descriptor.ProfileId == expected.ProfileId &&
                    descriptor.GameVersion == expected.GameVersion && descriptor.CatalogId == expected.CatalogId &&
                    descriptor.CatalogSha256 == expected.CatalogSha256 && descriptor.LeaseId == expected.LeaseId);
                ValidateUnion(descriptor);
                return new DescriptorParseResult(descriptor);
            }
            catch (InvalidDescriptorException)
            {
                return new DescriptorParseResult(null);
            }
        }

        private static SkinLaunchDescriptor DecodeDescriptor(StrictJson json)
        {
            var f = Fields(json, "schemaVersion descriptorId sessionSequence profileId gameVersion catalogId catalogSha256 registryGenerationId registryGenerationSha256 activation packs leaseId leaseTokenSha256");
            int schemaVersion = Integer(f["schemaVersion"]);
            Require(schemaVersion == 1);
            var descriptorId = Uuid(String(f["descriptorId"]));
            long sequence = Decimal(f["sessionSequence"]);
            string profile = String(f["profileId"]), game = String(f["gameVersion"]);
            string catalog = String(f["catalogId"]), catalogHash = Digest(f["catalogSha256"]);
            Require(profile == ProfileId && game == GameVersion && catalog == CatalogId && catalogHash == CatalogSha256);
            string generation = UuidText(f["registryGenerationId"]), generationHash = Digest(f["registryGenerationSha256"]);
            var activation = DecodeActivation(f["activation"]);
            var items = Array(f["packs"], 0, 64);
            var packs = new List<DescriptorPackEnvelope>(items.Count);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var candidates = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                var pack = DecodePack(item);
                Require(ids.Add(pack.Id) && candidates.Add(pack.CandidateKey));
                if (packs.Count != 0)
                {
                    var previous = packs[packs.Count - 1];
                    int order = RotationValues.Utf8Compare(previous.Name, pack.Name);
                    Require(order < 0 || order == 0 && RotationValues.Utf8Compare(previous.Id, pack.Id) < 0);
                }
                packs.Add(pack);
            }
            return new SkinLaunchDescriptor(schemaVersion, descriptorId, sequence, profile, game, catalog, catalogHash,
                generation, generationHash, activation, packs, Uuid(String(f["leaseId"])), Digest(f["leaseTokenSha256"]));
        }

        private static DescriptorPackEnvelope DecodePack(StrictJson json)
        {
            var f = Fields(json, "id name author candidateKey rotationEligible currentObject", "retainedActiveObject");
            string id = PackId(f["id"]), name = Display(f["name"]), author = Display(f["author"]);
            var current = DecodeObject(f["currentObject"]);
            var retained = f.TryGetValue("retainedActiveObject", out var old) ? DecodeObject(old) : null;
            Require(retained == null || Identity(retained) != Identity(current));
            return new DescriptorPackEnvelope(id, name, author, Digest(f["candidateKey"]), Boolean(f["rotationEligible"]), current, retained);
        }

        private static DescriptorObjectEnvelope DecodeObject(StrictJson json)
        {
            var f = Fields(json, "objectRoot receiptPath treeSha256 contentSha256 manifestSha256 importReceiptSha256 textures");
            string tree = Digest(f["treeSha256"]), content = Digest(f["contentSha256"]);
            string manifest = Digest(f["manifestSha256"]), receipt = Digest(f["importReceiptSha256"]);
            string root = String(f["objectRoot"]), receiptPath = String(f["receiptPath"]);
            Require(root == "objects/sha256/" + tree.Substring(0, 2) + "/" + tree);
            Require(receiptPath == "import-receipts/sha256/" + receipt.Substring(0, 2) + "/" + receipt);
            var items = Array(f["textures"], 1, CatalogPaths.Count);
            var textures = new List<DescriptorTextureEnvelope>(items.Count);
            int previous = -1;
            foreach (var item in items)
            {
                var texture = DecodeTexture(item);
                Require(texture.Ordinal > previous);
                previous = texture.Ordinal;
                textures.Add(texture);
            }
            return new DescriptorObjectEnvelope(root, receiptPath, tree, content, manifest, receipt, textures);
        }

        private static DescriptorTextureEnvelope DecodeTexture(StrictJson json)
        {
            var f = Fields(json, "ordinal target sourceRelativePath sourceSha256 length");
            int ordinal = Integer(f["ordinal"]);
            string target = String(f["target"]), source = Digest(f["sourceSha256"]), path = String(f["sourceRelativePath"]);
            long length = Decimal(f["length"]);
            Require(ordinal < CatalogPaths.Count && target == CatalogPaths[ordinal]);
            Require(length >= 1 && length <= 16777216);
            Require(path == "pack/assets/" + Base32(source));
            return new DescriptorTextureEnvelope(ordinal, target, path, source, length);
        }

        private static DescriptorActivation DecodeActivation(StrictJson json)
        {
            var f = Fields(json, "mode active skinStamp rotationInterlock", "selectedPackId");
            var snapshot = SnapshotFields(f);
            var interlock = DecodeInterlock(f["rotationInterlock"]);
            Require(interlock.State == InterlockState.CLEAR || snapshot == interlock.Prior);
            return new DescriptorActivation(snapshot, interlock);
        }

        private static ActivationSnapshot DecodeSnapshot(StrictJson json) =>
            SnapshotFields(Fields(json, "mode active skinStamp", "selectedPackId"));

        private static ActivationSnapshot SnapshotFields(IReadOnlyDictionary<string, StrictJson> f) =>
            new ActivationSnapshot(Mode(String(f["mode"])), f.TryGetValue("selectedPackId", out var selected) ? PackId(selected) : null,
                DecodeVisual(f["active"]), Decimal(f["skinStamp"]));

        private static ActiveVisual DecodeVisual(StrictJson json)
        {
            Require(json.Kind == StrictJson.ValueKind.Object && json.Members.TryGetValue("kind", out _));
            string kind = String(json.Members["kind"]);
            if (kind == "VANILLA")
            {
                Fields(json, "kind");
                return new ActiveVisual.Vanilla();
            }
            Require(kind == "PACK");
            var f = Fields(json, "kind id treeSha256 contentSha256 importReceiptSha256");
            return new ActiveVisual.Pack(PackId(f["id"]), Digest(f["treeSha256"]), Digest(f["contentSha256"]), Digest(f["importReceiptSha256"]));
        }

        private static RotationInterlock DecodeInterlock(StrictJson json)
        {
            Require(json.Kind == StrictJson.ValueKind.Object && json.Members.TryGetValue("state", out _));
            string state = String(json.Members["state"]);
            if (state == "CLEAR")
            {
                Fields(json, "state");
                return RotationInterlock.Clear();
            }
            Require(state == "ARMED" || state == "ROLLBACK_FAILED");
            bool failed = state == "ROLLBACK_FAILED";
            var f = Fields(json, "state transactionId operation baseGenerationId baseGenerationSha256 prior target bindingToken priorEstablishedOnBinding" +
                (failed ? " originalFailure rollbackFailure" : ""));
            var prior = DecodeSnapshot(f["prior"]);
            var target = DecodeSnapshot(f["target"]);
            Require(prior.SkinStamp < long.MaxValue && target.SkinStamp == prior.SkinStamp + 1);
            string binding = String(f["bindingToken"]);
            Require(RotationValues.Text(binding, 256));
            return new RotationInterlock(failed ? InterlockState.ROLLBACK_FAILED : InterlockState.ARMED,
                UuidText(f["transactionId"]), Operation(String(f["operation"])), UuidText(f["baseGenerationId"]), Digest(f["baseGenerationSha256"]),
                prior, target, new SkinBindingToken(binding), Boolean(f["priorEstablishedOnBinding"]),
                failed ? Failure(f["originalFailure"]) : null, failed ? Failure(f["rollbackFailure"]) : null);
        }

        // References from outer activation AND both snapshots define the exact pack union.
        // Identity is tree/content/receipt, not tree alone; no content bytes are available here.
        private static void ValidateUnion(SkinLaunchDescriptor descriptor)
        {
            var packs = new Dictionary<string, DescriptorPackEnvelope>(StringComparer.Ordinal);
            var required = new HashSet<string>(StringComparer.Ordinal);
            var visuals = new Dictionary<string, HashSet<(string Tree, string Content, string Receipt)>>(StringComparer.Ordinal);
            foreach (var pack in descriptor.Packs)
            {
                packs.Add(pack.Id, pack);
                if (pack.RotationEligible) required.Add(pack.Id);
            }
            void Reference(string selected, ActiveVisual visual)
            {
                if (selected != null) { Require(packs.ContainsKey(selected)); required.Add(selected); }
                if (visual is ActiveVisual.Pack active)
                {
                    Require(packs.ContainsKey(active.Id));
                    required.Add(active.Id);
                    if (!visuals.TryGetValue(active.Id, out var identities))
                    {
                        identities = new HashSet<(string, string, string)>();
                        visuals.Add(active.Id, identities);
                    }
                    identities.Add((active.TreeSha256, active.ContentSha256, active.ImportReceiptSha256));
                }
            }
            var activation = descriptor.Activation;
            Reference(activation.SelectedPackId, activation.Active);
            var interlock = activation.RotationInterlock;
            if (interlock.State != InterlockState.CLEAR)
            {
                Reference(interlock.Prior.SelectedPackId, interlock.Prior.Active);
                Reference(interlock.Target.SelectedPackId, interlock.Target.Active);
            }
            Require(required.SetEquals(packs.Keys));
            foreach (var pack in descriptor.Packs)
            {
                if (!visuals.TryGetValue(pack.Id, out var identities)) identities = new HashSet<(string, string, string)>();
                identities.Remove(Identity(pack.CurrentObject));
                if (identities.Count == 0) Require(pack.RetainedActiveObject == null);
                else Require(identities.Count == 1 && pack.RetainedActiveObject != null && identities.Contains(Identity(pack.RetainedActiveObject)));
            }
        }

        private static (string Tree, string Content, string Receipt) Identity(DescriptorObjectEnvelope value) =>
            (value.TreeSha256, value.ContentSha256, value.ImportReceiptSha256);

        private static IReadOnlyDictionary<string, StrictJson> Fields(StrictJson json, string required, string optional = "")
        {
            Require(json.Kind == StrictJson.ValueKind.Object);
            var allowed = new HashSet<string>(required.Split(' '), StringComparer.Ordinal);
            foreach (var key in allowed) Require(json.Members.ContainsKey(key));
            if (optional.Length != 0) foreach (var key in optional.Split(' ')) allowed.Add(key);
            foreach (var key in json.Members.Keys) Require(allowed.Contains(key));
            return json.Members;
        }
        private static IReadOnlyList<StrictJson> Array(StrictJson json, int minimum, int maximum)
        {
            Require(json.Kind == StrictJson.ValueKind.Array && json.Items.Count >= minimum && json.Items.Count <= maximum);
            return json.Items;
        }
        private static string String(StrictJson json) { Require(json.Kind == StrictJson.ValueKind.String); return json.Text; }
        private static bool Boolean(StrictJson json) { Require(json.Kind == StrictJson.ValueKind.Boolean); return json.Boolean; }
        private static int Integer(StrictJson json) { Require(json.TryGetNonnegativeInt32(out int value)); return value; }
        private static long Decimal(StrictJson json) { Require(json.TryGetNonnegativeInt64String(out long value)); return value; }
        private static string Digest(StrictJson json) { string text = String(json); Require(RotationValues.Digest(text)); return text; }
        private static string PackId(StrictJson json) { string text = String(json); Require(RotationValues.PackId(text)); return text; }
        private static string Display(StrictJson json)
        {
            string text = String(json);
            Require(RotationValues.Text(text, 80) && text.IndexOf('/') < 0 && text.IndexOf('\\') < 0);
            return text;
        }
        private static Guid Uuid(string text)
        {
            Require(text.Length == 36 && Guid.TryParseExact(text, "D", out _));
            Guid value = Guid.ParseExact(text, "D");
            Require(value.ToString("D") == text);
            return value;
        }
        private static string UuidText(StrictJson json) { string text = String(json); Uuid(text); return text; }
        private static string Failure(StrictJson json)
        {
            string text = String(json); Require(text.Length <= 128 && FailureCode.IsMatch(text)); return text;
        }
        private static SkinMode Mode(string text) => text switch
        {
            "OFF" => SkinMode.OFF, "ON" => SkinMode.ON, "ROTATE" => SkinMode.ROTATE,
            _ => throw new InvalidDescriptorException()
        };
        private static SkinOperationKind Operation(string text) => text switch
        {
            "STARTUP_APPLY" => SkinOperationKind.STARTUP_APPLY, "MODE_ON" => SkinOperationKind.MODE_ON,
            "MODE_OFF" => SkinOperationKind.MODE_OFF, "DEATH_ROTATION" => SkinOperationKind.DEATH_ROTATION,
            "REBIND_APPLY" => SkinOperationKind.REBIND_APPLY, _ => throw new InvalidDescriptorException()
        };
        private static string Base32(string digest)
        {
            const string alphabet = "abcdefghijklmnopqrstuvwxyz234567";
            const string hex = "0123456789abcdef";
            var output = new StringBuilder(52);
            int buffer = 0, bits = 0;
            for (int i = 0; i < digest.Length; i += 2)
            {
                buffer = (buffer << 8) | (hex.IndexOf(digest[i]) << 4) | hex.IndexOf(digest[i + 1]);
                bits += 8;
                while (bits >= 5) { bits -= 5; output.Append(alphabet[(buffer >> bits) & 31]); }
                buffer &= (1 << bits) - 1;
            }
            if (bits != 0) output.Append(alphabet[(buffer << (5 - bits)) & 31]);
            return output.ToString();
        }
        private sealed class InvalidDescriptorException : Exception { }
        private static void Require(bool condition) { if (!condition) throw new InvalidDescriptorException(); }

        private static IReadOnlyList<string> CreateCatalogPaths()
        {
            // Pinned ordinal/path data only, not strategy/bindingKey/maximumTargets qualification.
            byte[] digest;
            using (var sha = SHA256.Create()) digest = sha.ComputeHash(Encoding.UTF8.GetBytes(CatalogSource));
            var actual = new StringBuilder(64);
            foreach (byte b in digest) actual.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
            if (actual.ToString() != CatalogSha256 || !CatalogSource.EndsWith("\n", StringComparison.Ordinal)) return null;
            string[] paths = CatalogSource.Substring(0, CatalogSource.Length - 1).Split('\n');
            if (paths.Length != 205 || new HashSet<string>(paths, StringComparer.Ordinal).Count != 205) return null;
            return System.Array.AsReadOnly(paths);
        }

        // Exact UTF-8 LF source from docs/superpowers/specs/data/hollow-knight-skin-catalog-v1.txt.
        private const string CatalogSource =
            "Knight.png\nSprint.png\nUnn.png\nShade.png\nShadeOrb.png\nWraiths.png\nVoidSpells.png\nVS.png\nGeo.png\nHud.png\n" +
            "OrbFull.png\nLiquid.png\nQOrbs.png\nQOrbs2.png\nScrOrbs.png\nScrOrbs2.png\nDungRecharge.png\nSDCrystalBurst.png\nDoubleJFeather.png\nLeak.png\n" +
            "HitPt.png\nShadowDashBlobs.png\nDeathpt.png\nDDeathpt.png\nBaldur.png\nFluke.png\nGrimm.png\nShield.png\nWeaver.png\nHatchling.png\n" +
            "Compass.png\nBeam.png\nCloak.png\nShriek.png\nWings.png\nQuirrel.png\nWebbed.png\nDreamArrival.png\nDreamnail.png\nHornet.png\nBirthplace.png\n" +
            "Charms/Charm_0.png\nCharms/Charm_1.png\nCharms/Charm_2.png\nCharms/Charm_3.png\nCharms/Charm_4.png\nCharms/Charm_5.png\n" +
            "Charms/Charm_6.png\nCharms/Charm_7.png\nCharms/Charm_8.png\nCharms/Charm_9.png\nCharms/Charm_10.png\nCharms/Charm_11.png\n" +
            "Charms/Charm_12.png\nCharms/Charm_13.png\nCharms/Charm_14.png\nCharms/Charm_15.png\nCharms/Charm_16.png\nCharms/Charm_17.png\n" +
            "Charms/Charm_18.png\nCharms/Charm_19.png\nCharms/Charm_20.png\nCharms/Charm_21.png\nCharms/Charm_22.png\n" +
            "Charms/Charm_23_Broken.png\nCharms/Charm_23_Fragile.png\nCharms/Charm_23_Unbreakable.png\n" +
            "Charms/Charm_24_Broken.png\nCharms/Charm_24_Fragile.png\nCharms/Charm_24_Unbreakable.png\n" +
            "Charms/Charm_25_Broken.png\nCharms/Charm_25_Fragile.png\nCharms/Charm_25_Unbreakable.png\n" +
            "Charms/Charm_26.png\nCharms/Charm_27.png\nCharms/Charm_28.png\nCharms/Charm_29.png\nCharms/Charm_30.png\nCharms/Charm_31.png\n" +
            "Charms/Charm_32.png\nCharms/Charm_33.png\nCharms/Charm_34.png\nCharms/Charm_35.png\n" +
            "Charms/Charm_36_Black.png\nCharms/Charm_36_Full.png\nCharms/Charm_36_Left.png\nCharms/Charm_36_Right.png\n" +
            "Charms/Charm_37.png\nCharms/Charm_38.png\nCharms/Charm_39.png\nCharms/Charm_40_1.png\nCharms/Charm_40_2.png\nCharms/Charm_40_3.png\nCharms/Charm_40_4.png\nCharms/Charm_40_5.png\n" +
            "Inventory/Nail_1.png\nInventory/Nail_2.png\nInventory/Nail_3.png\nInventory/Nail_4.png\nInventory/Nail_5.png\n" +
            "Inventory/Heart_0.png\nInventory/Heart_1.png\nInventory/Heart_2.png\nInventory/Heart_3.png\nInventory/Heart_4.png\n" +
            "Inventory/Vessel_0.png\nInventory/Vessel_1.png\nInventory/Vessel_2.png\nInventory/Vessel_3.png\n" +
            "Inventory/Fireball_1.png\nInventory/Fireball_2.png\nInventory/Quake_1.png\nInventory/Quake_2.png\nInventory/Scream_1.png\nInventory/Scream_2.png\n" +
            "Inventory/Focus.png\nInventory/ArtBG.png\nInventory/DSlash.png\nInventory/GSlash.png\nInventory/CSlash.png\nInventory/DreamGate.png\n" +
            "Inventory/DreamNail_0.png\nInventory/DreamNail_1.png\nInventory/Geo.png\nInventory/GodFinder_0.png\nInventory/GodFinder_1.png\nInventory/GodFinder_2.png\n" +
            "Inventory/Cloak_1.png\nInventory/Cloak_2.png\nInventory/Claw.png\nInventory/CRHeart.png\nInventory/Lantern.png\nInventory/DJump.png\nInventory/Tear.png\n" +
            "Inventory/SlyKey.png\nInventory/ElegentKey.png\nInventory/WJournal.png\nInventory/HSeal.png\nInventory/Idol.png\nInventory/BlackEgg.png\nInventory/Flower_0.png\nInventory/Flower_1.png\n" +
            "Inventory/TramPass.png\nInventory/Ore.png\nInventory/CityKey.png\nInventory/LoveKey.png\nInventory/Brand.png\nInventory/RancidEgg.png\nInventory/SimpleKey.png\n" +
            "Inventory/Map.png\nInventory/Quill.png\nInventory/MapQuill.png\nInventory/RelicBG.png\nInventory/SpellBG.png\n" +
            "DeathNail.png\nDeathAsh.png\nBrummWave.png\nBrummShield.png\nFlowerBreak.png\nSalubra.png\nCharms/front.png\nCharms/back.png\n" +
            "SaveHud/geoIcon.png\nSaveHud/ggSoulOrb.png\nSaveHud/hardcoreSoulOrb.png\nSaveHud/normalHealth.png\nSaveHud/normalSoulOrb.png\nSaveHud/soulOrbIcon.png\n" +
            "SaveHud/steelHealth.png\nSaveHud/steelSoulOrb.png\nSaveHud/brokenSteelOrb.png\n" +
            "AreaBackgrounds/ABYSS.png\nAreaBackgrounds/BEASTS_DEN.png\nAreaBackgrounds/CITY.png\nAreaBackgrounds/CLIFFS.png\nAreaBackgrounds/COLOSSEUM.png\n" +
            "AreaBackgrounds/CROSSROADS.png\nAreaBackgrounds/DEEPNEST.png\nAreaBackgrounds/DREAM_WORLD.png\nAreaBackgrounds/FINAL_BOSS.png\nAreaBackgrounds/GODSEEKER_WASTE.png\n" +
            "AreaBackgrounds/GODS_GLORY.png\nAreaBackgrounds/GREEN_PATH.png\nAreaBackgrounds/HIVE.png\nAreaBackgrounds/KINGS_PASS.png\nAreaBackgrounds/KINGS_STATION.png\n" +
            "AreaBackgrounds/LURIENS_TOWER.png\nAreaBackgrounds/MANTIS_VILLAGE.png\nAreaBackgrounds/MINES.png\nAreaBackgrounds/MONOMON_ARCHIVE.png\nAreaBackgrounds/OUTSKIRTS.png\n" +
            "AreaBackgrounds/PALACE_GROUNDS.png\nAreaBackgrounds/QUEENS_STATION.png\nAreaBackgrounds/RESTING_GROUNDS.png\nAreaBackgrounds/ROYAL_GARDENS.png\nAreaBackgrounds/ROYAL_QUARTER.png\n" +
            "AreaBackgrounds/SHAMAN_TEMPLE.png\nAreaBackgrounds/SOUL_SOCIETY.png\nAreaBackgrounds/TOWN.png\nAreaBackgrounds/TRAM_LOWER.png\nAreaBackgrounds/TRAM_UPPER.png\n" +
            "AreaBackgrounds/WASTES.png\nAreaBackgrounds/WATERWAYS.png\nAreaBackgrounds/WHITE_PALACE.png\nAreaBackgrounds/defeatedBackground.png\n";
    }
}
