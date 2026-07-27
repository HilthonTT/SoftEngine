using System.Text.Json.Serialization;

namespace SoftEngine.Core.Geometry.Gltf;

/// <summary>
/// The parts of the glTF 2.0 JSON this engine reads, as plain objects for
/// <see cref="System.Text.Json"/> to fill.
///
/// glTF is a flat file of parallel arrays that reference each other by index — a primitive
/// names an accessor, which names a buffer view, which names a buffer. Nothing is nested, and
/// almost everything is optional, so every list here defaults to empty and every index is
/// checked at the point it is followed rather than trusted because the file declared it.
/// </summary>
internal sealed class GltfRoot
{
    public GltfAsset? Asset { get; set; }

    /// <summary>Which of <see cref="Scenes"/> to show. Absent means the file is a library, not a scene.</summary>
    public int? Scene { get; set; }

    public List<GltfScene> Scenes { get; set; } = [];

    public List<GltfNode> Nodes { get; set; } = [];

    public List<GltfMesh> Meshes { get; set; } = [];

    public List<GltfAccessor> Accessors { get; set; } = [];

    public List<GltfBufferView> BufferViews { get; set; } = [];

    public List<GltfBuffer> Buffers { get; set; } = [];

    public List<GltfMaterial> Materials { get; set; } = [];

    public List<GltfTexture> Textures { get; set; } = [];

    public List<GltfImage> Images { get; set; } = [];

    public List<GltfSkin> Skins { get; set; } = [];

    public List<GltfAnimation> Animations { get; set; } = [];

    /// <summary>
    /// Extensions the file says it cannot be read without. Unlike <c>extensionsUsed</c>, this
    /// one is not advisory: a reader that ignores an entry here renders something the author
    /// did not write.
    /// </summary>
    public List<string> ExtensionsRequired { get; set; } = [];

    public List<string> ExtensionsUsed { get; set; } = [];
}

internal sealed class GltfAsset
{
    public string? Version { get; set; }

    public string? Generator { get; set; }
}

internal sealed class GltfScene
{
    public string? Name { get; set; }

    public List<int> Nodes { get; set; } = [];
}

internal sealed class GltfNode
{
    public string? Name { get; set; }

    public List<int> Children { get; set; } = [];

    /// <summary>The whole local transform, 16 floats. Mutually exclusive with the TRS trio.</summary>
    public float[]? Matrix { get; set; }

    public float[]? Translation { get; set; }

    /// <summary>Quaternion as (x, y, z, w) — the same order <see cref="System.Numerics.Quaternion"/> takes.</summary>
    public float[]? Rotation { get; set; }

    public float[]? Scale { get; set; }

    public int? Mesh { get; set; }

    public int? Skin { get; set; }

    public int? Camera { get; set; }
}

internal sealed class GltfMesh
{
    public string? Name { get; set; }

    public List<GltfPrimitive> Primitives { get; set; } = [];
}

internal sealed class GltfPrimitive
{
    /// <summary>Attribute name (POSITION, NORMAL, TEXCOORD_0, …) to accessor index.</summary>
    public Dictionary<string, int> Attributes { get; set; } = [];

    public int? Indices { get; set; }

    public int? Material { get; set; }

    /// <summary>Primitive topology; 4 (triangles) unless the file says otherwise.</summary>
    public int Mode { get; set; } = 4;

    public Dictionary<string, System.Text.Json.JsonElement> Extensions { get; set; } = [];
}

internal sealed class GltfAccessor
{
    public int? BufferView { get; set; }

    public int ByteOffset { get; set; }

    public int ComponentType { get; set; }

    /// <summary>Whether integer components encode a fraction of their own range rather than a count.</summary>
    public bool Normalized { get; set; }

    public int Count { get; set; }

    /// <summary>SCALAR, VEC2, VEC3, VEC4, MAT4, …</summary>
    public string Type { get; set; } = "SCALAR";

    public GltfSparse? Sparse { get; set; }
}

/// <summary>
/// An accessor that stores only the elements which differ from a base — the form a file uses
/// when one morph target moves fifty of a mesh's vertices and leaves the other fifty thousand
/// alone.
/// </summary>
internal sealed class GltfSparse
{
    public int Count { get; set; }

    public GltfSparseIndices? Indices { get; set; }

    public GltfSparseValues? Values { get; set; }
}

internal sealed class GltfSparseIndices
{
    public int BufferView { get; set; }

    public int ByteOffset { get; set; }

    public int ComponentType { get; set; }
}

internal sealed class GltfSparseValues
{
    public int BufferView { get; set; }

    public int ByteOffset { get; set; }
}

internal sealed class GltfBufferView
{
    public int Buffer { get; set; }

    public int ByteOffset { get; set; }

    public int ByteLength { get; set; }

    /// <summary>Bytes between the starts of consecutive elements; absent means tightly packed.</summary>
    public int? ByteStride { get; set; }
}

internal sealed class GltfBuffer
{
    /// <summary>A file path, a <c>data:</c> URI, or absent for the GLB container's own binary chunk.</summary>
    public string? Uri { get; set; }

    public int ByteLength { get; set; }
}

internal sealed class GltfMaterial
{
    public string? Name { get; set; }

    public GltfPbrMetallicRoughness? PbrMetallicRoughness { get; set; }

    public GltfNormalTextureInfo? NormalTexture { get; set; }

    public GltfTextureInfo? OcclusionTexture { get; set; }

    public GltfTextureInfo? EmissiveTexture { get; set; }

    public float[]? EmissiveFactor { get; set; }

    /// <summary>OPAQUE, MASK or BLEND.</summary>
    public string? AlphaMode { get; set; }

    public float AlphaCutoff { get; set; } = 0.5f;

    public bool DoubleSided { get; set; }

    public GltfMaterialExtensions? Extensions { get; set; }
}

internal sealed class GltfMaterialExtensions
{
    [JsonPropertyName("KHR_materials_emissive_strength")]
    public GltfEmissiveStrength? EmissiveStrength { get; set; }
}

internal sealed class GltfEmissiveStrength
{
    [JsonPropertyName("emissiveStrength")]
    public float Strength { get; set; } = 1f;
}

internal sealed class GltfPbrMetallicRoughness
{
    public float[]? BaseColorFactor { get; set; }

    public GltfTextureInfo? BaseColorTexture { get; set; }

    public float MetallicFactor { get; set; } = 1f;

    public float RoughnessFactor { get; set; } = 1f;

    /// <summary>Roughness in green, metalness in blue — the channels the engine's material already reads.</summary>
    public GltfTextureInfo? MetallicRoughnessTexture { get; set; }
}

internal class GltfTextureInfo
{
    public int Index { get; set; } = -1;

    public int TexCoord { get; set; }
}

internal sealed class GltfNormalTextureInfo : GltfTextureInfo
{
    public float Scale { get; set; } = 1f;
}

internal sealed class GltfTexture
{
    public int? Source { get; set; }

    public int? Sampler { get; set; }
}

internal sealed class GltfImage
{
    public string? Uri { get; set; }

    public string? MimeType { get; set; }

    public int? BufferView { get; set; }
}

internal sealed class GltfSkin
{
    public string? Name { get; set; }

    public int? InverseBindMatrices { get; set; }

    /// <summary>The node the skeleton hangs off. Advisory: the spec makes it optional.</summary>
    public int? Skeleton { get; set; }

    public List<int> Joints { get; set; } = [];
}

internal sealed class GltfAnimation
{
    public string? Name { get; set; }

    public List<GltfAnimationChannel> Channels { get; set; } = [];

    public List<GltfAnimationSampler> Samplers { get; set; } = [];
}

internal sealed class GltfAnimationChannel
{
    public int Sampler { get; set; } = -1;

    public GltfAnimationTarget? Target { get; set; }
}

internal sealed class GltfAnimationTarget
{
    public int? Node { get; set; }

    /// <summary>translation, rotation, scale or weights.</summary>
    public string? Path { get; set; }
}

internal sealed class GltfAnimationSampler
{
    /// <summary>Accessor of key times, in seconds.</summary>
    public int Input { get; set; } = -1;

    /// <summary>Accessor of keyed values.</summary>
    public int Output { get; set; } = -1;

    /// <summary>LINEAR, STEP or CUBICSPLINE.</summary>
    public string? Interpolation { get; set; }
}
