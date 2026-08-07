using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;

namespace SoftEngine.Core.Shading;

/// <summary>
/// What a surface does to a reflection, in the two numbers a specular reflection is defined
/// by: the fraction of light it reflects head-on, per channel, and how sharply it reflects it.
///
/// <para>
/// This is the whole of the frame's surface description — a G-buffer with two fields. It
/// exists because a screen-space reflection cannot be computed from the finished image: the
/// image says what colour a pixel ended up, not whether the thing at that pixel was a mirror
/// or a brick. Every other screen-space effect in this engine reads only depth, and depth is
/// enough for them, because occlusion and blur are properties of where a surface is rather
/// than what it is made of.
/// </para>
///
/// <para>
/// <see cref="Reflectivity"/> is F0 — the Fresnel reflectance at normal incidence — and is
/// carried per channel rather than as one number because for a metal it is <em>both</em>
/// quantities a reflection needs. A dielectric's F0 is a colourless few percent; a metal's is
/// its albedo, which is why gold reflects a white wall as gold. One channel would have made
/// every metal in the scene a mirror the colour of whatever it was looking at, and the
/// alternative — carrying albedo separately — is the deferred path the roadmap wants and this
/// is deliberately not.
/// </para>
///
/// <para>
/// Packed into a <see cref="uint"/> (F0 in the top three bytes, roughness in the low one)
/// because it is stored per pixel for a whole frame and read back by a full-screen pass:
/// four bytes is one buffer the size of the z-buffer's half, and the pack is two shifts.
/// <c>default</c> is a surface that reflects nothing, which is what a painter that never
/// tagged its state describes.
/// </para>
/// </summary>
public readonly struct SurfaceReflectance
{
    // Below this, the reflection is weaker than the rounding on the byte that would carry it.
    private const float Negligible = 1f / 512f;

    private readonly uint _packed;

    private SurfaceReflectance(uint packed) => _packed = packed;

    /// <summary>The bit pattern, for the buffer that stores one of these per pixel.</summary>
    public uint Packed => _packed;

    /// <summary>A surface that reflects nothing — the default, and what a matte material gives.</summary>
    public static SurfaceReflectance None => default;

    /// <summary>Whether this surface reflects enough to be worth tracing a ray for.</summary>
    public bool IsReflective => (_packed & 0xFFFFFF00u) != 0;

    /// <summary>Fresnel reflectance at normal incidence, per channel, in [0, 1].</summary>
    public LinearColor Reflectivity => new(
        ((_packed >> 24) & 0xFF) * (1f / 255f),
        ((_packed >> 16) & 0xFF) * (1f / 255f),
        ((_packed >> 8) & 0xFF) * (1f / 255f));

    /// <summary>How rough the surface is, in [0, 1]: 0 is a mirror, 1 is fully diffuse.</summary>
    public float Roughness => (_packed & 0xFF) * (1f / 255f);

    /// <summary>Reads back a value stored by <see cref="Packed"/>.</summary>
    public static SurfaceReflectance FromPacked(uint packed) => new(packed);

    /// <summary>
    /// The metallic-roughness reading, as <see cref="Rasterization.PbrShader"/> computes it: a
    /// dielectric reflects a flat 4% and keeps its colour in the diffuse term, a metal reflects
    /// its albedo and has no diffuse term at all, and a value in between crosses from one to
    /// the other.
    /// </summary>
    public static SurfaceReflectance FromMetallic(ColorRGB albedo, float metallic, float roughness)
    {
        var m = System.Math.Clamp(metallic, 0f, 1f);
        LinearColor linear = albedo;

        return new SurfaceReflectance(Pack(
            float.Lerp(0.04f, linear.R, m),
            float.Lerp(0.04f, linear.G, m),
            float.Lerp(0.04f, linear.B, m),
            roughness));
    }

    /// <summary>
    /// The Blinn-Phong reading, so the older painters reflect too rather than the feature
    /// belonging to one shading mode.
    ///
    /// <para>
    /// Both numbers are conversions of quantities that were never physical.
    /// <see cref="Material.SpecularStrength"/> becomes F0 through the convention Disney's
    /// model and the engines that followed it use — <c>F0 = 0.08 · specular</c> — which puts
    /// the middle of the range on a real dielectric's four percent. Taken as F0 directly it
    /// would make every surface in the scene a 35% mirror, because 0.35 is this engine's
    /// default and it means "a fairly visible highlight", not "reflects a third of the light".
    /// It stays colourless: Blinn-Phong has no notion of a metal to tint it with.
    /// <see cref="Material.Shininess"/> becomes roughness through the usual inversion of the
    /// Phong lobe's width, which maps the default 32 to a satin 0.24 and a mirror-like 2048 to
    /// 0.03.
    /// </para>
    /// </summary>
    public static SurfaceReflectance FromSpecular(float specularStrength, float shininess)
    {
        var f0 = 0.08f * MathF.Max(specularStrength, 0f);
        var roughness = MathF.Sqrt(2f / (MathF.Max(shininess, 0f) + 2f));

        return new SurfaceReflectance(Pack(f0, f0, f0, roughness));
    }

    /// <summary>What a mesh's material reflects under a painter that does not shade physically.</summary>
    public static SurfaceReflectance FromMaterial(Material? material) =>
        material is null
            ? None
            : FromSpecular(material.SpecularStrength, material.Shininess);

    private static uint Pack(float r, float g, float b, float roughness)
    {
        // A reflection too weak to see costs a ray per pixel to find out. Rounding it to
        // nothing here is what lets the effect skip those pixels with one test.
        if (r < Negligible && g < Negligible && b < Negligible)
        {
            return 0u;
        }

        return ((uint)Quantize(r) << 24)
            | ((uint)Quantize(g) << 16)
            | ((uint)Quantize(b) << 8)
            | Quantize(roughness);
    }

    private static uint Quantize(float value) =>
        (uint)System.Math.Clamp((int)(value * 255f + 0.5f), 0, 255);
}
