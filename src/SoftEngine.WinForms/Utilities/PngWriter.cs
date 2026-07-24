namespace SoftEngine.WinForms.Utilities;

/// <summary>
/// A minimal, dependency-free PNG encoder for the M8 screenshot hotkey. StbImageSharp (the Assets
/// texture <em>decoder</em>) has no write path and we add no image-writing dependency, so this
/// hand-rolls the format: an RGBA8, non-interlaced PNG whose single <c>IDAT</c> stream uses DEFLATE
/// <b>stored</b> (uncompressed) blocks. Stored blocks keep the zlib layer trivial — no Huffman/LZ77 —
/// which is all a screenshot needs; the file is larger than a compressed PNG but perfectly valid.
/// </summary>
internal static class PngWriter
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>
    /// Encodes <paramref name="pixels"/> (packed RGBA, row-major, top-left origin) as a PNG and writes
    /// it to <paramref name="path"/>.
    /// </summary>
    /// <param name="path">Destination file path.</param>
    /// <param name="pixels">Packed-RGBA pixels; length must be at least <paramref name="width"/>·<paramref name="height"/>.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    public static void Save(string path, ReadOnlySpan<uint> pixels, int width, int height)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, nameof(path));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width, nameof(width));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height, nameof(height));
        if (pixels.Length < width * height)
        {
            throw new ArgumentException($"Need {width * height} pixels; got {pixels.Length}.", nameof(pixels));
        }

        // Filtered scanlines: PNG prefixes every row with a filter-type byte (0 = None), followed by
        // the row's RGBA bytes. Row 0 is the top, which matches our framebuffer's row order.
        int stride = (width * 4) + 1;
        byte[] raw = new byte[stride * height];

        for (int y = 0; y < height; y++)
        {
            int row = y * stride;
            raw[row] = 0; // filter: None
            int src = y * width;
            int dst = row + 1;

            for (int x = 0; x < width; x++)
            {
                uint c = pixels[src + x];
                raw[dst++] = (byte)(c & 0xFF);         // R
                raw[dst++] = (byte)((c >> 8) & 0xFF);  // G
                raw[dst++] = (byte)((c >> 16) & 0xFF); // B
                raw[dst++] = (byte)((c >> 24) & 0xFF); // A
            }
        }

        List<byte> png = [.. Signature];
        WriteChunk(png, "IHDR", BuildHeader(width, height));
        WriteChunk(png, "IDAT", ZlibStore(raw));
        WriteChunk(png, "IEND", []);

        File.WriteAllBytes(path, [.. png]);
    }

    private static byte[] BuildHeader(int width, int height)
    {
        byte[] header = new byte[13];
        WriteBigEndian(header, 0, (uint)width);
        WriteBigEndian(header, 4, (uint)height);
        header[8] = 8;  // bit depth per channel
        header[9] = 6;  // color type 6 = truecolor with alpha (RGBA)
        header[10] = 0; // compression method: DEFLATE
        header[11] = 0; // filter method: adaptive (per-row filter bytes)
        header[12] = 0; // interlace: none
        return header;
    }

    /// <summary>
    /// Wraps <paramref name="data"/> in a zlib stream using only DEFLATE stored (uncompressed) blocks.
    /// Each block carries up to 65,535 bytes; the final block sets BFINAL. The stream ends with an
    /// Adler-32 checksum of the uncompressed data (big-endian), as zlib requires.
    /// </summary>
    private static byte[] ZlibStore(byte[] data)
    {
        List<byte> z =
        [
            0x78, // CMF: CM = 8 (DEFLATE), CINFO = 7 (32 KiB window)
            0x01, // FLG: no preset dict, level 0; chosen so (CMF*256+FLG) % 31 == 0
        ];

        int offset = 0;
        do
        {
            int chunk = int.Min(0xFFFF, data.Length - offset);
            bool final = offset + chunk >= data.Length;
            z.Add((byte)(final ? 1 : 0)); // BFINAL in bit 0, BTYPE = 00 (stored) in bits 1-2
            z.Add((byte)(chunk & 0xFF));
            z.Add((byte)((chunk >> 8) & 0xFF));
            int nlen = ~chunk & 0xFFFF; // one's complement of LEN, as the format demands
            z.Add((byte)(nlen & 0xFF));
            z.Add((byte)((nlen >> 8) & 0xFF));
            for (int i = 0; i < chunk; i++)
            {
                z.Add(data[offset + i]);
            }

            offset += chunk;
        }
        while (offset < data.Length);

        uint adler = Adler32(data);
        z.Add((byte)((adler >> 24) & 0xFF));
        z.Add((byte)((adler >> 16) & 0xFF));
        z.Add((byte)((adler >> 8) & 0xFF));
        z.Add((byte)(adler & 0xFF));
        return [.. z];
    }

    // Appends one PNG chunk: length (BE) + 4-char type + data + CRC-32 (BE) over type+data.
    private static void WriteChunk(List<byte> png, string type, byte[] data)
    {
        byte[] length = new byte[4];
        WriteBigEndian(length, 0, (uint)data.Length);
        png.AddRange(length);

        int crcStart = png.Count;
        for (int i = 0; i < type.Length; i++)
        {
            png.Add((byte)type[i]);
        }

        png.AddRange(data);

        uint crc = Crc32(png, crcStart, png.Count - crcStart);
        byte[] crcBytes = new byte[4];
        WriteBigEndian(crcBytes, 0, crc);
        png.AddRange(crcBytes);
    }

    private static void WriteBigEndian(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)((value >> 24) & 0xFF);
        buffer[offset + 1] = (byte)((value >> 16) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 3] = (byte)(value & 0xFF);
    }

    // Adler-32: two running 16-bit sums mod 65521 (the largest prime below 2^16).
    private static uint Adler32(byte[] data)
    {
        const uint Mod = 65521;
        uint a = 1;
        uint b = 0;

        foreach (byte value in data)
        {
            a = (a + value) % Mod;
            b = (b + a) % Mod;
        }

        return (b << 16) | a;
    }

    private static uint Crc32(List<byte> data, int start, int count)
    {
        uint crc = 0xFFFFFFFF;
        for (int i = 0; i < count; i++)
        {
            crc = CrcTable[(crc ^ data[start + i]) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFF;
    }

    private static uint[] BuildCrcTable()
    {
        uint[] table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;

            for (int j = 0; j < 8; j++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }

            table[i] = c;
        }

        return table;
    }
}
