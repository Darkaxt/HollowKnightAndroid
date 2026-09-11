using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace DualSouls.Skins.Runtime
{
    public sealed class SkinRuntimeRules
    {
        readonly Func<string, bool> isSupported;
        readonly Func<string, string, bool> allows;
        public string ProfileId { get; }
        public int MappingLimit { get; }
        public bool RestoreBeforeRotation { get; }
        public SkinRuntimeRules(string profileId, int mappingLimit, Func<string, bool> isSupported,
            Func<string, string, bool> allows, bool restoreBeforeRotation = false)
        {
            if (string.IsNullOrWhiteSpace(profileId)) throw new ArgumentException("Profile ID is required.", nameof(profileId));
            if (mappingLimit < 1 || mappingLimit > 4096) throw new ArgumentOutOfRangeException(nameof(mappingLimit));
            ProfileId = profileId;
            MappingLimit = mappingLimit;
            RestoreBeforeRotation = restoreBeforeRotation;
            this.isSupported = isSupported ?? throw new ArgumentNullException(nameof(isSupported));
            this.allows = allows ?? throw new ArgumentNullException(nameof(allows));
        }
        public bool IsSupported(string target) => isSupported(target);
        public bool Allows(string mode, string target) => isSupported(target) && allows(mode, target);
    }

    // A caller-verified, immutable normalized object. No registry or automatic folder discovery.
    public sealed class SkinPack
    {
        public string Id { get; }
        public string Root { get; }
        public IReadOnlyDictionary<string, string> Textures { get; }
        public string Mode { get; }
        public SkinPack(string id, string root, IDictionary<string, string> textures, string mode = "ON")
        {
            if (mode != "ON" && mode != "ROTATE") throw new ArgumentException("Invalid skin visual policy.");
            Mode = mode;
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Pack ID is required.");
            Id = id; Root = Path.GetFullPath(root);
            var copy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in textures)
            {
                if (!SafeRelative(pair.Key) || !pair.Key.EndsWith(".png", StringComparison.Ordinal) || !SafeRelative(pair.Value))
                    throw new ArgumentException("Expected normalized target and payload paths.");
                copy.Add(pair.Key, pair.Value);
            }
            if (copy.Count == 0 || copy.Count > 205) throw new ArgumentException("Pack mapping count is invalid.");
            Textures = new ReadOnlyDictionary<string, string>(copy);
        }
        static bool SafeRelative(string path) => !string.IsNullOrEmpty(path) && !Path.IsPathRooted(path) &&
            path.IndexOfAny(new[] { '\\', ':', '\0' }) < 0 && path.Split('/').All(x => x.Length > 0 && x != "." && x != "..");
    }

    public enum SkinApplyStatus { Applied, Restored, Unchanged, AwaitingTargets, Cancelled, Rejected, Failed, RestoreFailed, Blocked }
    public sealed class SkinApplyResult
    {
        public SkinApplyStatus Status { get; }
        public string Detail { get; }
        public IReadOnlyList<string> UnsupportedTargets { get; }
        internal SkinApplyResult(SkinApplyStatus status, string detail = "", IEnumerable<string> unsupported = null)
        { Status = status; Detail = detail; UnsupportedTargets = new List<string>(unsupported ?? Array.Empty<string>()).AsReadOnly(); }
    }
    public interface ISkinTextureDecoder { SkinTexture Decode(byte[] bytes, int width, int height); }

    public sealed class SkinTexture
    {
        public object Value { get; }
        public int Width { get; }
        public int Height { get; }
        public bool DecodeSucceeded { get; }
        // RGBA32 readable CPU storage + GPU storage, no mipmaps. Not a driver/total-heap bound.
        public long AccountedBytes => checked((long)Width * Height * 8);
        readonly Action release; readonly Func<bool> released;
        readonly List<object> owned = new List<object>();
        bool releaseRequested;
        public SkinTexture(object value, int width, int height, Action release, Func<bool> released, bool decodeSucceeded = true)
        { Value = value ?? throw new ArgumentNullException(nameof(value)); Width = width; Height = height; this.release = release; this.released = released; DecodeSucceeded = decodeSucceeded; }
        public void Own(object value)
        {
            if (releaseRequested || owned.Count >= 128) throw new InvalidOperationException("Generated sprite bound exceeded or texture retiring.");
            owned.Add(value);
        }
        internal bool Owns(object value) => ReferenceEquals(Value, value) || owned.Any(x => ReferenceEquals(x, value));
        internal bool Released => releaseRequested && released();
        internal bool ReleaseRequested => releaseRequested;
        internal void Release() { if (releaseRequested) return; release(); releaseRequested = true; }
    }

    // Equality identifies the actual Unity owner and member, not a sheet name or scene counter.
    public sealed class SkinSlot : IEquatable<SkinSlot>
    {
        public object Owner { get; }
        public string Member { get; }
        public string Target { get; }
        public Func<bool> IsAlive { get; }
        public Func<object> Read { get; }
        public Action<object> Write { get; }
        public Func<SkinTexture, object, object> Prepare { get; }
        // Known InvNailSprite/InvItemDisplay renderer; its vanilla authority is the source field.
        public SkinSlot InventoryConsumer { get; }
        public SkinSlot(object owner, string member, string target, Func<bool> alive, Func<object> read,
            Action<object> write, Func<SkinTexture, object, object> prepare, SkinSlot inventoryConsumer = null)
        { Owner = owner; Member = member; Target = target; IsAlive = alive; Read = read; Write = write; Prepare = prepare; InventoryConsumer = inventoryConsumer; }
        public bool Equals(SkinSlot other) => other != null && ReferenceEquals(Owner, other.Owner) && Member == other.Member;
        public override bool Equals(object other) => Equals(other as SkinSlot);
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(Owner) * 397 ^ Member.GetHashCode();
    }

    // Only the graphics boundary is fake in host tests; this is the actual atlas apply/undo binding.
    public interface ISkinAtlasSurface
    {
        object Identity { get; }
        bool IsAlive { get; }
        int Width { get; }
        int Height { get; }
        SkinTexture Capture();
        SkinTexture Fit(SkinTexture source);
        bool CopyFrom(SkinTexture source);
    }
    internal sealed class SkinAtlasImage { public SkinTexture Pixels; }
    public sealed class SkinAtlasSlot
    {
        readonly ISkinAtlasSurface surface;
        readonly string target;
        readonly Func<long, Func<SkinTexture>, SkinTexture> allocate;
        readonly SkinAtlasImage original = new SkinAtlasImage();
        SkinAtlasImage displayed;
        SkinTexture preparedSource;
        SkinAtlasImage prepared;
        public SkinTexture OriginalPixels => original.Pixels;
        public SkinAtlasSlot(ISkinAtlasSurface surface, string target, Func<long, Func<SkinTexture>, SkinTexture> allocate)
        { this.surface = surface; this.target = target; this.allocate = allocate; displayed = original; }
        public SkinSlot Binding() => new SkinSlot(surface.Identity, "atlasPixels", target, () => surface.IsAlive,
            () => displayed, value =>
            {
                var image = (SkinAtlasImage)value;
                if (image.Pixels == null || image.Pixels.ReleaseRequested) throw new IOException("Atlas restore pixels are unavailable.");
                // Distinct identity means unknown live pixels, even when the attempted image is vanilla.
                // Keep replayable pixels for undo, but do not let equality suppress a real restore retry.
                displayed = new SkinAtlasImage { Pixels = image.Pixels };
                if (!surface.CopyFrom(image.Pixels)) throw new IOException("Graphics.ConvertTexture returned false for " + target);
                displayed = image; // only a successful copy establishes the requested image
            }, (skin, baseline) => Prepare(skin));
        object Prepare(SkinTexture skin)
        {
            if (surface.Width < 1 || surface.Height < 1 || surface.Width > 4096 || surface.Height > 4096)
                throw new InvalidDataException("Live atlas dimensions exceed bound.");
            long peak = checked((long)surface.Width * surface.Height * 12); // readable RGBA32 CPU+GPU and temporary render target
            if (original.Pixels == null || original.Pixels.ReleaseRequested)
            {
                original.Pixels = allocate(peak, surface.Capture);
                Check(original.Pixels);
            }
            if (ReferenceEquals(preparedSource, skin) && prepared != null && !prepared.Pixels.ReleaseRequested) return prepared;
            var pixels = skin.Width == surface.Width && skin.Height == surface.Height ? skin : allocate(peak, () => surface.Fit(skin));
            Check(pixels); preparedSource = skin; prepared = new SkinAtlasImage { Pixels = pixels }; return prepared;
        }
        void Check(SkinTexture image)
        {
            if (!image.DecodeSucceeded || image.Width != surface.Width || image.Height != surface.Height)
                throw new IOException("Atlas capture/fit failed or returned unexpected dimensions.");
        }
    }

    public sealed class SkinRuntimeSession : IDisposable, ISkinTeardownSession
    {
        const long EncodedLimit = 16L * 1024 * 1024;
        readonly ISkinTextureDecoder decoder;
        readonly Func<IReadOnlyList<SkinSlot>> discover;
        readonly SkinRuntimeRules rules;
        readonly long memoryLimit;
        Dictionary<string, SkinTexture> current = new Dictionary<string, SkinTexture>(StringComparer.OrdinalIgnoreCase);
        Dictionary<SkinSlot, object> originals = new Dictionary<SkinSlot, object>();
        readonly List<SkinTexture> retired = new List<SkinTexture>();
        readonly List<SkinTexture> held = new List<SkinTexture>();
        readonly List<SkinTexture> auxiliary = new List<SkinTexture>();
        readonly List<SkinTexture> preparing = new List<SkinTexture>();
        long encodedAdmission, scratchBytes;
        bool blocked, disposed;
        string retirementError = "", mode = "ON";
        public SkinPack CurrentPack { get; private set; }
        public int SkinStamp { get; private set; }
        public bool TeardownComplete => disposed;
        public string LastError { get; private set; } = "";
        public long AccountedBytes => checked(AllTextures().Sum(x => x.AccountedBytes) + scratchBytes);
        public SkinRuntimeSession(ISkinTextureDecoder decoder, Func<IReadOnlyList<SkinSlot>> discover,
            long memoryLimit, SkinRuntimeRules rules)
        {
            this.decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
            this.discover = discover ?? throw new ArgumentNullException(nameof(discover));
            this.rules = rules ?? throw new ArgumentNullException(nameof(rules));
            if (memoryLimit <= 0) throw new ArgumentOutOfRangeException(nameof(memoryLimit));
            this.memoryLimit = memoryLimit;
        }

        public void WithScratch(long bytes, Action work)
        {
            if (bytes < 0 || checked(AccountedBytes + encodedAdmission + bytes) > memoryLimit)
                throw new InvalidDataException("HUD CPU scratch memory admission exceeded.");
            scratchBytes += bytes;
            try { work(); }
            finally { scratchBytes -= bytes; }
        }
        public SkinTexture AllocateAuxiliary(long peakBytes, Func<SkinTexture> create)
        {
            if (peakBytes <= 0 || checked(AccountedBytes + encodedAdmission + peakBytes) > memoryLimit)
                throw new InvalidDataException("Atlas/HUD backup and transient memory admission exceeded.");
            var texture = create();
            if (texture == null) throw new IOException("Graphics preparation returned no owned texture.");
            auxiliary.Add(texture); // own before checking a failed graphics operation
            if (texture.AccountedBytes > peakBytes) throw new IOException("Graphics preparation exceeded admitted texture size.");
            return texture;
        }

        public SkinApplyResult TryApply(SkinPack pack, CancellationToken cancellation = default)
        {
            if (disposed || blocked) return new SkinApplyResult(SkinApplyStatus.Blocked, "Runtime disposed or restoration required.");
            if (pack == null) return new SkinApplyResult(SkinApplyStatus.Rejected, "Normalized pack is required.");
            if (pack.Textures.Count > rules.MappingLimit)
                return new SkinApplyResult(SkinApplyStatus.Rejected, "Pack mapping count exceeds the launched profile bound.");
            if (cancellation.IsCancellationRequested) return new SkinApplyResult(SkinApplyStatus.Cancelled);
            Reap();
            if (retired.Count > 0)
                return WithRetirement(new SkinApplyResult(SkinApplyStatus.AwaitingTargets,
                    "Prior skin resources are still retiring; successor allocation is deferred."));
            if (ReferenceEquals(pack, CurrentPack) && pack.Mode == mode) return Refresh();
            var unsupported = pack.Textures.Keys.Where(x => !rules.IsSupported(x)).ToList();
            var candidate = new Dictionary<string, SkinTexture>(StringComparer.OrdinalIgnoreCase);
            try
            {
                cancellation.ThrowIfCancellationRequested();
                var files = new Dictionary<string, PngFile>(StringComparer.Ordinal);
                long candidatePeak = 0;
                foreach (var pair in pack.Textures.Where(x => rules.Allows(pack.Mode, x.Key)))
                {
                    cancellation.ThrowIfCancellationRequested();
                    string path = Path.Combine(pack.Root, pair.Value);
                    if (files.ContainsKey(path)) continue;
                    var file = Inspect(path);
                    candidatePeak = checked(candidatePeak + file.Length + (long)file.Width * file.Height * 8);
                    if (candidatePeak > memoryLimit) throw new InvalidDataException("Owned decoded/encoded memory admission exceeded.");
                    files.Add(path, file);
                }
                if (CurrentPack != null && checked(AccountedBytes + candidatePeak) > memoryLimit)
                {
                    var restoration = TryRestore();
                    if (restoration.Status != SkinApplyStatus.Restored && restoration.Status != SkinApplyStatus.Unchanged)
                        return restoration;
                    return WithRetirement(new SkinApplyResult(SkinApplyStatus.AwaitingTargets,
                        "Previous visuals restored; successor waits for confirmed resource retirement."));
                }
                mode = pack.Mode;
                encodedAdmission = files.Values.Sum(x => x.Length);
                var decoded = new Dictionary<string, SkinTexture>(StringComparer.Ordinal);
                foreach (var pair in pack.Textures.Where(x => rules.Allows(pack.Mode, x.Key)))
                {
                    cancellation.ThrowIfCancellationRequested();
                    string path = Path.Combine(pack.Root, pair.Value);
                    if (!decoded.TryGetValue(path, out var texture))
                    {
                        var info = files[path];
                        byte[] bytes = ReadExact(path, info);
                        texture = decoder.Decode(bytes, info.Width, info.Height);
                        if (texture == null) throw new IOException("Decoder returned no texture.");
                        candidate.Add(pair.Key, texture); preparing.Add(texture); // own before validation/cancellation can fail
                        decoded.Add(path, texture);
                        if (!texture.DecodeSucceeded) throw new IOException("PNG decoder rejected the payload.");
                        if (texture.Width != info.Width || texture.Height != info.Height)
                            throw new IOException("Decoded dimensions differ from validated PNG header.");
                    }
                    else candidate.Add(pair.Key, texture);
                    cancellation.ThrowIfCancellationRequested();
                }
                var result = Change(candidate, false, cancellation, unsupported, pack.Mode);
                if (result.Status == SkinApplyStatus.Applied || result.Status == SkinApplyStatus.Unchanged)
                {
                    var previous = current.Values.Distinct().ToList(); current = candidate; CurrentPack = pack;
                    Retire(previous); return WithRetirement(result);
                }
                if (blocked) held.AddRange(candidate.Values.Distinct()); else Retire(candidate.Values);
                return WithRetirement(result);
            }
            catch (Exception error)
            {
                Retire(candidate.Values);
                return WithRetirement(new SkinApplyResult(error is OperationCanceledException ? SkinApplyStatus.Cancelled :
                    error is InvalidDataException || error is OverflowException ? SkinApplyStatus.Rejected : SkinApplyStatus.Failed,
                    error.Message, unsupported));
            }
            finally { preparing.Clear(); encodedAdmission = 0; }
        }

        public SkinApplyResult Refresh()
        {
            Reap();
            if (disposed || blocked) return new SkinApplyResult(SkinApplyStatus.Blocked);
            if (CurrentPack == null) return WithRetirement(new SkinApplyResult(SkinApplyStatus.Unchanged));
            return WithRetirement(Change(current.Where(x => rules.Allows(mode, x.Key)).ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase),
                false, default, CurrentPack.Textures.Keys.Where(x => !rules.IsSupported(x)), mode));
        }

        public SkinApplyResult TryRestore()
        {
            Reap();
            var result = Change(new Dictionary<string, SkinTexture>(), true, default, null, mode);
            if (result.Status == SkinApplyStatus.Restored || result.Status == SkinApplyStatus.Unchanged)
            {
                var release = current.Values.Concat(held).Distinct().ToList();
                current.Clear(); held.Clear(); CurrentPack = null; blocked = false;
                Retire(release);
            }
            return WithRetirement(result);
        }

        SkinApplyResult Change(IReadOnlyDictionary<string, SkinTexture> desired, bool restoring,
            CancellationToken cancellation, IEnumerable<string> unsupported, string requestedMode)
        {
            var undo = new List<(SkinSlot Slot, object Value)>();
            var nextOriginals = originals.Where(x => x.Key.IsAlive()).ToDictionary(x => x.Key, x => x.Value);
            try
            {
                var slots = discover();
                unsupported = (unsupported ?? Array.Empty<string>()).Concat(slots.Where(x => x.Target.StartsWith("unsupported:", StringComparison.Ordinal)).Select(x => x.Target)).Distinct().ToList();
                if (slots.Count > 4096) throw new InvalidDataException("Live target bound exceeded.");
                var writes = new Dictionary<SkinSlot, object>();
                foreach (var slot in slots)
                {
                    if (!slot.IsAlive()) continue;
                    var value = slot.Read();
                    if (!nextOriginals.ContainsKey(slot) && AllTextures().Concat(desired.Values).Any(x => x.Owns(value)))
                    {
                        blocked = true;
                        throw new InvalidOperationException("New target inherited an owned skin value; original is unknown.");
                    }
                    if (!desired.TryGetValue(slot.Target, out var texture)) continue;
                    if (!nextOriginals.ContainsKey(slot)) nextOriginals.Add(slot, value);
                    var replacement = slot.Prepare(texture, nextOriginals[slot]);
                    if (writes.TryGetValue(slot, out var existing) && !Equals(existing, replacement))
                        throw new InvalidOperationException("Conflicting sheets target the same live slot.");
                    writes[slot] = replacement;
                }
                bool waiting = !restoring && writes.Count == 0;
                if (waiting && requestedMode != "ROTATE")
                    return new SkinApplyResult(SkinApplyStatus.AwaitingTargets, "No requested targets are currently available.", unsupported);
                // No character target yet: retain working character visuals, but restore excluded
                // environment now. A later refresh must not reapply the previous full-pack policy.
                var activeOriginals = nextOriginals.Where(x => writes.ContainsKey(x.Key) ||
                    (waiting && rules.Allows(requestedMode, x.Key.Target))).ToDictionary(x => x.Key, x => x.Value);
                foreach (var original in nextOriginals)
                    if (!writes.ContainsKey(original.Key) && !(waiting && rules.Allows(requestedMode, original.Key.Target)))
                        writes.Add(original.Key, original.Value);
                AddInventoryConsumerWrites(slots, writes, nextOriginals, activeOriginals);
                cancellation.ThrowIfCancellationRequested(); // commit is synchronous; no mid-frame yielding
                foreach (var write in writes)
                {
                    if (!write.Key.IsAlive()) continue;
                    var before = write.Key.Read();
                    if (Equals(before, write.Value)) continue;
                    undo.Add((write.Key, before)); // setter may throw after mutating
                    write.Key.Write(write.Value);
                }
                originals = restoring ? new Dictionary<SkinSlot, object>() : activeOriginals;
                if (undo.Count > 0) unchecked { SkinStamp++; }
                if (waiting) return new SkinApplyResult(SkinApplyStatus.AwaitingTargets, "No character targets available; excluded environment restored.", unsupported);
                return new SkinApplyResult(undo.Count == 0 ? SkinApplyStatus.Unchanged :
                    restoring ? SkinApplyStatus.Restored : SkinApplyStatus.Applied, unsupported: unsupported);
            }
            catch (Exception error)
            {
                var failures = new List<string>();
                for (int i = undo.Count - 1; i >= 0; i--)
                {
                    try { if (undo[i].Slot.IsAlive()) undo[i].Slot.Write(undo[i].Value); }
                    catch (Exception rollback) { failures.Add(rollback.Message); }
                }
                if (failures.Count > 0 || blocked)
                {
                    blocked = true; originals = nextOriginals;
                    return new SkinApplyResult(SkinApplyStatus.RestoreFailed, error.Message + "; " + string.Join("; ", failures), unsupported);
                }
                // OFF failure is explicit even when its operation-local rollback succeeded.
                if (restoring) blocked = true;
                return new SkinApplyResult(restoring ? SkinApplyStatus.RestoreFailed :
                    error is OperationCanceledException ? SkinApplyStatus.Cancelled : SkinApplyStatus.Failed, error.Message, unsupported);
            }
        }

        void AddInventoryConsumerWrites(IReadOnlyList<SkinSlot> slots, Dictionary<SkinSlot, object> writes,
            Dictionary<SkinSlot, object> nextOriginals, Dictionary<SkinSlot, object> activeOriginals)
        {
            // OnEnable copies a known source field into its renderer. Infer the selected field from
            // exact sprite identity, never PlayerData or a generated renderer sprite as vanilla.
            var groups = slots.Concat(writes.Keys).Where(x => x.InventoryConsumer != null && writes.ContainsKey(x))
                .GroupBy(x => x.InventoryConsumer).ToList();
            foreach (var group in groups)
            {
                var consumer = group.Key;
                if (!consumer.IsAlive()) continue;
                var displayed = consumer.Read();
                var matches = group.Distinct().Where(x => x.IsAlive() && displayed != null && ReferenceEquals(x.Read(), displayed)).ToList();
                if (matches.Count == 0)
                {
                    // A game-selected unskinned variant is not ours to overwrite. A still-owned value
                    // after failed undo must instead keep its saved restore write and resource charge.
                    if (nextOriginals.ContainsKey(consumer) && !AllTextures().Any(x => x.Owns(displayed)))
                    { nextOriginals.Remove(consumer); writes.Remove(consumer); }
                    continue;
                }
                var source = matches[0]; var vanilla = nextOriginals[source]; var replacement = writes[source];
                if (matches.Any(x => !ReferenceEquals(nextOriginals[x], vanilla) || !ReferenceEquals(writes[x], replacement)))
                    throw new InvalidOperationException("Ambiguous inventory sprite consumer source.");
                nextOriginals[consumer] = vanilla;
                writes[consumer] = replacement; // same synchronous undo list, before any retirement
                if (matches.Any(activeOriginals.ContainsKey)) activeOriginals[consumer] = vanilla;
            }
            if (writes.Count > 4096) throw new InvalidDataException("Live target and inventory consumer bound exceeded.");
        }

        IEnumerable<SkinTexture> AllTextures() => current.Values.Concat(held).Concat(retired).Concat(auxiliary).Concat(preparing).Distinct();
        void Retire(IEnumerable<SkinTexture> textures)
        {
            foreach (var texture in textures.Distinct())
                if (!retired.Contains(texture)) retired.Add(texture);
            Reap();
        }
        void Reap()
        {
            retirementError = "";
            foreach (var texture in retired.ToArray())
            {
                try { texture.Release(); if (texture.Released) retired.Remove(texture); }
                catch (Exception error) { retirementError = error.Message; }
            }
        }
        SkinApplyResult WithRetirement(SkinApplyResult result)
        {
            // Backups belong to saved originals; fitted/repaired textures belong to live slot values.
            // Failed rollback retains all preparations until a successful explicit restoration.
            if (!blocked && auxiliary.Count > 0)
            {
                try
                {
                    var references = originals.Where(x => x.Key.IsAlive()).SelectMany(x => new[] { x.Value, x.Key.Read() }).ToList();
                    var release = auxiliary.Where(texture => !references.Any(value => texture.Owns(value) ||
                        value is SkinAtlasImage image && ReferenceEquals(image.Pixels, texture))).ToList();
                    foreach (var texture in release) auxiliary.Remove(texture);
                    Retire(release);
                }
                catch (Exception error) { retirementError = "Cannot establish auxiliary resource reachability: " + error.Message; }
            }
            return retirementError.Length == 0 ? result : new SkinApplyResult(result.Status,
                result.Detail + "; Resource retirement pending: " + retirementError, result.UnsupportedTargets);
        }
        public void TickTeardown()
        {
            if (disposed) return;
            var result = TryRestore();
            if (result.Status == SkinApplyStatus.RestoreFailed || result.Status == SkinApplyStatus.Blocked ||
                retired.Count > 0 || auxiliary.Count > 0)
            {
                LastError = "Skin teardown retains resources; retry after restoration/destruction completes: " + result.Detail;
                return;
            }
            LastError = "";
            disposed = true;
        }

        public void Dispose()
        {
            TickTeardown();
            if (!disposed) throw new InvalidOperationException(LastError);
        }

        sealed class PngFile { public long Length; public int Width, Height; public byte[] Header; }
        static PngFile Inspect(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length < 45 || stream.Length > EncodedLimit) throw new InvalidDataException("PNG encoded size is outside bound.");
                var header = new byte[33]; ReadFully(stream, header);
                byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
                if (!header.Take(8).SequenceEqual(signature) || BigEndian(header, 8) != 13 ||
                    header[12] != 'I' || header[13] != 'H' || header[14] != 'D' || header[15] != 'R')
                    throw new InvalidDataException("PNG IHDR is invalid.");
                long width = BigEndian(header, 16), height = BigEndian(header, 20);
                if (width < 1 || height < 1 || width > 8192 || height > 8192 || width * height > 33554432)
                    throw new InvalidDataException("PNG dimensions exceed decoded bound.");
                return new PngFile { Length = stream.Length, Width = (int)width, Height = (int)height, Header = header };
            }
        }
        static long BigEndian(byte[] bytes, int at) => ((long)bytes[at] << 24) | ((long)bytes[at + 1] << 16) | ((long)bytes[at + 2] << 8) | bytes[at + 3];
        static byte[] ReadExact(string path, PngFile expected)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length != expected.Length) throw new InvalidDataException("PNG changed after admission.");
                var bytes = new byte[(int)expected.Length]; ReadFully(stream, bytes);
                if (stream.ReadByte() != -1 || !bytes.Take(33).SequenceEqual(expected.Header)) throw new InvalidDataException("PNG changed after admission.");
                return bytes;
            }
        }
        static void ReadFully(Stream stream, byte[] bytes)
        {
            int at = 0;
            while (at < bytes.Length) { int count = stream.Read(bytes, at, bytes.Length - at); if (count == 0) throw new InvalidDataException("Truncated PNG."); at += count; }
        }
    }
}
