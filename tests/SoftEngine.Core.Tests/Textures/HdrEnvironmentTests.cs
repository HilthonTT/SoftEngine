using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Imaging;
using SoftEngine.Core.Shading;
using SoftEngine.Core.Textures;
using System.Numerics;
using System.Text;

namespace SoftEngine.Core.Tests.Textures;

public class HdrEnvironmentTests
{
    private static byte[] Encode(
        int width,
        int height,
        LinearColor[] pixels,
        bool runLength = true,
        float exposure = 1f,
        bool bottomUp = false)
    {
        var header = new StringBuilder();
        header.Append("#?RADIANCE\n");
        header.Append("SOFTWARE=SoftEngine tests\n");
        header.Append("FORMAT=32-bit_rle_rgbe\n");

        if (exposure != 1f)
        {
            header.Append($"EXPOSURE={exposure.ToString(System.Globalization.CultureInfo.InvariantCulture)}\n");
        }

        header.Append('\n');
        header.Append(bottomUp ? $"+Y {height} +X {width}\n" : $"-Y {height} +X {width}\n");

        var bytes = new List<byte>(Encoding.ASCII.GetBytes(header.ToString()));

        for (var y = 0; y < height; y++)
        {
            var sourceRow = bottomUp ? height - 1 - y : y;

            var scanline = new byte[width * 4];

            for (var x = 0; x < width; x++)
            {
                var (r, g, b, e) = ToRgbe(pixels[x + sourceRow * width], exposure);

                scanline[x * 4] = r;
                scanline[x * 4 + 1] = g;
                scanline[x * 4 + 2] = b;
                scanline[x * 4 + 3] = e;
            }

            if (runLength && width >= 8)
            {
                bytes.Add(2);
                bytes.Add(2);
                bytes.Add((byte)(width >> 8));
                bytes.Add((byte)(width & 0xFF));

                for (var component = 0; component < 4; component++)
                {
                    var row = new byte[width];

                    for (var x = 0; x < width; x++)
                    {
                        row[x] = scanline[x * 4 + component];
                    }

                    bytes.AddRange(RunLengthEncode(row));
                }
            }
            else
            {
                bytes.AddRange(scanline);
            }
        }

        return [.. bytes];
    }

    private static List<byte> RunLengthEncode(byte[] row)
    {
        var output = new List<byte>();
        var x = 0;

        while (x < row.Length)
        {
            var run = 1;
            while (x + run < row.Length && run < 127 && row[x + run] == row[x])
            {
                run++;
            }

            if (run >= 4)
            {
                output.Add((byte)(128 + run));
                output.Add(row[x]);
                x += run;
                continue;
            }

            var literal = 0;
            while (x + literal < row.Length && literal < 128)
            {
                var ahead = 1;
                while (x + literal + ahead < row.Length && ahead < 5 && row[x + literal + ahead] == row[x + literal])
                {
                    ahead++;
                }

                if (ahead >= 4 && literal > 0)
                {
                    break;
                }

                literal++;
            }

            output.Add((byte)literal);
            for (var i = 0; i < literal; i++)
            {
                output.Add(row[x + i]);
            }

            x += literal;
        }

        return output;
    }

    private static (byte R, byte G, byte B, byte E) ToRgbe(LinearColor color, float exposure)
    {
        var r = color.R * exposure;
        var g = color.G * exposure;
        var b = color.B * exposure;

        var max = MathF.Max(r, MathF.Max(g, b));

        if (max < 1e-32f)
        {
            return (0, 0, 0, 0);
        }

        var e = (int)MathF.Floor(MathF.Log2(max)) + 1;
        var scale = MathF.ScaleB(256f, -e);

        return (
            (byte)System.Math.Clamp((int)MathF.Round(r * scale), 0, 255),
            (byte)System.Math.Clamp((int)MathF.Round(g * scale), 0, 255),
            (byte)System.Math.Clamp((int)MathF.Round(b * scale), 0, 255),
            (byte)(e + 128));
    }

    private static HdrImage Decode(byte[] bytes) => RadianceHdrCodec.Load(new MemoryStream(bytes));

    private static LinearColor[] Ramp(int width, int height)
    {
        var pixels = new LinearColor[width * height];

        for (var i = 0; i < pixels.Length; i++)
        {
            var value = MathF.ScaleB(1f, i % 12);
            pixels[i] = new LinearColor(value, value * 0.5f, value * 0.25f);
        }

        return pixels;
    }

    [Fact]
    public void Codec_ReadsRunLengthEncodedScanlines()
    {
        var pixels = Ramp(8, 4);
        var image = Decode(Encode(8, 4, pixels));

        Assert.Equal(8, image.Width);
        Assert.Equal(4, image.Height);

        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                var expected = pixels[x + y * 8];
                var actual = image[x, y];

                Assert.Equal(expected.R, actual.R, 5);
                Assert.Equal(expected.G, actual.G, 5);
                Assert.Equal(expected.B, actual.B, 5);
            }
        }
    }

    [Fact]
    public void Codec_ReadsRunsOfOneColour()
    {
        var pixels = Enumerable.Repeat(new LinearColor(4f, 2f, 1f), 16 * 2).ToArray();
        var image = Decode(Encode(16, 2, pixels));

        foreach (var y in new[] { 0, 1 })
        {
            foreach (var x in new[] { 0, 7, 15 })
            {
                Assert.Equal(4f, image[x, y].R, 5);
                Assert.Equal(2f, image[x, y].G, 5);
                Assert.Equal(1f, image[x, y].B, 5);
            }
        }
    }

    [Fact]
    public void Codec_ReadsFlatScanlines()
    {
        var pixels = Ramp(8, 3);
        var image = Decode(Encode(8, 3, pixels, runLength: false));

        Assert.Equal(pixels[0].R, image[0, 0].R, 5);
        Assert.Equal(pixels[8 + 3].R, image[3, 1].R, 5);
        Assert.Equal(pixels[16 + 7].B, image[7, 2].B, 5);
    }

    [Fact]
    public void Codec_ReadsScanlinesTooShortForRunLength()
    {
        var pixels = Ramp(4, 2);
        var image = Decode(Encode(4, 2, pixels));

        Assert.Equal(4, image.Width);
        Assert.Equal(pixels[5].G, image[1, 1].G, 5);
    }

    [Fact]
    public void Codec_DividesOutTheRecordedExposure()
    {
        var pixels = Enumerable.Repeat(new LinearColor(1f, 1f, 1f), 8).ToArray();
        var image = Decode(Encode(8, 1, pixels, exposure: 4f));

        Assert.Equal(1f, image[0, 0].R, 4);
    }

    [Fact]
    public void Codec_TurnsBottomUpImagesOver()
    {
        var pixels = new LinearColor[8 * 2];
        Array.Fill(pixels, new LinearColor(8f, 8f, 8f), 0, 8);
        Array.Fill(pixels, new LinearColor(1f, 1f, 1f), 8, 8);

        var topDown = Decode(Encode(8, 2, pixels));
        var bottomUp = Decode(Encode(8, 2, pixels, bottomUp: true));

        Assert.Equal(8f, topDown[0, 0].R, 4);
        Assert.Equal(8f, bottomUp[0, 0].R, 4);
        Assert.Equal(1f, bottomUp[0, 1].R, 4);
    }

    [Fact]
    public void Codec_RejectsWhatItCannotDecode()
    {
        Assert.Throws<InvalidDataException>(() => Decode(Encoding.ASCII.GetBytes("not an image at all\n\n-Y 1 +X 8\n")));

        var xyz = Encoding.ASCII.GetBytes("#?RADIANCE\nFORMAT=32-bit_rle_xyze\n\n-Y 1 +X 8\n");
        Assert.Throws<InvalidDataException>(() => Decode(xyz));

        var transposed = Encoding.ASCII.GetBytes("#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n+X 8 -Y 1\n");
        Assert.Throws<InvalidDataException>(() => Decode(transposed));
    }

    [Fact]
    public void HdrImage_KeepsRangeAnEightBitImageCouldNot()
    {
        var pixels = Enumerable.Repeat(new LinearColor(64f, 64f, 64f), 8).ToArray();
        var image = Decode(Encode(8, 1, pixels));

        Assert.Equal(64f, image[0, 0].R, 3);
        Assert.Equal(64f, image.MaxLuminance, 2);

        var clipped = ColorRGB.FromPacked(image.ToTexture().Pixels[0]);
        Assert.Equal(255, clipped.R);
    }

    [Fact]
    public void HdrImage_SampleWrapsLongitudeAndClampsLatitude()
    {
        var pixels = new LinearColor[4 * 2];
        pixels[0] = new LinearColor(1f, 0f, 0f);
        pixels[3] = new LinearColor(1f, 0f, 0f);
        for (var i = 1; i <= 2; i++)
        {
            pixels[i] = LinearColor.Black;
        }
        for (var i = 4; i < 8; i++)
        {
            pixels[i] = new LinearColor(0f, 1f, 0f);
        }

        var image = new HdrImage(4, 2, ToFloats(pixels));

        Assert.Equal(1f, image.Sample(0f, 0.25f).R, 3);
        Assert.Equal(0f, image.Sample(0f, 0.25f).G, 3);

        Assert.Equal(image[0, 0].R, image.Sample(0.125f, -0.5f).R, 3);
        Assert.Equal(image[0, 1].G, image.Sample(0.125f, 1.5f).G, 3);
    }

    private static float[] ToFloats(LinearColor[] pixels)
    {
        var floats = new float[pixels.Length * 3];

        for (var i = 0; i < pixels.Length; i++)
        {
            floats[i * 3] = pixels[i].R;
            floats[i * 3 + 1] = pixels[i].G;
            floats[i * 3 + 2] = pixels[i].B;
        }

        return floats;
    }

    [Fact]
    public void Equirectangular_ProjectAndDirection_AreInverses()
    {
        foreach (var u in new[] { 0.05f, 0.25f, 0.5f, 0.75f, 0.95f })
        {
            foreach (var v in new[] { 0.1f, 0.35f, 0.5f, 0.9f })
            {
                var (backU, backV) = Equirectangular.Project(Equirectangular.Direction(u, v));

                Assert.Equal(u, backU, 4);
                Assert.Equal(v, backV, 4);
            }
        }
    }

    [Fact]
    public void Equirectangular_PutsTheMiddleOfThePanoramaAhead()
    {
        Assert.Equal(-Vector3.UnitZ, Equirectangular.Direction(0.5f, 0.5f), Approximately);
        Assert.Equal(Vector3.UnitY, Equirectangular.Direction(0.5f, 0f), Approximately);
        Assert.Equal(-Vector3.UnitY, Equirectangular.Direction(0.5f, 1f), Approximately);
        Assert.Equal(Vector3.UnitX, Equirectangular.Direction(0.75f, 0.5f), Approximately);
    }

    private static readonly IEqualityComparer<Vector3> Approximately =
        new VectorComparer(1e-4f);

    private sealed class VectorComparer(float tolerance) : IEqualityComparer<Vector3>
    {
        public bool Equals(Vector3 a, Vector3 b) => (a - b).Length() <= tolerance;

        public int GetHashCode(Vector3 v) => 0;
    }

    private static HdrImage LatitudeBands(int width, int height, Func<float, LinearColor> byV)
    {
        var pixels = new float[width * height * 3];

        for (var y = 0; y < height; y++)
        {
            var color = byV((y + 0.5f) / height);

            for (var x = 0; x < width; x++)
            {
                var i = (x + y * width) * 3;
                pixels[i] = color.R;
                pixels[i + 1] = color.G;
                pixels[i + 2] = color.B;
            }
        }

        return new HdrImage(width, height, pixels);
    }

    [Fact]
    public void ToCubeMap_LandsLatitudesOnTheRightFaces()
    {
        var panorama = LatitudeBands(64, 32, v => v < 0.5f
            ? new LinearColor(0f, 4f, 0f)
            : new LinearColor(2f, 0f, 0f));

        var cube = Equirectangular.ToCubeMap(panorama, resolution: 16, samplesPerAxis: 1);

        Assert.True(cube.IsHighDynamicRange);

        Assert.Equal(4f, cube.SampleRadiance(Vector3.UnitY).G, 2);
        Assert.Equal(0f, cube.SampleRadiance(Vector3.UnitY).R, 2);
        Assert.Equal(2f, cube.SampleRadiance(-Vector3.UnitY).R, 2);
    }

    [Fact]
    public void ToCubeMap_LandsLongitudesOnTheRightFaces()
    {
        var width = 64;
        var pixels = new float[width * 32 * 3];

        for (var y = 0; y < 32; y++)
        {
            var i = ((width / 2) + y * width) * 3;
            pixels[i] = 8f;
            pixels[i + 1] = 8f;
            pixels[i + 2] = 8f;
        }

        var cube = Equirectangular.ToCubeMap(new HdrImage(width, 32, pixels), resolution: 32, samplesPerAxis: 1);

        var ahead = cube.SampleRadiance(-Vector3.UnitZ).Luminance;
        var behind = cube.SampleRadiance(Vector3.UnitZ).Luminance;

        Assert.True(ahead > behind, $"ahead {ahead} should outshine behind {behind}");
        Assert.Equal(0f, behind, 3);
    }

    [Fact]
    public void ToCubeMap_FromBytes_ClaimsNoRangeItDoesNotHave()
    {
        var panorama = Texture.Checkerboard(16, 4, ColorRGB.White, ColorRGB.Black);
        var cube = Equirectangular.ToCubeMap(panorama, resolution: 8);

        Assert.False(cube.IsHighDynamicRange);
        Assert.Null(cube.Radiance(CubeFace.PositiveY));
    }

    [Fact]
    public void SampleRadiance_WithoutFloats_MatchesTheDecodedByteSample()
    {
        var faces = new Texture[6];
        for (var f = 0; f < 6; f++)
        {
            faces[f] = Texture.Checkerboard(8, 4, new ColorRGB(200, 130, 60), new ColorRGB(20, 40, 90));
        }

        var cube = new CubeMap(faces);

        foreach (var direction in new[]
        {
            Vector3.Normalize(new Vector3(0.3f, 0.9f, -0.2f)),
            Vector3.Normalize(new Vector3(-0.7f, 0.1f, 0.6f)),
            Vector3.Normalize(new Vector3(1f, -0.4f, 0.05f)),
        })
        {
            LinearColor expected = cube.Sample(direction);
            var actual = cube.SampleRadiance(direction);

            Assert.Equal(expected.R, actual.R);
            Assert.Equal(expected.G, actual.G);
            Assert.Equal(expected.B, actual.B);
        }
    }

    [Fact]
    public void GenerateRadiance_KeepsTheSunItWasGiven()
    {
        var sunward = Vector3.Normalize(new Vector3(0.2f, 0.9f, -0.4f));

        var sky = CubeMap.GenerateRadiance(32, direction =>
            Vector3.Dot(direction, sunward) > 0.99f
                ? new LinearColor(500f, 500f, 450f)
                : new LinearColor(0.1f, 0.15f, 0.3f));

        Assert.True(sky.IsHighDynamicRange);
        Assert.True(sky.SampleRadiance(sunward).R > 100f);

        Assert.True(sky.SampleRadiance(-sunward).R < 1f);

        var brightest = sky[CubeFace.PositiveY].Pixels.Max(p => ColorRGB.FromPacked(p).R);
        Assert.Equal(255, brightest);
    }

    [Fact]
    public void AmbientCube_TakesTheSunFromTheFloatFaces()
    {
        var sunward = Vector3.UnitY;

        LinearColor Shade(Vector3 direction) =>
            Vector3.Dot(direction, sunward) > 0.98f
                ? new LinearColor(1000f, 1000f, 1000f)
                : new LinearColor(0.05f, 0.05f, 0.08f);

        var hdr = CubeMap.GenerateRadiance(64, Shade);
        var ldr = CubeMap.Generate(64, direction => Shade(direction).ToColorRGB());

        var fromFloats = AmbientCube.FromEnvironment(hdr).Evaluate(Vector3.UnitY);
        var fromBytes = AmbientCube.FromEnvironment(ldr).Evaluate(Vector3.UnitY);

        Assert.True(fromFloats.Luminance > 10f * fromBytes.Luminance,
            $"floats {fromFloats.Luminance} should dwarf bytes {fromBytes.Luminance}");
    }

    [Fact]
    public void PrefilteredEnvironment_KeepsTheRangeAtRoughnessZero()
    {
        var sunward = Vector3.Normalize(new Vector3(0f, 1f, 0f));

        var sky = CubeMap.GenerateRadiance(32, direction =>
            Vector3.Dot(direction, sunward) > 0.95f
                ? new LinearColor(200f, 200f, 200f)
                : new LinearColor(0.05f, 0.05f, 0.1f));

        var prefiltered = PrefilteredEnvironment.Build(sky, baseResolution: 16, levelCount: 3);

        Assert.True(prefiltered.Sample(sunward, 0f).Luminance > 100f);

        Assert.True(prefiltered.Sample(sunward, 1f).Luminance > 1f);
    }
}
