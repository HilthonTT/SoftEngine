using SoftEngine.Core.Diagnostics;

namespace SoftEngine.Core.Geometry;

/// <summary>
/// How a mesh's surface responds to light: a base colour, the maps that vary it across the
/// surface, and the two numbers that shape its highlight.
///
/// The engine keeps one material per mesh — the OBJ importer already splits a file into one
/// mesh per material used, so a material never has to be selected per triangle.
/// </summary>
public sealed class Material
{
    /// <summary>Base colour where <see cref="DiffuseMap"/> is absent, and the tint applied where it isn't.</summary>
    public ColorRGB Diffuse { get; set; } = ColorRGB.Gray;

    /// <summary>Albedo texture, sampled by the mesh's UVs. sRGB-encoded, like every other colour here.</summary>
    public Texture? DiffuseMap { get; set; }

    /// <summary>
    /// Tangent-space normal map: RGB encodes a unit vector as (v + 1) / 2, with +Z out of
    /// the surface. Unlike the diffuse map it holds directions, not colour, so it is never
    /// gamma-decoded — that would bend every normal it stores.
    /// </summary>
    public Texture? NormalMap { get; set; }

    /// <summary>Per-texel multiplier for <see cref="SpecularStrength"/>; only the red channel is read.</summary>
    public Texture? SpecularMap { get; set; }

    /// <summary>How much of the light's specular reflection reaches the eye. 0 is a matte surface.</summary>
    public float SpecularStrength { get; set; } = 0.35f;

    /// <summary>Blinn-Phong exponent: higher is a tighter, glossier highlight.</summary>
    public float Shininess { get; set; } = 32f;

    /// <summary>
    /// Scales how far the normal map tilts the surface normal. 0 falls back to the
    /// interpolated vertex normal, 1 uses the map as authored, above 1 exaggerates it.
    /// </summary>
    public float NormalStrength { get; set; } = 1f;

    /// <summary>Whether this material needs per-pixel tangents — that is, whether it has a normal map.</summary>
    public bool NeedsTangents => NormalMap is not null;
}
