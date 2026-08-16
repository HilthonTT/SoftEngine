using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace SoftEngine.Core.Imaging;

/// <summary>
/// Reads and writes 8-bit RGBA PNGs of the engine's own frames.
///
/// <para>
/// This is not a retreat from the line the importers hold. An OBJ or glTF reader still resolves
/// an image down to bytes and hands the <em>decoding</em> to whoever hosts it, because a texture
/// arrives in whatever format an artist saved it in and supporting that is an application's
/// problem. This codec answers a different question: how the engine writes out the frame it just
/// produced, which is its own <c>int[]</c> in its own layout, and how it reads one back to
/// compare against. Three consumers wanted exactly that — the golden-image harness, the viewer's
/// screenshot key and the headless renderer — and three copies of a PNG encoder is not a line
/// being held, it is a line being paid for repeatedly.
/// </para>
///
/// <para>
/// Pixels are exchanged in the packed ARGB <see cref="Buffers.FrameBuffer.Screen"/> holds —
/// alpha in the top byte, then red, green, blue — and the byte order on disk is PNG's RGBA. The
/// two disagree, which is exactly why the swizzle happens in one place.
/// </para>
/// </summary>
public static class PngCodec
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>Encodes packed-ARGB <paramref name="pixels"/> as a PNG at <paramref name="path"/>.</summary>
    public static void Save(string path, ReadOnlySpan<int> pixels, int width, int height)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, nameof(path));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width, nameof(width));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height, nameof(height));

        if (pixels.Length < width * height)
        {
            throw new ArgumentException($"Need {width * height} pixels; got {pixels.Length}.", nameof(pixels));
        }

        // PNG prefixes every scanline with a filter-type byte. Filter 0 (None) keeps decoding a
        // straight copy and lets DEFLATE do the compressing, which is the right trade for images
        // written once and read many times.
        var stride = width * 4 + 1;
        var raw = new byte[stride * height];

        for (var y = 0; y < height; y++)
        {
            var row = y * stride;
            raw[row] = 0;

            var source = y * width;
            var destination = row + 1;

            for (var x = 0; x < width; x++)
            {
                var argb = pixels[source + x];

                raw[destination++] = (byte)((argb >> 16) & 0xFF);
                raw[destination++] = (byte)((argb >> 8) & 0xFF);
                raw[destination++] = (byte)(argb & 0xFF);
                raw[destination++] = (byte)((argb >> 24) & 0xFF);
            }
        }

        var png = new MemoryStream();
        png.Write(Signature);

        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)height);
        header[8] = 8; // bits per channel
        header[9] = 6; // colour type 6: truecolour with alpha
        header[10] = 0; // compression: DEFLATE
        header[11] = 0; // filtering: adaptive, per scanline
        header[12] = 0; // interlacing: none

        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", Deflate(raw));
        WriteChunk(png, "IEND", []);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllBytes(path, png.ToArray());
    }

    /// <summary>
    /// The largest image this decoder will allocate for, in pixels.
    ///
    /// <para>
    /// A PNG's dimensions are two numbers in a thirteen-byte header, and nothing else in the
    /// file has to agree with them. Sixty bytes can therefore ask for a hundred gigabytes, and
    /// a decoder that believes the header allocates it before it ever discovers the pixels are
    /// not there. The limit is the difference between a malformed file failing and a malformed
    /// file taking the process with it.
    /// </para>
    ///
    /// <para>
    /// A quarter of a gigapixel: past any frame this renderer produces and past any texture
    /// worth loading into one, so nothing legitimate ever meets it.
    /// </para>
    /// </summary>
    public const int MaxPixels = 256 * 1024 * 1024;

    /// <summary>
    /// The largest width or height accepted. Bounding each dimension before they are multiplied
    /// is what keeps the product from overflowing on its way to being checked against
    /// <see cref="MaxPixels"/>.
    /// </summary>
    public const int MaxDimension = 65_536;

    /// <summary>Decodes a PNG written by <see cref="Save"/> back into packed ARGB.</summary>
    /// <remarks>
    /// <para>
    /// Only the subset <see cref="Save"/> produces is supported — 8-bit RGBA, non-interlaced —
    /// but all five scanline filters are handled, because a file that comes back is one a person
    /// may well have re-saved from an image editor along the way, and those filter properly.
    /// </para>
    /// <para>
    /// Every way the file can be malformed arrives as <see cref="InvalidDataException"/>, and
    /// every way it can be well-formed but unsupported as <see cref="NotSupportedException"/>.
    /// Nothing else escapes: the sizes in the header are bounded before anything is allocated
    /// for them, and every offset the chunk walk produces is checked against the bytes that are
    /// really there. Catching those two covers the file being wrong in any way at all.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidDataException">The file is not a well-formed PNG.</exception>
    /// <exception cref="NotSupportedException">It is a PNG this decoder does not read.</exception>
    public static (int[] Pixels, int Width, int Height) Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, nameof(path));

        var bytes = File.ReadAllBytes(path);

        if (bytes.Length < Signature.Length || !bytes.AsSpan(0, Signature.Length).SequenceEqual(Signature))
        {
            throw new InvalidDataException($"{path} is not a PNG.");
        }

        var width = 0;
        var height = 0;
        var seenHeader = false;
        var idat = new MemoryStream();

        var offset = Signature.Length;

        while (offset + 8 <= bytes.Length)
        {
            // Read unsigned and held as a long until it has been checked. A chunk declaring
            // 0xFFFFFFFF bytes is four billion as the format defines it and -1 as an int, and
            // a negative length slices backwards rather than failing.
            var length = (long)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset));
            var type = Encoding.ASCII.GetString(bytes, offset + 4, 4);

            if (offset + 8 + length > bytes.Length)
            {
                throw new InvalidDataException(
                    $"{path} has a {type} chunk claiming {length} bytes with {bytes.Length - offset - 8} left in the file.");
            }

            var data = bytes.AsSpan(offset + 8, (int)length);

            switch (type)
            {
                case "IHDR":
                    // Thirteen bytes exactly, and every field read below indexes into them.
                    if (data.Length < 13)
                    {
                        throw new InvalidDataException($"{path} has a {data.Length}-byte IHDR; the format defines 13.");
                    }

                    width = ReadDimension(path, data, "width");
                    height = ReadDimension(path, data[4..], "height");
                    seenHeader = true;

                    if ((long)width * height > MaxPixels)
                    {
                        throw new InvalidDataException(
                            $"{path} declares {width}x{height} pixels, past the {MaxPixels:N0} this decoder allocates for.");
                    }

                    if (data[8] != 8 || data[9] != 6)
                    {
                        throw new NotSupportedException($"{path} is not 8-bit RGBA (depth {data[8]}, colour type {data[9]}).");
                    }

                    if (data[12] != 0)
                    {
                        throw new NotSupportedException($"{path} is interlaced.");
                    }

                    break;

                case "IDAT":
                    idat.Write(data);
                    break;
            }

            offset += 12 + (int)length; // length + type + data + CRC

            if (type == "IEND")
            {
                break;
            }
        }

        if (!seenHeader)
        {
            throw new InvalidDataException($"{path} has no IHDR.");
        }

        if (width == 0 || height == 0)
        {
            throw new InvalidDataException($"{path} declares an empty image ({width}x{height}).");
        }

        idat.Position = 0;
        using var inflate = new ZLibStream(idat, CompressionMode.Decompress);

        var stride = width * 4;
        var raw = new byte[(stride + 1) * height];

        // The header said how many bytes the image is; the compressed data is under no
        // obligation to hold that many, or to be a DEFLATE stream at all. Both arrive from
        // under ReadExactly as something that names neither the file nor what was expected of
        // it: an EndOfStreamException when the data runs out, a ZLibException when it is
        // corrupt rather than short. Both derive from IOException, and nothing here does any
        // real I/O — the stream being inflated is a MemoryStream of bytes already read — so
        // catching that is precise rather than broad.
        try
        {
            inflate.ReadExactly(raw);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            throw new InvalidDataException(
                $"{path} does not hold the {raw.Length:N0} bytes its {width}x{height} header declares.", exception);
        }

        var pixels = new int[width * height];

        // Filters are defined against the reconstructed bytes of the row above, so the rows
        // have to be undone in order and in place.
        var current = new byte[stride];
        var previous = new byte[stride];

        for (var y = 0; y < height; y++)
        {
            var row = y * (stride + 1);
            var filter = raw[row];

            raw.AsSpan(row + 1, stride).CopyTo(current);
            Unfilter(filter, current, previous, 4);

            var destination = y * width;

            for (var x = 0; x < width; x++)
            {
                var i = x * 4;

                pixels[destination + x] =
                    (current[i + 3] << 24) |
                    (current[i] << 16) |
                    (current[i + 1] << 8) |
                    current[i + 2];
            }

            (previous, current) = (current, previous);
        }

        return (pixels, width, height);
    }

    /// <summary>
    /// One dimension out of an IHDR, bounded before anything is sized off it. PNG stores them
    /// as unsigned 32-bit, so the range they can name is twice what an int holds and the whole
    /// top half of it reads back negative.
    /// </summary>
    private static int ReadDimension(string path, ReadOnlySpan<byte> data, string name)
    {
        var value = BinaryPrimitives.ReadUInt32BigEndian(data);

        if (value > MaxDimension)
        {
            throw new InvalidDataException(
                $"{path} declares a {name} of {value}, past the {MaxDimension:N0} this decoder accepts.");
        }

        return (int)value;
    }

    /// <summary>Reverses one scanline's filter in place. <paramref name="bpp"/> is the byte distance to the pixel on the left.</summary>
    private static void Unfilter(int filter, Span<byte> current, ReadOnlySpan<byte> previous, int bpp)
    {
        switch (filter)
        {
            case 0:
                break;

            case 1:
                for (var i = bpp; i < current.Length; i++)
                {
                    current[i] = (byte)(current[i] + current[i - bpp]);
                }

                break;

            case 2:
                for (var i = 0; i < current.Length; i++)
                {
                    current[i] = (byte)(current[i] + previous[i]);
                }

                break;

            case 3:
                for (var i = 0; i < current.Length; i++)
                {
                    var left = i >= bpp ? current[i - bpp] : 0;
                    current[i] = (byte)(current[i] + ((left + previous[i]) >> 1));
                }

                break;

            case 4:
                for (var i = 0; i < current.Length; i++)
                {
                    var left = i >= bpp ? current[i - bpp] : 0;
                    var upLeft = i >= bpp ? previous[i - bpp] : 0;
                    current[i] = (byte)(current[i] + Paeth(left, previous[i], upLeft));
                }

                break;

            default:
                // Not NotSupportedException: the format defines five filter types and no
                // others, so a sixth is a corrupt file and not a feature to go and add.
                throw new InvalidDataException($"PNG filter type {filter}; the format defines 0 to 4.");
        }
    }

    /// <summary>The PNG predictor: of left, above and above-left, whichever is nearest their linear estimate.</summary>
    private static int Paeth(int left, int above, int upLeft)
    {
        var estimate = left + above - upLeft;

        var dLeft = System.Math.Abs(estimate - left);
        var dAbove = System.Math.Abs(estimate - above);
        var dUpLeft = System.Math.Abs(estimate - upLeft);

        if (dLeft <= dAbove && dLeft <= dUpLeft)
        {
            return left;
        }

        return dAbove <= dUpLeft ? above : upLeft;
    }

    private static byte[] Deflate(byte[] raw)
    {
        var compressed = new MemoryStream();

        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        return compressed.ToArray();
    }

    private static void WriteChunk(Stream png, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        png.Write(length);

        Span<byte> name = stackalloc byte[4];
        Encoding.ASCII.GetBytes(type, name);
        png.Write(name);
        png.Write(data);

        var crc = 0xFFFFFFFFu;
        crc = Crc32(crc, name);
        crc = Crc32(crc, data);

        Span<byte> checksum = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(checksum, crc ^ 0xFFFFFFFFu);
        png.Write(checksum);
    }

    private static uint Crc32(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];

        for (uint i = 0; i < 256; i++)
        {
            var c = i;

            for (var bit = 0; bit < 8; bit++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[i] = c;
        }

        return table;
    }
}
