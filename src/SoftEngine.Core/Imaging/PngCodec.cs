using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace SoftEngine.Core.Imaging;

public static class PngCodec
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly uint[] CrcTable = BuildCrcTable();

    public static void Save(string path, ReadOnlySpan<int> pixels, int width, int height)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, nameof(path));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width, nameof(width));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height, nameof(height));

        if (pixels.Length < width * height)
        {
            throw new ArgumentException($"Need {width * height} pixels; got {pixels.Length}.", nameof(pixels));
        }

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
        header[8] = 8;
        header[9] = 6;
        header[10] = 0;
        header[11] = 0;
        header[12] = 0;

        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", Deflate(raw));
        WriteChunk(png, "IEND", []);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllBytes(path, png.ToArray());
    }

    public const int MaxPixels = 256 * 1024 * 1024;

    public const int MaxDimension = 65_536;

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

            offset += 12 + (int)length;

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

                throw new InvalidDataException($"PNG filter type {filter}; the format defines 0 to 4.");
        }
    }

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
