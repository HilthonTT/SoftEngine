using SoftEngine.Core.Imaging;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace SoftEngine.Core.Tests.Textures;

public class PngCodecTests
{
    [Fact]
    public void RgbaRoundTripsThroughSaveAndLoad()
    {
        int[] pixels = [unchecked((int)0x80102030), unchecked((int)0xFF405060), 0x00708090, unchecked((int)0xFFA0B0C0)];

        var path = TemporaryPath();

        try
        {
            PngCodec.Save(path, pixels, 2, 2);

            var (loaded, width, height) = PngCodec.Load(path);

            Assert.Equal((2, 2), (width, height));
            Assert.Equal(pixels, loaded);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RgbLoadsOpaqueAcrossEveryFilter()
    {
        // Three bytes a pixel, so the Sub, Average and Paeth filters reach back three bytes, not four.
        const int width = 3;
        const int height = 5;

        var expected = new int[width * height];
        var raw = new byte[(width * 3 + 1) * height];

        for (var y = 0; y < height; y++)
        {
            var row = y * (width * 3 + 1);
            raw[row] = (byte)y;

            for (var x = 0; x < width; x++)
            {
                var (r, g, b) = ((byte)(40 * x + y), (byte)(90 + 7 * y), (byte)(200 - 30 * x));
                expected[y * width + x] = unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
                raw[row + 1 + x * 3] = r;
                raw[row + 2 + x * 3] = g;
                raw[row + 3 + x * 3] = b;
            }
        }

        Filter(raw, width * 3, height, 3);

        var path = TemporaryPath();

        try
        {
            File.WriteAllBytes(path, EncodeRgb(raw, width, height));

            var (loaded, loadedWidth, loadedHeight) = PngCodec.Load(path);

            Assert.Equal((width, height), (loadedWidth, loadedHeight));
            Assert.Equal(expected, loaded);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GreyscaleIsRefused()
    {
        var path = TemporaryPath();

        try
        {
            File.WriteAllBytes(path, Encode(new byte[2], 1, 1, colourType: 0));

            Assert.Throws<NotSupportedException>(() => PngCodec.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string TemporaryPath() =>
        Path.Combine(Path.GetTempPath(), $"softengine-png-{Guid.NewGuid():N}.png");

    private static void Filter(byte[] raw, int stride, int height, int bpp)
    {
        var unfiltered = (byte[])raw.Clone();

        for (var y = height - 1; y >= 0; y--)
        {
            var row = y * (stride + 1);
            var above = (y - 1) * (stride + 1);

            for (var i = 0; i < stride; i++)
            {
                int left = i >= bpp ? unfiltered[row + 1 + i - bpp] : 0;
                int up = y > 0 ? unfiltered[above + 1 + i] : 0;
                int upLeft = y > 0 && i >= bpp ? unfiltered[above + 1 + i - bpp] : 0;

                var predicted = raw[row] switch
                {
                    1 => left,
                    2 => up,
                    3 => (left + up) >> 1,
                    4 => Paeth(left, up, upLeft),
                    _ => 0,
                };

                raw[row + 1 + i] = (byte)(unfiltered[row + 1 + i] - predicted);
            }
        }
    }

    private static int Paeth(int left, int above, int upLeft)
    {
        var estimate = left + above - upLeft;
        var (dLeft, dAbove, dUpLeft) = (System.Math.Abs(estimate - left), System.Math.Abs(estimate - above), System.Math.Abs(estimate - upLeft));

        return dLeft <= dAbove && dLeft <= dUpLeft ? left : dAbove <= dUpLeft ? above : upLeft;
    }

    private static byte[] EncodeRgb(byte[] raw, int width, int height) =>
        Encode(raw, width, height, colourType: 2);

    private static byte[] Encode(byte[] raw, int width, int height, byte colourType)
    {
        var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)height);
        header[8] = 8;
        header[9] = colourType;

        var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);

        return png.ToArray();
    }

    // The decoder does not check CRCs, so the chunks carry a zero one.
    private static void WriteChunk(Stream png, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);

        png.Write(length);
        png.Write(Encoding.ASCII.GetBytes(type));
        png.Write(data);
        png.Write(new byte[4]);
    }
}
