using AssetRipper.Primitives;
using AssetsTools.NET;

namespace BundleSurgery;

// Read/write-capable port of Unity's ShaderSubProgram layout (the same
// structure USCSandbox decodes — we need the writer side too because
// we rewrite entries back into the blob, not just inspect them).
internal sealed class SubProgramEntry
{
    public int BlobVersion;
    public int ProgramType;
    public int StatsALU;
    public int StatsTEX;
    public int StatsFlow;
    public int StatsTempRegister;
    public List<string> GlobalKeywords = new();
    public List<string> LocalKeywords = new();
    public byte[] ProgramData = Array.Empty<byte>();
    public byte[] TrailerBytes = Array.Empty<byte>();   // BindChannels + ShaderParams sections we preserve as-is

    // Reads a single ShaderSubProgram entry from the start of `bytes`.
    // Layout mirrors Unity's serializer (cross-checked against USCSandbox's
    // reader on the same shipped shader bytes).
    public static SubProgramEntry Read(byte[] bytes, UnityVersion engVer)
    {
        var hasStatsTempRegister = engVer.IsGreaterEqual(5, 5);
        var hasLocalKeywords = engVer.IsLess(2021, 2) && engVer.IsGreaterEqual(2019, 1);

        using var ms = new MemoryStream(bytes);
        using var r = new AssetsFileReader(ms);

        var entry = new SubProgramEntry
        {
            BlobVersion = r.ReadInt32(),
            ProgramType = r.ReadInt32(),
            StatsALU = r.ReadInt32(),
            StatsTEX = r.ReadInt32(),
            StatsFlow = r.ReadInt32(),
        };
        if (hasStatsTempRegister) entry.StatsTempRegister = r.ReadInt32();

        var gkCount = r.ReadInt32();
        for (int i = 0; i < gkCount; i++)
        {
            entry.GlobalKeywords.Add(r.ReadCountStringInt32());
            r.Align();
        }
        if (hasLocalKeywords)
        {
            var lkCount = r.ReadInt32();
            for (int i = 0; i < lkCount; i++)
            {
                entry.LocalKeywords.Add(r.ReadCountStringInt32());
                r.Align();
            }
        }

        var progDataSize = r.ReadInt32();
        entry.ProgramData = r.ReadBytes(progDataSize);
        r.Align();

        // Capture whatever's left as the trailer — we preserve it verbatim
        // rather than parsing BindChannels/ShaderParams, since the
        // simplest correct behavior is to leave that data unchanged.
        var trailerStart = r.Position;
        var trailerLen = bytes.Length - (int)trailerStart;
        if (trailerLen > 0)
        {
            entry.TrailerBytes = r.ReadBytes(trailerLen);
        }
        return entry;
    }

    // Serialize this entry to bytes in the exact layout that
    // ShaderSubProgram's reader expects.
    public byte[] Write(UnityVersion engVer)
    {
        var hasStatsTempRegister = engVer.IsGreaterEqual(5, 5);
        var hasLocalKeywords = engVer.IsLess(2021, 2) && engVer.IsGreaterEqual(2019, 1);

        using var ms = new MemoryStream();
        using var w = new AssetsFileWriter(ms);

        w.Write(BlobVersion);
        w.Write(ProgramType);
        w.Write(StatsALU);
        w.Write(StatsTEX);
        w.Write(StatsFlow);
        if (hasStatsTempRegister) w.Write(StatsTempRegister);

        w.Write(GlobalKeywords.Count);
        foreach (var kw in GlobalKeywords)
        {
            w.WriteCountStringInt32(kw);
            w.Align();
        }
        if (hasLocalKeywords)
        {
            w.Write(LocalKeywords.Count);
            foreach (var kw in LocalKeywords)
            {
                w.WriteCountStringInt32(kw);
                w.Align();
            }
        }

        w.Write(ProgramData.Length);
        w.Write(ProgramData);
        w.Align();

        if (TrailerBytes.Length > 0) w.Write(TrailerBytes);

        w.Flush();
        return ms.ToArray();
    }
}

// Unity's decompressed shader-blob index layout. The blob is:
//   uint32  entryCount
//   Entry[entryCount]:
//     uint32 offset   (relative to the BLOB START, not the entry table end)
//     uint32 length
//     uint32 segment  (Unity 2019.3+)
//   Then: concatenated entry data starts at entries-table-end, aligned.
//
// Each entry's bytes are either a ShaderSubProgram or a ShaderParams. We
// don't need to know which — we just preserve the raw bytes per index.
internal sealed class BlobLayout
{
    public int[] Segments = Array.Empty<int>();        // one per entry
    public byte[][] EntryBytes = Array.Empty<byte[]>();

    public static BlobLayout ReadDecompressed(byte[] blob, UnityVersion engVer)
    {
        var hasSegment = engVer.IsGreaterEqual(2019, 3);
        using var ms = new MemoryStream(blob);
        using var r = new BinaryReader(ms);

        var count = r.ReadInt32();
        var offsets = new int[count];
        var lengths = new int[count];
        var segments = new int[count];
        for (int i = 0; i < count; i++)
        {
            offsets[i] = r.ReadInt32();
            lengths[i] = r.ReadInt32();
            if (hasSegment) segments[i] = r.ReadInt32();
        }

        var bytes = new byte[count][];
        for (int i = 0; i < count; i++)
        {
            ms.Position = offsets[i];
            bytes[i] = r.ReadBytes(lengths[i]);
        }
        return new BlobLayout { Segments = segments, EntryBytes = bytes };
    }

    public byte[] WriteDecompressed(UnityVersion engVer)
    {
        var hasSegment = engVer.IsGreaterEqual(2019, 3);
        var count = EntryBytes.Length;

        // Header is: u32 count + (3*u32 per entry, or 2*u32 pre-2019.3).
        var indexSize = 4 + count * (hasSegment ? 12 : 8);

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        // First pass: figure out offsets. Entries are concatenated tightly
        // starting at indexSize.
        var offsets = new int[count];
        var cursor = indexSize;
        for (int i = 0; i < count; i++)
        {
            offsets[i] = cursor;
            cursor += EntryBytes[i].Length;
            // Align to 4 bytes between entries to match Unity's layout.
            while (cursor % 4 != 0) cursor++;
        }

        // Header.
        w.Write(count);
        for (int i = 0; i < count; i++)
        {
            w.Write(offsets[i]);
            w.Write(EntryBytes[i].Length);
            if (hasSegment) w.Write(Segments.Length > i ? Segments[i] : 0);
        }

        // Entries.
        for (int i = 0; i < count; i++)
        {
            w.Write(EntryBytes[i]);
            while (ms.Position % 4 != 0) w.Write((byte)0);
        }
        return ms.ToArray();
    }
}
