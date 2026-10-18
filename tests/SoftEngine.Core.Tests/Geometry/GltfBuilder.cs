using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace SoftEngine.Core.Tests.Geometry;

internal sealed class GltfBuilder
{
    private readonly List<byte> _bytes = [];
    private readonly Dictionary<string, string> _values = [];

    public GltfBuilder Floats(string name, params float[] values) =>
        Record(name, MemoryMarshal.AsBytes<float>(values));

    public GltfBuilder UShorts(string name, params ushort[] values) =>
        Record(name, MemoryMarshal.AsBytes<ushort>(values));

    public GltfBuilder With(string name, object value)
    {
        _values[name] = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        return this;
    }

    public byte[] Gltf(string json) =>
        Encoding.UTF8.GetBytes(Fill(json)
            .Replace("@BUFFER@", "data:application/octet-stream;base64," + Convert.ToBase64String([.. _bytes])));

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
