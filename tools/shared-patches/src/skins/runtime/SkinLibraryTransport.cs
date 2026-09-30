using System;
using System.Collections.Generic;
using System.Globalization;
using DualSouls.Skins.HollowKnight.Core;

namespace DualSouls.Skins.Runtime
{
    // The same bounded decoder is used by Android JNI and host transport contracts.
    public static class SkinLibraryTransport
    {
        public static SkinLibraryRequest Decode(string json, SkinRuntimeRules rules)
        {
            if (!StrictJson.TryParseTransport(json, 262144, out var root) || root.Kind != StrictJson.ValueKind.Object)
                throw new InvalidOperationException("Invalid skin configuration transport.");
            StrictJson Field(string key) => root.Members.TryGetValue(key, out var value) ? value :
                throw new InvalidOperationException("Missing skin transport field: " + key);
            string Text(string key) {
                var value = Field(key);
                if (value.Kind != StrictJson.ValueKind.String) throw new InvalidOperationException("Invalid skin transport string.");
                return value.Text;
            }
            bool Flag(string key) {
                var value = Field(key);
                if (value.Kind != StrictJson.ValueKind.Boolean) throw new InvalidOperationException("Invalid skin transport flag.");
                return value.Boolean;
            }
            long Number(string key) {
                var value = Field(key);
                if (value.Kind != StrictJson.ValueKind.Integer || !long.TryParse(value.Text, NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out long result)) throw new InvalidOperationException("Invalid skin transport integer.");
                return result;
            }
            if (!Flag("ok")) {
                string code = Text("code");
                if (code == "LIFECYCLE_BLOCKED") return null;
                throw new InvalidOperationException(code + ": " + Text("detail"));
            }
            long slot = Number("saveSlot"), last = Number("lastDeath"), pending = Number("pendingOccurrence");
            if (slot < -1 || slot > 4 || last < 0 || pending < 0) throw new InvalidOperationException("Invalid skin save/occurrence authority.");
            string profile = Text("profileId"), mode = Text("mode");
            if (profile != rules.ProfileId) throw new InvalidOperationException("Skin transport belongs to another profile.");
            var request = new SkinLibraryRequest { ProfileId = profile, SaveSlot = (int)slot, ConfigSha256 = Text("configSha256"),
                Mode = mode, SpriteScope = Text("spriteScope"), Vanilla = Flag("vanilla"),
                RotationRun = Text("rotationRun"), RotationDetail = Text("rotationDetail"), LastDeath = last, PendingOccurrence = pending };
            if (mode != "OFF" && !request.Vanilla) {
                request.PackId = Text("packId"); request.TreeSha256 = Text("treeSha256"); request.Root = Text("root");
                var mappings = Field("textures");
                if (mappings.Kind != StrictJson.ValueKind.Array || mappings.Items.Count < 1 || mappings.Items.Count > rules.MappingLimit)
                    throw new InvalidOperationException("Skin mapping bound exceeded.");
                var textures = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var item in mappings.Items) {
                    if (item.Kind != StrictJson.ValueKind.Object || item.Members.Count != 2 ||
                        !item.Members.TryGetValue("target", out var target) || target.Kind != StrictJson.ValueKind.String ||
                        !item.Members.TryGetValue("path", out var path) || path.Kind != StrictJson.ValueKind.String)
                        throw new InvalidOperationException("Invalid skin mapping.");
                    textures.Add(target.Text, path.Text);
                }
                request.Textures = textures;
            }
            return request;
        }
    }
}
