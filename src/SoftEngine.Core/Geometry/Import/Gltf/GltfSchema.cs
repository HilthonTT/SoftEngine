using System.Text.Json.Serialization;

namespace SoftEngine.Core.Geometry.Import.Gltf;

internal sealed class GltfRoot
{
    public GltfAsset? Asset { get; set; }

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

    public float[]? Matrix { get; set; }

    public float[]? Translation { get; set; }

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
    public Dictionary<string, int> Attributes { get; set; } = [];

    public int? Indices { get; set; }

    public int? Material { get; set; }

    public int Mode { get; set; } = 4;

    public Dictionary<string, System.Text.Json.JsonElement> Extensions { get; set; } = [];
}

internal sealed class GltfAccessor
{
    public int? BufferView { get; set; }

    public int ByteOffset { get; set; }

    public int ComponentType { get; set; }

    public bool Normalized { get; set; }

    public int Count { get; set; }

    public string Type { get; set; } = "SCALAR";

    public GltfSparse? Sparse { get; set; }
}

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

    public int? ByteStride { get; set; }
}

internal sealed class GltfBuffer
{
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

    public string? Path { get; set; }
}

internal sealed class GltfAnimationSampler
{
    public int Input { get; set; } = -1;

    public int Output { get; set; } = -1;

    public string? Interpolation { get; set; }
}
