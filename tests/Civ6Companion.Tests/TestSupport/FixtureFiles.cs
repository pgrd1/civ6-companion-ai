using System.IO.Compression;
using System.Text;

namespace Civ6Companion.Tests.TestSupport;

public static class FixtureFiles
{
    private static readonly string FixtureRoot = System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures");

    public static string Path(string name)
    {
        if (!string.Equals(name, "black-frame.png", StringComparison.Ordinal) &&
            !string.Equals(name, "civ-map-sample.png", StringComparison.Ordinal))
        {
            throw new ArgumentOutOfRangeException(nameof(name));
        }

        System.IO.Directory.CreateDirectory(FixtureRoot);
        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(FixtureRoot, name));
        var root = System.IO.Path.GetFullPath(FixtureRoot + System.IO.Path.DirectorySeparatorChar);
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Fixture path escaped the fixture directory.");
        }

        if (!System.IO.File.Exists(path))
        {
            WriteFixture(path, name == "black-frame.png");
        }

        return path;
    }

    public static string CreatePng(int width, int height, byte[] bgraPixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(bgraPixels);
        if (bgraPixels.Length != checked(width * height * 4))
        {
            throw new ArgumentException("Expected exactly one BGRA pixel per source pixel.", nameof(bgraPixels));
        }

        System.IO.Directory.CreateDirectory(FixtureRoot);
        var path = System.IO.Path.Combine(FixtureRoot, $"generated-{Guid.NewGuid():N}.png");
        WritePng(path, width, height, bgraPixels);
        return path;
    }

    private static void WriteFixture(string path, bool black)
    {
        const int width = 64;
        const int height = 36;
        var raw = new byte[height * ((width * 4) + 1)];
        for (var y = 0; y < height; y++)
        {
            var row = y * ((width * 4) + 1);
            for (var x = 0; x < width; x++)
            {
                var index = row + 1 + (x * 4);
                if (!black)
                {
                    raw[index] = (byte)((x * 17 + y * 3) % 256);
                    raw[index + 1] = (byte)((x * 5 + y * 19) % 256);
                    raw[index + 2] = (byte)((x * 11 + y * 7) % 256);
                    raw[index + 3] = 255;
                }
            }
        }

        WriteRawPng(path, width, height, raw);
    }

    private static void WritePng(string path, int width, int height, byte[] bgraPixels)
    {
        var raw = new byte[height * ((width * 4) + 1)];
        for (var y = 0; y < height; y++)
        {
            Buffer.BlockCopy(bgraPixels, y * width * 4, raw, (y * ((width * 4) + 1)) + 1, width * 4);
        }

        WriteRawPng(path, width, height, raw);
    }

    private static void WriteRawPng(string path, int width, int height, byte[] raw)
    {
        using var stream = new System.IO.FileStream(path, System.IO.FileMode.CreateNew, System.IO.FileAccess.Write, System.IO.FileShare.None);
        stream.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        WriteChunk(stream, "IHDR", CreateHeader(width, height));
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        WriteChunk(stream, "IDAT", compressed.ToArray());
        WriteChunk(stream, "IEND", Array.Empty<byte>());
    }

    private static byte[] CreateHeader(int width, int height)
    {
        var header = new byte[13];
        WriteInt32(header, 0, width);
        WriteInt32(header, 4, height);
        header[8] = 8;
        header[9] = 6;
        return header;
    }

    private static void WriteChunk(Stream stream, string type, byte[] payload)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        var length = new byte[4];
        WriteInt32(length, 0, payload.Length);
        stream.Write(length);
        stream.Write(typeBytes);
        stream.Write(payload);
        var crc = Crc32(typeBytes, payload);
        var checksum = new byte[4];
        WriteInt32(checksum, 0, unchecked((int)crc));
        stream.Write(checksum);
    }

    private static void WriteInt32(byte[] target, int offset, int value)
    {
        target[offset] = (byte)(value >> 24);
        target[offset + 1] = (byte)(value >> 16);
        target[offset + 2] = (byte)(value >> 8);
        target[offset + 3] = (byte)value;
    }

    private static uint Crc32(byte[] type, byte[] payload)
    {
        var crc = 0xffffffffu;
        foreach (var value in type.Concat(payload))
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb88320u);
            }
        }

        return ~crc;
    }
}
