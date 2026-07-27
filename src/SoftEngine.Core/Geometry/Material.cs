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

    /// <summary>
    /// How metallic the surface is: 0 is a dielectric (plastic, stone, skin), 1 is bare
    /// metal. Read by the physically-based path only.
    ///
    /// The parameter is a switch rather than a dial, because that is what it describes. A
    /// dielectric reflects a few percent of the light at every angle and scatters the rest
    /// as diffuse colour; a metal has no diffuse term at all and tints its reflection with
    /// the albedo instead. Values in between exist only to let a texture cross from one to
    /// the other without a seam.
    /// </summary>
    public float Metallic { get; set; }

    /// <summary>
    /// How rough the surface is, in [0, 1]: 0 is a mirror, 1 is fully diffuse. Read by the
    /// physically-based path in place of <see cref="Shininess"/>, which measures the same
    /// thing on a scale with no physical meaning and no top.
    /// </summary>
    public float Roughness { get; set; } = 0.5f;

    /// <summary>
    /// Per-texel <see cref="Metallic"/>, read from the <em>blue</em> channel, and per-texel
    /// <see cref="Roughness"/>, read from the <em>green</em> one.
    ///
    /// Those are the channels glTF's packed metallic-roughness texture puts them in, so one
    /// packed map can be assigned to both properties and each will find its own channel. A
    /// greyscale map — which is what OBJ's <c>map_Pm</c> and <c>map_Pr</c> are — works
    /// either way round, since all three of its channels carry the same value.
    /// </summary>
    public Texture? MetallicMap { get; set; }

    /// <inheritdoc cref="MetallicMap"/>
    public Texture? RoughnessMap { get; set; }

    /// <summary>
    /// Light the surface emits on its own, added after everything else. Black (the default)
    /// emits nothing.
    ///
    /// It is not a light: it brightens the surface it is on and nothing around it. What it
    /// is for is the part of a model that should read as *being* bright — a screen, a
    /// filament, a hot vent — which on an HDR target can sit above white and bloom.
    /// </summary>
    public ColorRGB Emissive { get; set; } = ColorRGB.Black;

    /// <summary>Per-texel multiplier for <see cref="Emissive"/>, sampled as sRGB colour.</summary>
    public Texture? EmissiveMap { get; set; }

    /// <summary>Scales <see cref="Emissive"/>; above 1 needs an HDR target to mean anything.</summary>
    public float EmissiveStrength { get; set; } = 1f;

    /// <summary>Whether this material needs per-pixel tangents — that is, whether it has a normal map.</summary>
    public bool NeedsTangents => NormalMap is not null;
}
