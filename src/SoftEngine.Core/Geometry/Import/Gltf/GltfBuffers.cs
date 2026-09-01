using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace SoftEngine.Core.Geometry.Import.Gltf;

internal sealed class GltfBuffers
{
    private const uint GlbMagic = 0x46546C67;
    private const uint ChunkJson = 0x4E4F534A;
    private const uint ChunkBinary = 0x004E4942;

    private const int ComponentByte = 5120;
    private const int ComponentUnsignedByte = 5121;
    private const int ComponentShort = 5122;
    private const int ComponentUnsignedShort = 5123;
    private const int ComponentUnsignedInt = 5125;
    private const int ComponentFloat = 5126;

    public const int MaxComponents = 64 * 1024 * 1024;

    private readonly GltfRoot _root;
    private readonly byte[]?[] _buffers;

    public GltfBuffers(GltfRoot root, byte[]? binaryChunk, string baseDirectory)
    {
        _root = root;
        _buffers = new byte[]?[root.Buffers.Count];

        for (var i = 0; i < root.Buffers.Count; i++)
        {
            var uri = root.Buffers[i].Uri;

            _buffers[i] = uri is null ? binaryChunk : ResolveUri(uri, baseDirectory);
        }
    }

    public static (string Json, byte[]? Binary) ReadGlb(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 12 || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != GlbMagic)
        {
            throw new InvalidDataException("Not a GLB container: the file does not start with the glTF magic number.");
        }

        var version = BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]);
        if (version != 2)
        {
            throw new NotSupportedException($"GLB container version {version} is not supported; this reader implements glTF 2.0.");
        }

        string? json = null;
        byte[]? binary = null;

        var offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
            var kind = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(offset + 4)..]);

            offset += 8;

            if (length < 0 || offset + length > bytes.Length)
            {
                break;
            }

            var chunk = bytes.Slice(offset, length);

            if (kind == ChunkJson && json is null)
            {
                json = Encoding.UTF8.GetString(chunk);
            }
            else if (kind == ChunkBinary && binary is null)
            {
                binary = chunk.ToArray();
            }

            offset += length;
            offset += (4 - (offset & 3)) & 3;
        }

        return json is null
            ? throw new InvalidDataException("The GLB container has no JSON chunk.")
            : (json, binary);
    }

    public static int ComponentsOf(string type) => type switch
    {
        "SCALAR" => 1,
        "VEC2" => 2,
        "VEC3" => 3,
        "VEC4" => 4,
        "MAT2" => 4,
        "MAT3" => 9,
        "MAT4" => 16,
        _ => 0,
    };

    public static int SizeOf(int componentType) => componentType switch
    {
        ComponentByte or ComponentUnsignedByte => 1,
        ComponentShort or ComponentUnsignedShort => 2,
        ComponentUnsignedInt or ComponentFloat => 4,
        _ => 0,
    };

    public GltfAccessor? AccessorAt(int? index) =>
        index is { } i && i >= 0 && i < _root.Accessors.Count ? _root.Accessors[i] : null;

    public float[] ReadFloats(int? accessorIndex)
    {
        if (AccessorAt(accessorIndex) is not { } accessor)
        {
            return [];
        }

        var components = ComponentsOf(accessor.Type);
        if (components == 0)
        {
            return [];
        }

        var result = new float[ComponentCount(accessor, components)];

        Read(accessor, components, result);
        ApplySparse(accessor, components, result);

        return result;
    }

    private static int ComponentCount(GltfAccessor accessor, int components)
    {
        var total = (long)accessor.Count * components;

        if (accessor.Count < 0 || total > MaxComponents)
        {
            throw new InvalidDataException(
                $"A glTF accessor declares {accessor.Count:N0} {accessor.Type} elements " +
                $"({total:N0} components), past the {MaxComponents:N0} this reader will hold.");
        }

        return (int)total;
    }

    public Vector3[] ReadVector3(int? accessorIndex) => Pack3(ReadFloats(accessorIndex));

    public Vector2[] ReadVector2(int? accessorIndex) => Pack2(ReadFloats(accessorIndex));

    public Vector4[] ReadVector4(int? accessorIndex) => Pack4(ReadFloats(accessorIndex));

    public Matrix4x4[] ReadMatrices(int? accessorIndex)
    {
        var floats = ReadFloats(accessorIndex);
        var count = floats.Length / 16;

        var result = new Matrix4x4[count];

        for (var i = 0; i < count; i++)
        {
            var o = i * 16;

            result[i] = new Matrix4x4(
                floats[o + 0], floats[o + 1], floats[o + 2], floats[o + 3],
                floats[o + 4], floats[o + 5], floats[o + 6], floats[o + 7],
                floats[o + 8], floats[o + 9], floats[o + 10], floats[o + 11],
                floats[o + 12], floats[o + 13], floats[o + 14], floats[o + 15]);
        }

        return result;
    }

    public int[] ReadIndices(int? accessorIndex)
    {
        if (AccessorAt(accessorIndex) is not { } accessor)
        {
            return [];
        }

        var components = ComponentsOf(accessor.Type);
        if (components == 0)
        {
            return [];
        }

        var result = new int[ComponentCount(accessor, components)];

        if (Locate(accessor.BufferView, accessor.ByteOffset, out var data, out var stride))
        {
            var size = SizeOf(accessor.ComponentType);
            var element = stride > 0 ? stride : size * components;

            for (var i = 0; i < accessor.Count; i++)
            {
                var start = (long)i * element;

                for (var c = 0; c < components; c++)
                {
                    var at = start + ((long)c * size);

                    result[i * components + c] = at >= 0 && at + size <= data.Length
                        ? ReadInteger(data, (int)at, accessor.ComponentType)
                        : 0;
                }
            }
        }

        return result;
    }

    public ReadOnlyMemory<byte> ViewBytes(int? bufferViewIndex)
    {
        if (bufferViewIndex is not { } index || index < 0 || index >= _root.BufferViews.Count)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        var view = _root.BufferViews[index];

        if (view.Buffer < 0 || view.Buffer >= _buffers.Length || _buffers[view.Buffer] is not { } buffer)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        var offset = System.Math.Clamp(view.ByteOffset, 0, buffer.Length);
        var length = System.Math.Clamp(view.ByteLength, 0, buffer.Length - offset);

        return buffer.AsMemory(offset, length);
    }

    private void Read(GltfAccessor accessor, int components, float[] destination)
    {
        if (!Locate(accessor.BufferView, accessor.ByteOffset, out var data, out var stride))
        {
            return;
        }

        var size = SizeOf(accessor.ComponentType);
        if (size == 0)
        {
            return;
        }

        var element = stride > 0 ? stride : size * components;

        for (var i = 0; i < accessor.Count; i++)
        {
            var start = (long)i * element;

            for (var c = 0; c < components; c++)
            {
                var at = start + ((long)c * size);

                destination[i * components + c] = at >= 0 && at + size <= data.Length
                    ? ReadComponent(data, (int)at, accessor.ComponentType, accessor.Normalized)
                    : 0f;
            }
        }
    }

    private void ApplySparse(GltfAccessor accessor, int components, float[] destination)
    {
        if (accessor.Sparse is not { Count: > 0 } sparse ||
            sparse.Indices is not { } indices ||
            sparse.Values is not { } values)
        {
            return;
        }

        if (!Locate(indices.BufferView, indices.ByteOffset, out var indexData, out _) ||
            !Locate(values.BufferView, values.ByteOffset, out var valueData, out _))
        {
            return;
        }

        var indexSize = SizeOf(indices.ComponentType);
        var valueSize = SizeOf(accessor.ComponentType);

        if (indexSize == 0 || valueSize == 0)
        {
            return;
        }

        for (var i = 0; i < sparse.Count; i++)
        {
            var indexAt = i * indexSize;
            if (indexAt + indexSize > indexData.Length)
            {
                break;
            }

            var target = ReadInteger(indexData, indexAt, indices.ComponentType);
            if (target < 0 || target >= accessor.Count)
            {
                continue;
            }

            for (var c = 0; c < components; c++)
            {
                var at = (i * components + c) * valueSize;
                if (at + valueSize > valueData.Length)
                {
                    return;
                }

                destination[target * components + c] =
                    ReadComponent(valueData, at, accessor.ComponentType, accessor.Normalized);
            }
        }
    }

    private bool Locate(int? bufferViewIndex, int byteOffset, out ReadOnlySpan<byte> data, out int stride)
    {
        data = default;
        stride = 0;

        if (bufferViewIndex is not { } index || index < 0 || index >= _root.BufferViews.Count)
        {
            return false;
        }

        var view = _root.BufferViews[index];

        if (view.Buffer < 0 || view.Buffer >= _buffers.Length || _buffers[view.Buffer] is not { } buffer)
        {
            return false;
        }

        var start = view.ByteOffset + byteOffset;
        if (start < 0 || start > buffer.Length)
        {
            return false;
        }

        var available = System.Math.Min(view.ByteLength - byteOffset, buffer.Length - start);
        if (available <= 0)
        {
            return false;
        }

        data = buffer.AsSpan(start, available);
        stride = view.ByteStride ?? 0;

        return true;
    }

    private static float ReadComponent(ReadOnlySpan<byte> data, int at, int componentType, bool normalized) =>
        componentType switch
        {
            ComponentFloat => BinaryPrimitives.ReadSingleLittleEndian(data[at..]),

            ComponentUnsignedByte => normalized ? data[at] / 255f : data[at],
            ComponentByte => normalized ? MathF.Max((sbyte)data[at] / 127f, -1f) : (sbyte)data[at],

            ComponentUnsignedShort => normalized
                ? BinaryPrimitives.ReadUInt16LittleEndian(data[at..]) / 65535f
                : BinaryPrimitives.ReadUInt16LittleEndian(data[at..]),

            ComponentShort => normalized
                ? MathF.Max(BinaryPrimitives.ReadInt16LittleEndian(data[at..]) / 32767f, -1f)
                : BinaryPrimitives.ReadInt16LittleEndian(data[at..]),

            ComponentUnsignedInt => BinaryPrimitives.ReadUInt32LittleEndian(data[at..]),

            _ => 0f,
        };

    private static int ReadInteger(ReadOnlySpan<byte> data, int at, int componentType) =>
        componentType switch
        {
            ComponentUnsignedByte => data[at],
            ComponentByte => (sbyte)data[at],
            ComponentUnsignedShort => BinaryPrimitives.ReadUInt16LittleEndian(data[at..]),
            ComponentShort => BinaryPrimitives.ReadInt16LittleEndian(data[at..]),
            ComponentUnsignedInt => (int)BinaryPrimitives.ReadUInt32LittleEndian(data[at..]),
            ComponentFloat => (int)BinaryPrimitives.ReadSingleLittleEndian(data[at..]),
            _ => 0,
        };

    internal static byte[]? ResolveUri(string uri, string baseDirectory)
    {
        if (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var comma = uri.IndexOf(',');
            if (comma < 0)
            {
                return null;
            }

            var header = uri.AsSpan(5, comma - 5);
            var payload = uri[(comma + 1)..];

            if (!header.Contains("base64", StringComparison.OrdinalIgnoreCase))
            {
                return Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload));
            }

            try
            {
                return Convert.FromBase64String(payload);
            }
            catch (FormatException)
            {
                return null;
            }
        }

        try
        {
            var path = Path.Combine(baseDirectory, Uri.UnescapeDataString(uri.Replace('/', Path.DirectorySeparatorChar)));
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static Vector2[] Pack2(float[] floats)
    {
        var result = new Vector2[floats.Length / 2];

        for (var i = 0; i < result.Length; i++)
        {
            result[i] = new Vector2(floats[i * 2], floats[i * 2 + 1]);
        }

        return result;
    }

    private static Vector3[] Pack3(float[] floats)
    {
        var result = new Vector3[floats.Length / 3];

        for (var i = 0; i < result.Length; i++)
        {
            result[i] = new Vector3(floats[i * 3], floats[i * 3 + 1], floats[i * 3 + 2]);
        }

        return result;
    }

    private static Vector4[] Pack4(float[] floats)
    {
        var result = new Vector4[floats.Length / 4];

        for (var i = 0; i < result.Length; i++)
        {
            result[i] = new Vector4(floats[i * 4], floats[i * 4 + 1], floats[i * 4 + 2], floats[i * 4 + 3]);
        }

        return result;
    }
}
