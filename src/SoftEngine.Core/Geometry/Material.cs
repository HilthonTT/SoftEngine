using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Textures;

namespace SoftEngine.Core.Geometry;

public sealed class Material
{
    public ColorRGB Diffuse { get; set; } = ColorRGB.Gray;

    public Texture? DiffuseMap { get; set; }

    public Texture? NormalMap { get; set; }

    public Texture? SpecularMap { get; set; }

    public float SpecularStrength { get; set; } = 0.35f;

    public float Shininess { get; set; } = 32f;

    public float NormalStrength { get; set; } = 1f;

    public float Metallic { get; set; }

    public float Roughness { get; set; } = 0.5f;

    public Texture? MetallicMap { get; set; }

    public Texture? RoughnessMap { get; set; }

    public ColorRGB Emissive { get; set; } = ColorRGB.Black;

    public Texture? EmissiveMap { get; set; }

    public float EmissiveStrength { get; set; } = 1f;

    public float AlphaCutoff { get; set; }

    public bool IsCutout => AlphaCutoff > 0f && DiffuseMap is not null;

    public bool NeedsTangents => NormalMap is not null;
}
