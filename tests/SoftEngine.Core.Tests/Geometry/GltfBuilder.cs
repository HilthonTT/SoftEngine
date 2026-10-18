using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace SoftEngine.Core.Tests.Geometry;

/// <summary>
/// Assembles glTF documents for the importer tests: a binary blob built up from named typed
/// arrays, and the JSON that addresses it.
///
/// The tests hand-write their JSON rather than shipping sample files, for the same reason the
/// Collada ones do — what is being tested is a <em>convention</em> (which way a matrix is
/// stored, which channel a roughness lives in, how a stride is counted), and a hand-written
/// buffer makes the expected answer unambiguous.
///
/// Values are substituted by <c>@name@</c> rather than by string interpolation, because JSON
/// is made of braces and a C# interpolated string reads <c>}}</c> as the end of a hole.
/// </summary>
internal sealed class GltfBuilder
{
    private readonly List<byte> _bytes = [];
    private readonly Dictionary<string, string> _values = [];

    /// <summary>Appends floats under a name the template can refer to as their byte offset.</summary>
    public GltfBuilder Floats(string name, params float[] values) =>
        Record(name, MemoryMarshal.AsBytes<float>(values));

    public GltfBuilder UShorts(string name, params ushort[] values) =>
        Record(name, MemoryMarshal.AsBytes<ushort>(values));

    /// <summary>Substitutes a value that is not a buffer offset — a mode, an interpolation name.</summary>
    public GltfBuilder With(string name, object value)
    {
        _values[name] = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        return this;
    }

    /// <summary>The document as a self-contained <c>.gltf</c>, its buffer inline as a data URI.</summary>
    public byte[] Gltf(string json) =>
        Encoding.UTF8.GetBytes(Fill(json)
            .Replace("@BUFFER@", "data:application/octet-stream;base64," + Convert.ToBase64String([.. _bytes])));

    /// <summary>
    /// The same document as a GLB: a 12-byte header, a JSON chunk and a binary chunk, each
    /// length-prefixed and padded to four bytes. A GLB's buffer carries no URI at all — its
    /// bytes <em>are</em> the second chunk.
    /// </summary>
    public byte[] Glb(string json)
    {
        var jsonChunk = Pad(Encoding.UTF8.GetBytes(Fill(json)), (byte)' ');
        var binaryChunk = Pad([.. _bytes], 0);

        var total = 12 + 8 + jsonChunk.Length + 8 + binaryChunk.Length;
        var result = new byte[total];

        BinaryPrimitives.WriteUInt32LittleEndian(result, 0x46546C67);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), (uint)total);

        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), (uint)jsonChunk.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(16), 0x4E4F534A);
        jsonChunk.CopyTo(result.AsSpan(20));

        var at = 20 + jsonChunk.Length;

        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(at), (uint)binaryChunk.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(at + 4), 0x004E4942);
        binaryChunk.CopyTo(result.AsSpan(at + 8));

        return result;
    }

    private string Fill(string json)
    {
        var filled = json.Replace("@LENGTH@", _bytes.Count.ToString(CultureInfo.InvariantCulture));

        foreach (var (name, value) in _values)
        {
            filled = filled.Replace($"@{name}@", value);
        }

        return filled;
    }

    private GltfBuilder Record(string name, ReadOnlySpan<byte> data)
    {
        // An accessor of a component type wider than a byte has to start on a multiple of
        // that width, and a reader is entitled to assume it.
        while (_bytes.Count % 4 != 0)
        {
            _bytes.Add(0);
        }

        _values[name] = _bytes.Count.ToString(CultureInfo.InvariantCulture);
        _values[name + "Length"] = data.Length.ToString(CultureInfo.InvariantCulture);

        _bytes.AddRange(data);

        return this;
    }

    private static byte[] Pad(byte[] data, byte filler)
    {
        var padding = (4 - (data.Length & 3)) & 3;
        if (padding == 0)
        {
            return data;
        }

        var result = new byte[data.Length + padding];
        data.CopyTo(result, 0);
        Array.Fill(result, filler, data.Length, padding);

        return result;
    }
}
