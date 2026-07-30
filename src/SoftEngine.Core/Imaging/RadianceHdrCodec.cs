using System.Text;

namespace SoftEngine.Core.Imaging;

/// <summary>
/// Reads Radiance <c>.hdr</c> (<c>.pic</c>) images: the format every free HDR panorama is
/// distributed in, and the one thing standing between this renderer's split-sum IBL and real
/// input data.
///
/// <para>
/// The encoding is shared-exponent RGBE — three 8-bit mantissas and one 8-bit power of two, four
/// bytes a pixel for about five usable decades of range. It is a poor float format in the
/// abstract (a saturated red next to a dim blue in the same pixel loses the blue) and an
/// excellent one for skies, where the three channels of a given pixel are usually within a factor
/// of a few of each other.
/// </para>
///
/// <para>
/// The same reasoning that put <see cref="PngCodec"/> in Core applies here: this is not the
/// engine deciding to support arbitrary image formats on an artist's behalf — <see cref="Geometry.Texture"/>
/// pixels still arrive from whoever hosts the renderer. It is the one format the *lighting* needs
/// in order to be fed anything but clamped bytes, and no host is going to hand over floats it has
/// no type for.
/// </para>
/// </summary>
public static class RadianceHdrCodec
{
    /// <summary>Scanlines this long or longer may use the adaptive run-length encoding.</summary>
    private const int MinimumRleWidth = 8;

    /// <summary>And no longer than this, since the length is written into two bytes.</summary>
    private const int MaximumRleWidth = 0x7FFF;

    /// <summary>Reads the image at <paramref name="path"/>.</summary>
    public static HdrImage Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, nameof(path));

        using var stream = File.OpenRead(path);
        return Load(stream);
    }

    /// <summary>
    /// Reads an image from a stream, so a caller with the bytes in hand — an embedded resource,
    /// a download — does not have to put them on disk first.
    /// </summary>
    public static HdrImage Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream, nameof(stream));

        var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

        ReadHeader(reader, out var exposure);
        var (width, height, flipVertically) = ReadResolution(reader);

        var pixels = new float[width * height * 3];
        var scanline = new byte[width * 4];

        for (var y = 0; y < height; y++)
        {
            ReadScanline(reader, scanline, width);

            // Radiance's own orientation (-Y first) is top row first, which is the order
            // HdrImage stores; +Y files count up from the bottom and have to be turned over.
            var row = (flipVertically ? height - 1 - y : y) * width * 3;

            for (var x = 0; x < width; x++)
            {
                var e = scanline[x * 4 + 3];

                if (e == 0)
                {
                    continue;
                }

                // The mantissas are in [0, 255] against an implied 256, so the scale carries
                // both the stored exponent (biased by 128) and that division.
                var scale = exposure * MathF.ScaleB(1f, e - (128 + 8));

                var i = row + x * 3;
                pixels[i] = scanline[x * 4] * scale;
                pixels[i + 1] = scanline[x * 4 + 1] * scale;
                pixels[i + 2] = scanline[x * 4 + 2] * scale;
            }
        }

        return new HdrImage(width, height, pixels);
    }

    /// <summary>
    /// Consumes the header up to its blank line, checking the format and accumulating exposure.
    ///
    /// <c>EXPOSURE</c> records what was applied to the samples when the file was written, so
    /// undoing it is what puts the pixels back into the units the scene was measured in. It may
    /// appear more than once, in which case the corrections multiply.
    /// </summary>
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
                    // 32-bit_rle_xyze is the other legal value: CIE XYZ rather than RGB. It is
                    // rare, and decoding it without the colour conversion would be silently wrong.
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

    /// <summary>
    /// Reads the resolution line. Radiance can write any of eight axis orderings; the standard
    /// one is <c>-Y height +X width</c> and the only other one worth carrying is its vertical
    /// mirror. The four transposed orderings would need the whole image rotated, and no tool in
    /// practice emits them.
    /// </summary>
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
            return (width, height, parts[0] == "+Y");
        }

        throw new InvalidDataException($"Unsupported Radiance resolution line \"{line}\"; expected \"-Y height +X width\".");
    }

    /// <summary>
    /// Reads one scanline as <paramref name="width"/> RGBE quadruples, in either encoding.
    ///
    /// The adaptive scheme separates the four components and run-length encodes each across the
    /// whole scanline, which compresses a sky far better than interleaved bytes do — a gradient
    /// varies smoothly in every channel, so each channel's own row is long runs of near-equal
    /// values. Its marker is a pixel reading (2, 2, high, low) where the last two bytes repeat
    /// the width, which cannot be a real pixel because component 2 with exponent 2 is black.
    /// </summary>
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
            // Not adaptive after all: those four bytes are the first pixel of a flat scanline.
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
                    // A run: one value repeated (count - 128) times.
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

    /// <summary>
    /// Reads a scanline of interleaved RGBE pixels, honouring the original run-length escape:
    /// a pixel whose RGB is (1, 1, 1) is not a colour but an instruction to repeat the previous
    /// pixel, its exponent byte holding the count. Consecutive escapes shift the count up a byte
    /// each time, so a long run costs three pixels rather than one per 255.
    /// </summary>
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

                // The loop's own increment is one too many after the inner one ran to its end.
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

    /// <summary>
    /// One header line, terminated by a newline. Read a byte at a time on purpose: the header is
    /// text of unknown length immediately followed by binary, so anything that buffers ahead
    /// would swallow the first pixels.
    /// </summary>
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
