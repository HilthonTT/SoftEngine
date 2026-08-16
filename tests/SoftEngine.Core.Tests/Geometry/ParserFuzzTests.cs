using SoftEngine.Core.Geometry.Import;
using SoftEngine.Core.Geometry.Import.Gltf;
using SoftEngine.Core.Imaging;
using System.Text;
using System.Text.Json;
using System.Xml;

namespace SoftEngine.Core.Tests.Geometry;

/// <summary>
/// The file readers, fed files that are wrong.
///
/// <para>
/// These are the engine's attack surface, and the only part of it that ever sees bytes nobody
/// here wrote: a model off the internet, a panorama out of a download, a scene somebody edited
/// by hand and got a brace wrong in. SECURITY.md says as much. What the rest of the suite
/// checks is that a <em>correct</em> file decodes correctly, which is the easier half — a
/// reader can be right about every well-formed file and still be one truncated download away
/// from an unhandled exception, an index off the end of a buffer, or a sixty-byte header that
/// asks for a hundred gigabytes.
/// </para>
///
/// <para>
/// So: take a file that works, break it in every way a file breaks — a byte flipped, a length
/// field made enormous, the end cut off — and require that the reader still fails in a way its
/// caller could have written a catch for. The mutations come from a seeded generator, so a
/// failure names the seed that produced it and the same bytes come back on the next run.
/// </para>
/// </summary>
public class ParserFuzzTests
{
    /// <summary>
    /// How a reader is allowed to reject a file: the data is malformed, or it is well-formed
    /// and asks for something the reader does not implement.
    ///
    /// <para>
    /// <see cref="JsonException"/> and <see cref="XmlException"/> are on the list because glTF
    /// and Collada are a JSON document and an XML one, and "this is not valid JSON" is a
    /// perfectly good account of what went wrong with a file whose braces do not match.
    /// </para>
    ///
    /// <para>
    /// What is deliberately <em>not</em> on the list is every exception that means the reader
    /// walked off the end of something: <see cref="IndexOutOfRangeException"/>,
    /// <see cref="ArgumentOutOfRangeException"/>, <see cref="OverflowException"/>,
    /// <see cref="OutOfMemoryException"/>. Those are the bug, not the report of one, and a
    /// suite that accepted them would pass while the thing it exists to catch went on
    /// happening.
    /// </para>
    /// </summary>
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
            // Rejected, and rejected in a way a caller could have caught by name.
        }
        catch (Exception exception)
        {
            Assert.Fail(
                $"{what} mutated with seed {seed} threw {exception.GetType().Name} " +
                $"rather than rejecting the file: {exception.Message}\n{exception.StackTrace}");
        }
    }

    /// <summary>
    /// One mutation of <paramref name="seed"/>'s bytes, chosen by <paramref name="rng"/>.
    ///
    /// <para>
    /// Four kinds, because they break different things. A flipped byte finds the reader that
    /// trusts a field; a run of <c>0xFF</c> finds the one that trusts a <em>length</em>, which
    /// is the whole decompression-bomb shape and the mutation most likely to end in an
    /// allocation nobody meant; truncation finds the one that assumes the bytes it was
    /// promised are there; and a chopped-out slice finds the one whose offsets are computed
    /// rather than checked.
    /// </para>
    /// </summary>
    private static byte[] Mutate(byte[] seed, Random rng)
    {
        var bytes = (byte[])seed.Clone();

        // One to three of them, compounded. A single edit is usually caught by the first
        // check a reader makes; the interesting failures are the ones where a field has been
        // made enormous *and* the bytes behind it have been cut away.
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
                // A length field made as large as it can be. Aligned to four bytes because
                // that is where the length fields are in every binary format here.
                var at = rng.Next(bytes.Length / 4) * 4;
                for (var i = at; i < System.Math.Min(at + 4, bytes.Length); i++)
                {
                    bytes[i] = 0xFF;
                }

                return bytes;

            case 2:
                return bytes[..(1 + rng.Next(bytes.Length))];

            case 4:
                // Text formats carry their counts as digits, so the way to make one enormous is
                // to find a digit and grow the number it is part of.
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

    /// <summary>
    /// How many mutations each reader is fed.
    ///
    /// <para>
    /// Two thousand is what the suite runs by default: a couple of seconds a reader, which is
    /// what a test everybody runs on every change can cost. It is not the number that found
    /// the bugs these tests were written for — those turned up at mutations 24, 1,320 and
    /// 6,857 of a twelve-thousand-round sweep, and the last of them would sail straight
    /// through the default.
    /// </para>
    ///
    /// <para>
    /// So the three of them are pinned separately below as cases in their own right, and the
    /// sweep is for the next one. <c>SOFTENGINE_FUZZ_ROUNDS=200000</c> is the long version,
    /// for when there is time to go looking — the mutations are seeded by their round number,
    /// so a failure at 173,402 is a failure anyone can reproduce by asking for that many again.
    /// </para>
    /// </summary>
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
                // The temp file, not the reader: none of these take a path they open for
                // writing, so this is the machine — a virus scanner holding the file it just
                // watched appear, most often — and not a verdict on the bytes. Skipped rather
                // than failed, because a sweep long enough to be worth running is long enough
                // to meet it, and a test that fails for that reason gets switched off.
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

    // ---- seeds -------------------------------------------------------------------------

    /// <summary>A real PNG, produced by the encoder these tests are otherwise about.</summary>
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

    /// <summary>A flat (un-run-length-encoded) Radiance image, small enough to mutate densely.</summary>
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

    // ---- the seeds themselves have to load ---------------------------------------------

    /// <summary>
    /// Every fuzz case below is worthless if the file it mutates was already broken: a reader
    /// that rejects everything passes all of them. This is the control.
    /// </summary>
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

    // ---- the fuzz cases ----------------------------------------------------------------

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

    // ---- the bombs ---------------------------------------------------------------------
    //
    // A mutation finds these by accident at best. They are the shape the format invites: a
    // small file whose header describes an enormous one, which a reader either bounds or
    // allocates for.

    [Fact]
    public void Png_HeaderDeclaringAnEnormousImage_IsRefusedBeforeItIsAllocatedFor()
    {
        var bytes = PngSeed();

        // The IHDR's width and height, which sit 8 bytes past the signature plus the chunk's
        // own length-and-type prefix.
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

    // ---- what the sweep found ----------------------------------------------------------
    //
    // Each of these was a mutation that crashed a reader, cut down to the one thing about it
    // that mattered. They are here rather than left to the sweep because the sweep only finds
    // them at the depth it happens to be run to, and a regression test that depends on an
    // environment variable is not one.

    /// <summary>
    /// A face indexing past the vertices it was given. It used to surface as an
    /// <see cref="IndexOutOfRangeException"/> from inside the mesh constructor's own normal
    /// calculation — the model never opened, and nothing in the message said which file, or
    /// which face, or that an index was the problem at all.
    /// </summary>
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

    /// <summary>
    /// One token in an index array that is not a number. The geometry path parsed these
    /// through <c>Convert.ChangeType</c>, which threw <see cref="FormatException"/> and took
    /// the whole model with it — while the animation path of the same importer had always
    /// skipped what it could not read.
    /// </summary>
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

    /// <summary>
    /// Image data that is not a DEFLATE stream. Short data and corrupt data fail differently
    /// inside the decompressor — an end-of-stream against a <c>ZLibException</c> — and only
    /// the first of them was being turned into something a caller could catch.
    /// </summary>
    [Fact]
    public void Png_CorruptCompressedData_IsRejectedAsInvalidData()
    {
        var bytes = PngSeed();

        // Past the signature, the IHDR and the IDAT's own length and type: the deflate stream.
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
