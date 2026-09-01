using SoftEngine.Core.Geometry.Import;
using SoftEngine.Core.Geometry.Import.Gltf;
using SoftEngine.Core.Imaging;
using System.Text;
using System.Text.Json;
using System.Xml;

namespace SoftEngine.Core.Tests.Geometry;

public class ParserFuzzTests
{
    private static bool IsAcceptable(Exception exception) =>
        exception is InvalidDataException or NotSupportedException or JsonException or XmlException;

    private static void AssertRejectedCleanly(string what, int seed, Action load)
    {
        try
        {
            load();
        }
        catch (Exception exception) when (IsAcceptable(exception))
        {
        }
        catch (Exception exception)
        {
            Assert.Fail(
                $"{what} mutated with seed {seed} threw {exception.GetType().Name} " +
                $"rather than rejecting the file: {exception.Message}\n{exception.StackTrace}");
        }
    }

    private static byte[] Mutate(byte[] seed, Random rng)
    {
        var bytes = (byte[])seed.Clone();

        for (var i = rng.Next(3); i >= 0; i--)
        {
            bytes = MutateOnce(bytes, rng);
        }

        return bytes;
    }

    private static byte[] MutateOnce(byte[] seed, Random rng)
    {
        if (seed.Length == 0)
        {
            return seed;
        }

        var bytes = (byte[])seed.Clone();

        switch (rng.Next(5))
        {
            case 0:
                for (var i = 0; i < 1 + rng.Next(8); i++)
                {
                    bytes[rng.Next(bytes.Length)] = (byte)rng.Next(256);
                }

                return bytes;

            case 1:

                var at = rng.Next(bytes.Length / 4) * 4;
                for (var i = at; i < System.Math.Min(at + 4, bytes.Length); i++)
                {
                    bytes[i] = 0xFF;
                }

                return bytes;

            case 2:
                return bytes[..(1 + rng.Next(bytes.Length))];

            case 4:

                for (var i = 0; i < bytes.Length; i++)
                {
                    var digit = (i + rng.Next(bytes.Length)) % bytes.Length;

                    if (bytes[digit] is >= (byte)'0' and <= (byte)'9')
                    {
                        for (var j = digit; j < System.Math.Min(digit + 10, bytes.Length); j++)
                        {
                            bytes[j] = (byte)'9';
                        }

                        break;
                    }
                }

                return bytes;

            default:
                var from = rng.Next(bytes.Length);
                var length = rng.Next(bytes.Length - from);

                return [.. bytes[..from], .. bytes[(from + length)..]];
        }
    }

    private static int Rounds =>
        int.TryParse(Environment.GetEnvironmentVariable("SOFTENGINE_FUZZ_ROUNDS"), out var rounds) && rounds > 0
            ? rounds
            : 2000;

    private static string WriteTemporary(byte[] bytes, string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"softengine-fuzz-{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, bytes);

        return path;
    }

    private static void FuzzFile(string what, byte[] seed, string extension, Action<string> load)
    {
        for (var round = 0; round < Rounds; round++)
        {
            var bytes = Mutate(seed, new Random(round));
            var path = WriteTemporary(bytes, extension);

            try
            {
                AssertRejectedCleanly(what, round, () => load(path));
            }
            catch (UnauthorizedAccessException)
            {
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    private static void FuzzBytes(string what, byte[] seed, Action<byte[]> load)
    {
        for (var round = 0; round < Rounds; round++)
        {
            var bytes = Mutate(seed, new Random(round));

            AssertRejectedCleanly(what, round, () => load(bytes));
        }
    }

    private static byte[] PngSeed()
    {
        var path = WriteTemporary([], ".png");

        try
        {
            var pixels = new int[8 * 8];
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = unchecked((int)0xFF102030) + i;
            }

            PngCodec.Save(path, pixels, 8, 8);

            return File.ReadAllBytes(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static byte[] HdrSeed()
    {
        var header = Encoding.ASCII.GetBytes("#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n-Y 4 +X 4\n");
        var pixels = new byte[4 * 4 * 4];

        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 128;
            pixels[i + 1] = 64;
            pixels[i + 2] = 32;
            pixels[i + 3] = 128;
        }

        return [.. header, .. pixels];
    }

    private static byte[] ObjSeed() => Encoding.UTF8.GetBytes(
        """
        # a triangle and a quad
        v 0 0 0
        v 1 0 0
        v 1 1 0
        v 0 1 0
        vt 0 0
        vt 1 0
        vt 1 1
        vn 0 0 1
        f 1/1/1 2/2/1 3/3/1
        f 1 2 3 4
        """);

    private static byte[] GltfSeed() =>
        new GltfBuilder()
            .Floats("positions", 0, 0, 0, 1, 0, 0, 0, 1, 0)
            .UShorts("indices", 0, 1, 2)
            .Glb(
                """
                {
                  "asset": { "version": "2.0" },
                  "scenes": [ { "nodes": [ 0 ] } ],
                  "nodes": [ { "mesh": 0 } ],
                  "meshes": [ { "primitives": [ { "attributes": { "POSITION": 0 }, "indices": 1 } ] } ],
                  "accessors": [
                    { "bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3" },
                    { "bufferView": 1, "componentType": 5123, "count": 3, "type": "SCALAR" }
                  ],
                  "bufferViews": [
                    { "buffer": 0, "byteOffset": @positions@, "byteLength": 36 },
                    { "buffer": 0, "byteOffset": @indices@, "byteLength": 6 }
                  ],
                  "buffers": [ { "byteLength": 42 } ]
                }
                """);

    private static byte[] ColladaSeed() => Encoding.UTF8.GetBytes(
        """
        <?xml version="1.0" encoding="utf-8"?>
        <COLLADA xmlns="http://www.collada.org/2005/11/COLLADASchema" version="1.4.1">
          <library_geometries>
            <geometry id="tri" name="tri">
              <mesh>
                <source id="tri-positions">
                  <float_array id="tri-positions-array" count="9">0 0 0 1 0 0 0 1 0</float_array>
                  <technique_common>
                    <accessor source="#tri-positions-array" count="3" stride="3">
                      <param name="X" type="float"/>
                      <param name="Y" type="float"/>
                      <param name="Z" type="float"/>
                    </accessor>
                  </technique_common>
                </source>
                <vertices id="tri-vertices">
                  <input semantic="POSITION" source="#tri-positions"/>
                </vertices>
                <triangles count="1">
                  <input semantic="VERTEX" source="#tri-vertices" offset="0"/>
                  <p>0 1 2</p>
                </triangles>
              </mesh>
            </geometry>
          </library_geometries>
        </COLLADA>
        """);

    [Fact]
    public void TheSeedFilesLoad()
    {
        var png = WriteTemporary(PngSeed(), ".png");
        var hdr = WriteTemporary(HdrSeed(), ".hdr");
        var obj = WriteTemporary(ObjSeed(), ".obj");
        var dae = WriteTemporary(ColladaSeed(), ".dae");

        try
        {
            Assert.Equal(8, PngCodec.Load(png).Width);
            Assert.Equal(4, RadianceHdrCodec.Load(hdr).Width);
            Assert.NotEmpty(ObjImporter.Import(obj));
            Assert.NotEmpty(ColladaImporter.HackyImportCollada(dae));
            Assert.NotEmpty(GltfImporter.Import(GltfSeed(), ".").Meshes);
        }
        finally
        {
            foreach (var path in new[] { png, hdr, obj, dae })
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Png_RejectsMutatedFilesCleanly() =>
        FuzzFile("PNG", PngSeed(), ".png", path => PngCodec.Load(path));

    [Fact]
    public void RadianceHdr_RejectsMutatedFilesCleanly() =>
        FuzzBytes("Radiance HDR", HdrSeed(), bytes => RadianceHdrCodec.Load(new MemoryStream(bytes)));

    [Fact]
    public void Obj_RejectsMutatedFilesCleanly() =>
        FuzzFile("OBJ", ObjSeed(), ".obj", path => ObjImporter.Import(path));

    [Fact]
    public void Gltf_RejectsMutatedFilesCleanly() =>
        FuzzBytes("GLB", GltfSeed(), bytes => GltfImporter.Import(bytes, "."));

    [Fact]
    public void Collada_RejectsMutatedFilesCleanly() =>
        FuzzFile("Collada", ColladaSeed(), ".dae", path => ColladaImporter.HackyImportCollada(path));

    [Fact]
    public void Png_HeaderDeclaringAnEnormousImage_IsRefusedBeforeItIsAllocatedFor()
    {
        var bytes = PngSeed();

        var at = 8 + 8;
        for (var i = at; i < at + 8; i++)
        {
            bytes[i] = 0x7F;
        }

        var path = WriteTemporary(bytes, ".png");

        try
        {
            var failure = Assert.Throws<InvalidDataException>(() => PngCodec.Load(path));
            Assert.Contains("decoder accepts", failure.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RadianceHdr_ResolutionLineDeclaringAnEnormousImage_IsRefusedBeforeItIsAllocatedFor()
    {
        var bytes = Encoding.ASCII.GetBytes(
            "#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n-Y 2000000000 +X 2000000000\n");

        var failure = Assert.Throws<InvalidDataException>(
            () => RadianceHdrCodec.Load(new MemoryStream(bytes)));

        Assert.Contains("this reader allocates for", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Gltf_AccessorDeclaringMoreElementsThanTheFileCouldHold_IsRefused()
    {
        var document = Encoding.UTF8.GetBytes(
            """
            {
              "asset": { "version": "2.0" },
              "scenes": [ { "nodes": [ 0 ] } ],
              "nodes": [ { "mesh": 0 } ],
              "meshes": [ { "primitives": [ { "attributes": { "POSITION": 0 } } ] } ],
              "accessors": [ { "componentType": 5126, "count": 2000000000, "type": "MAT4" } ],
              "bufferViews": [],
              "buffers": []
            }
            """);

        var failure = Assert.Throws<InvalidDataException>(() => GltfImporter.Import(document, "."));

        Assert.Contains("this reader will hold", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Collada_FaceIndexingPastTheVertices_LoadsWithoutIt()
    {
        var document = Encoding.UTF8.GetString(ColladaSeed()).Replace("<p>0 1 2</p>", "<p>0 1 900</p>");
        var path = WriteTemporary(Encoding.UTF8.GetBytes(document), ".dae");

        try
        {
            var meshes = ColladaImporter.HackyImportCollada(path);

            Assert.Empty(meshes[0].Triangles);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Collada_NonNumericTokenInAnIndexArray_DoesNotFailTheLoad()
    {
        var document = Encoding.UTF8.GetString(ColladaSeed()).Replace("<p>0 1 2</p>", "<p>0 1* 2</p>");
        var path = WriteTemporary(Encoding.UTF8.GetBytes(document), ".dae");

        try
        {
            ColladaImporter.HackyImportCollada(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Png_CorruptCompressedData_IsRejectedAsInvalidData()
    {
        var bytes = PngSeed();

        var idat = 8 + 12 + 13 + 8;
        for (var i = idat; i < System.Math.Min(idat + 16, bytes.Length); i++)
        {
            bytes[i] = 0x5A;
        }

        var path = WriteTemporary(bytes, ".png");

        try
        {
            Assert.Throws<InvalidDataException>(() => PngCodec.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
