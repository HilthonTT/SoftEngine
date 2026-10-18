using System.Text;

namespace SoftEngine.Core.Imaging;

public static class RadianceHdrCodec
{
    private const int MinimumRleWidth = 8;

    private const int MaximumRleWidth = 0x7FFF;

    public const int MaxPixels = 256 * 1024 * 1024;

    public const int MaxDimension = 65_536;

    public static HdrImage Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, nameof(path));

        using var stream = File.OpenRead(path);
        return Load(stream);
    }

    public static HdrImage Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream, nameof(stream));

        try
        {
            return Decode(stream);
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("The Radiance image ends before its pixels do.", exception);
        }
    }

    private static HdrImage Decode(Stream stream)
    {
        var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

        ReadHeader(reader, out var exposure);
        var (width, height, flipVertically) = ReadResolution(reader);

        var pixels = new float[width * height * 3];
        var scanline = new byte[width * 4];

        for (var y = 0; y < height; y++)
        {
            ReadScanline(reader, scanline, width);

            var row = (flipVertically ? height - 1 - y : y) * width * 3;

            for (var x = 0; x < width; x++)
            {
                var e = scanline[x * 4 + 3];

                if (e == 0)
                {
                    continue;
                }

                var scale = exposure * MathF.ScaleB(1f, e - (128 + 8));

                var i = row + x * 3;
                pixels[i] = scanline[x * 4] * scale;
                pixels[i + 1] = scanline[x * 4 + 1] * scale;
                pixels[i + 2] = scanline[x * 4 + 2] * scale;
            }
        }

        return new HdrImage(width, height, pixels);
    }

    private static void ReadHeader(BinaryReader reader, out float exposure)
    {
        var signature = ReadLine(reader);

        if (!signature.StartsWith("#?", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Not a Radiance image: expected a \"#?\" signature, got \"{signature}\".");
        }

        var totalExposure = 1f;

        while (true)
        {
            var line = ReadLine(reader);

            if (line.Length == 0)
            {
                break;
            }

            if (line.StartsWith("FORMAT=", StringComparison.Ordinal))
            {
                var format = line["FORMAT=".Length..].Trim();

                if (!format.Equals("32-bit_rle_rgbe", StringComparison.Ordinal) &&
                    !format.Equals("32-bit_rgbe", StringComparison.Ordinal))
                {
                    throw new InvalidDataException($"Unsupported Radiance format \"{format}\"; expected 32-bit_rle_rgbe.");
                }
            }
            else if (line.StartsWith("EXPOSURE=", StringComparison.Ordinal) &&
                     float.TryParse(line["EXPOSURE=".Length..].Trim(),
                         System.Globalization.NumberStyles.Float,
                         System.Globalization.CultureInfo.InvariantCulture,
                         out var value) &&
                     value > 0f)
            {
                totalExposure *= value;
            }
        }

        exposure = 1f / totalExposure;
    }

    private static (int Width, int Height, bool FlipVertically) ReadResolution(BinaryReader reader)
    {
        var line = ReadLine(reader);
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length == 4 &&
            (parts[0] == "-Y" || parts[0] == "+Y") && parts[2] == "+X" &&
            int.TryParse(parts[1], out var height) &&
            int.TryParse(parts[3], out var width) &&
            width > 0 && height > 0)
        {
            if (width > MaxDimension || height > MaxDimension || (long)width * height > MaxPixels)
            {
                throw new InvalidDataException(
                    $"The Radiance resolution line \"{line}\" declares {width}x{height}, past the " +
                    $"{MaxDimension:N0} per side and {MaxPixels:N0} total this reader allocates for.");
            }

            return (width, height, parts[0] == "+Y");
        }

        throw new InvalidDataException($"Unsupported Radiance resolution line \"{line}\"; expected \"-Y height +X width\".");
    }

    private static void ReadScanline(BinaryReader reader, byte[] scanline, int width)
    {
        if (width < MinimumRleWidth || width > MaximumRleWidth)
        {
            ReadFlatScanline(reader, scanline, width, 0);
            return;
        }

        var header = ReadExactly(reader, 4);

        if (header[0] != 2 || header[1] != 2 || (header[2] & 0x80) != 0)
        {
            scanline[0] = header[0];
            scanline[1] = header[1];
            scanline[2] = header[2];
            scanline[3] = header[3];

            ReadFlatScanline(reader, scanline, width, 1);
            return;
        }

        var declared = (header[2] << 8) | header[3];

        if (declared != width)
        {
            throw new InvalidDataException($"Scanline declares width {declared}, image says {width}.");
        }

        for (var component = 0; component < 4; component++)
        {
            var x = 0;

            while (x < width)
            {
                var count = reader.ReadByte();

                if (count > 128)
                {
                    var value = reader.ReadByte();
                    var run = count - 128;

                    if (x + run > width)
                    {
                        throw new InvalidDataException("Run-length run overruns the scanline.");
                    }

                    for (var i = 0; i < run; i++)
                    {
                        scanline[(x++) * 4 + component] = value;
                    }
                }
                else
                {
                    if (count == 0 || x + count > width)
                    {
                        throw new InvalidDataException("Run-length literal overruns the scanline.");
                    }

                    for (var i = 0; i < count; i++)
                    {
                        scanline[(x++) * 4 + component] = reader.ReadByte();
                    }
                }
            }
        }
    }

    private static void ReadFlatScanline(BinaryReader reader, byte[] scanline, int width, int start)
    {
        var shift = 0;

        for (var x = start; x < width; x++)
        {
            var pixel = ReadExactly(reader, 4);

            if (pixel[0] == 1 && pixel[1] == 1 && pixel[2] == 1)
            {
                if (x == 0)
                {
                    throw new InvalidDataException("A scanline cannot begin with a repeat of the previous pixel.");
                }

                var run = pixel[3] << shift;
                shift += 8;

                var previous = (x - 1) * 4;

                for (var i = 0; i < run && x < width; i++, x++)
                {
                    Array.Copy(scanline, previous, scanline, x * 4, 4);
                }

                x--;
                continue;
            }

            shift = 0;

            scanline[x * 4] = pixel[0];
            scanline[x * 4 + 1] = pixel[1];
            scanline[x * 4 + 2] = pixel[2];
            scanline[x * 4 + 3] = pixel[3];
        }
    }

    private static string ReadLine(BinaryReader reader)
    {
        var line = new StringBuilder();

        while (true)
        {
            var b = reader.ReadByte();

            if (b == '\n')
            {
                return line.ToString();
            }

            if (b != '\r')
            {
                line.Append((char)b);
            }
        }
    }

    private static byte[] ReadExactly(BinaryReader reader, int count)
    {
        var bytes = reader.ReadBytes(count);

        if (bytes.Length != count)
        {
            throw new EndOfStreamException($"Expected {count} more bytes of image data, got {bytes.Length}.");
        }

        return bytes;
    }
}
