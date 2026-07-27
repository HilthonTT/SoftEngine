using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace SoftEngine.Core.Geometry.Gltf;

/// <summary>
/// The binary half of a glTF file: the buffers themselves, and the reading of typed elements
/// out of them through the accessor / buffer-view indirection.
///
/// Everything a glTF holds that is not a name or a number lives in one of these buffers, and
/// an accessor is the only thing that says how to interpret a stretch of one. Positions can
/// arrive as floats, indices as bytes, shorts or ints, joint indices as either width, and
/// weights as floats or as normalized integers — and any of them can be interleaved with the
/// others at a stride the buffer view names. Decoding all of that here means the importer
/// above asks for <see cref="ReadVector3"/> and never learns which of the six encodings the
/// file happened to use.
/// </summary>
internal sealed class GltfBuffers
{
    private const uint GlbMagic = 0x46546C67;      // "glTF"
    private const uint ChunkJson = 0x4E4F534A;     // "JSON"
    private const uint ChunkBinary = 0x004E4942;   // "BIN\0"

    private const int ComponentByte = 5120;
    private const int ComponentUnsignedByte = 5121;
    private const int ComponentShort = 5122;
    private const int ComponentUnsignedShort = 5123;
    private const int ComponentUnsignedInt = 5125;
    private const int ComponentFloat = 5126;

    private readonly GltfRoot _root;
    private readonly byte[]?[] _buffers;

    public GltfBuffers(GltfRoot root, byte[]? binaryChunk, string baseDirectory)
    {
        _root = root;
        _buffers = new byte[]?[root.Buffers.Count];

        for (var i = 0; i < root.Buffers.Count; i++)
        {
            var uri = root.Buffers[i].Uri;

            // A buffer with no URI is the GLB container's own binary chunk. The spec allows
            // only the first buffer to be that, and only in a GLB.
            _buffers[i] = uri is null ? binaryChunk : ResolveUri(uri, baseDirectory);
        }
    }

    /// <summary>
    /// Splits a <c>.glb</c> into its JSON text and its binary chunk. The container is a
    /// 12-byte header followed by length-prefixed chunks; anything past the two the spec
    /// defines is skipped rather than rejected, since the format reserves the room for
    /// exactly that.
    /// </summary>
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

            // Chunks are padded to a four-byte boundary.
            offset += length;
            offset += (4 - (offset & 3)) & 3;
        }

        return json is null
            ? throw new InvalidDataException("The GLB container has no JSON chunk.")
            : (json, binary);
    }

    /// <summary>How many components one element of an accessor holds.</summary>
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

    /// <summary>
    /// Every component of an accessor, flattened and converted to float. Normalized integers
    /// are scaled into their unit range here, so a weight stored as a byte and one stored as a
    /// float both arrive as the same fraction.
    /// </summary>
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

        var result = new float[accessor.Count * components];

        Read(accessor, components, result);
        ApplySparse(accessor, components, result);

        return result;
    }

    public Vector3[] ReadVector3(int? accessorIndex) => Pack3(ReadFloats(accessorIndex));

    public Vector2[] ReadVector2(int? accessorIndex) => Pack2(ReadFloats(accessorIndex));

    public Vector4[] ReadVector4(int? accessorIndex) => Pack4(ReadFloats(accessorIndex));

    /// <summary>
    /// Matrices, read straight into the engine's layout.
    ///
    /// glTF stores a matrix as sixteen floats in column-major order for the column-vector
    /// convention — element (row r, column c) at index c·4 + r. This engine composes
    /// row-vector matrices, which are the transpose. Transposing a column-major array is
    /// reading it row-major, so the sixteen floats go into
    /// <see cref="Matrix4x4(float, float, float, float, float, float, float, float, float, float, float, float, float, float, float, float)"/>
    /// in the order they appear and nothing else has to happen. (Collada writes the same
    /// matrices the other way round, which is why that importer transposes and this one does
    /// not — the two files disagree, not the two engines.)
    /// </summary>
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

    /// <summary>
    /// An accessor read as unsigned integers — vertex indices and joint indices, which arrive
    /// as bytes, shorts or ints depending on how many of them the file needs to address.
    /// </summary>
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

        var result = new int[accessor.Count * components];

        if (Locate(accessor.BufferView, accessor.ByteOffset, out var data, out var stride))
        {
            var size = SizeOf(accessor.ComponentType);
            var element = stride > 0 ? stride : size * components;

            for (var i = 0; i < accessor.Count; i++)
            {
                var start = i * element;

                for (var c = 0; c < components; c++)
                {
                    var at = start + c * size;

                    result[i * components + c] = at + size <= data.Length
                        ? ReadInteger(data, at, accessor.ComponentType)
                        : 0;
                }
            }
        }

        return result;
    }

    /// <summary>The raw bytes a buffer view covers, for an image embedded in the file.</summary>
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
        // An accessor with no buffer view reads as zeros, which the spec defines and a sparse
        // accessor then writes over.
        if (!Locate(accessor.BufferView, accessor.ByteOffset, out var data, out var stride))
        {
            return;
        }

        var size = SizeOf(accessor.ComponentType);
        if (size == 0)
        {
            return;
        }

        // A stride the buffer view declares is between elements, not between components, so
        // an interleaved attribute steps by it and a packed one by its own size.
        var element = stride > 0 ? stride : size * components;

        for (var i = 0; i < accessor.Count; i++)
        {
            var start = i * element;

            for (var c = 0; c < components; c++)
            {
                var at = start + c * size;

                destination[i * components + c] = at + size <= data.Length
                    ? ReadComponent(data, at, accessor.ComponentType, accessor.Normalized)
                    : 0f;
            }
        }
    }

    /// <summary>
    /// Overwrites the elements a sparse accessor says differ from the base it was read from.
    /// Ignoring this silently renders the base — which for a mesh that stores its positions
    /// sparsely is the wrong geometry, not a missing detail.
    /// </summary>
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

    /// <summary>Resolves a buffer view plus an accessor's own offset to the bytes it addresses.</summary>
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

            // The spec's own normalization factors. Signed types divide by their maximum and
            // clamp at -1, so that -128 and -127 both mean exactly -1 rather than the curve
            // being lopsided by one step.
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

    /// <summary>
    /// A buffer's contents: a file beside the glTF, or the bytes of a <c>data:</c> URI. A URI
    /// that cannot be resolved yields null rather than throwing, so a model with one missing
    /// side-car still loads whatever the other buffers hold.
    /// </summary>
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
