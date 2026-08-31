using System.Buffers.Binary;
using System.Text;
using AssetsTools.NET;

internal static class SerializedFileDetection
{
    private const int LegacyHeaderSize = 20;
    private const int LargeHeaderSize = 48;
    private const int LargeFilesVersion = 22;
    private const int MaximumFormatVersion = 99;
    private const int MaximumUnityVersionLength = 255;

    internal static bool IsSerializedFile(string path)
    {
        if (AssetsFile.IsAssetsFile(path))
        {
            return true;
        }

        // AssetsTools.NET 3.0.0 rejects otherwise-valid Unity version stamps
        // containing '-'. Unity 6000 internal builds use stamps such as
        // 6000.0.50f1-uum-100966-branch1, so validate that narrow case against
        // the complete serialized-file header instead of treating it as a
        // bundle or arbitrary data.
        using var stream = File.OpenRead(path);
        if (stream.Length < LargeHeaderSize)
        {
            return false;
        }

        Span<byte> header = stackalloc byte[LargeHeaderSize];
        if (stream.Read(header) != header.Length ||
            header[..5].SequenceEqual("Unity"u8))
        {
            return false;
        }

        var format = BinaryPrimitives.ReadUInt32BigEndian(header[8..12]);
        if (format == 0 || format > MaximumFormatVersion || header[16] > 1)
        {
            return false;
        }

        int headerSize;
        ulong metadataSize;
        ulong declaredFileSize;
        ulong dataOffset;
        if (format >= LargeFilesVersion)
        {
            headerSize = LargeHeaderSize;
            metadataSize = BinaryPrimitives.ReadUInt32BigEndian(header[20..24]);
            declaredFileSize = BinaryPrimitives.ReadUInt64BigEndian(header[24..32]);
            dataOffset = BinaryPrimitives.ReadUInt64BigEndian(header[32..40]);
        }
        else
        {
            headerSize = LegacyHeaderSize;
            metadataSize = BinaryPrimitives.ReadUInt32BigEndian(header[0..4]);
            declaredFileSize = BinaryPrimitives.ReadUInt32BigEndian(header[4..8]);
            dataOffset = BinaryPrimitives.ReadUInt32BigEndian(header[12..16]);
        }

        if (metadataSize == 0 ||
            declaredFileSize != checked((ulong)stream.Length) ||
            dataOffset < checked((ulong)headerSize) ||
            dataOffset > declaredFileSize)
        {
            return false;
        }

        stream.Position = headerSize;
        var unityVersion = new StringBuilder();
        while (unityVersion.Length <= MaximumUnityVersionLength)
        {
            var next = stream.ReadByte();
            if (next < 0)
            {
                return false;
            }
            if (next == 0)
            {
                var version = unityVersion.ToString();
                return version.Contains('-', StringComparison.Ordinal) &&
                    version.All(IsUnityVersionCharacter);
            }
            unityVersion.Append(checked((char)next));
        }

        return false;
    }

    private static bool IsUnityVersionCharacter(char value) =>
        char.IsAsciiLetterOrDigit(value) || value is '.' or '-';
}
