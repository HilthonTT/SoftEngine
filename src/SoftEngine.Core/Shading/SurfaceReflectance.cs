using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;

namespace SoftEngine.Core.Shading;

public readonly struct SurfaceReflectance
{
    private const float Negligible = 1f / 512f;

    private readonly uint _packed;

    private SurfaceReflectance(uint packed) => _packed = packed;

    public uint Packed => _packed;

    public static SurfaceReflectance None => default;

    public bool IsReflective => (_packed & 0xFFFFFF00u) != 0;

    public LinearColor Reflectivity => new(
        ((_packed >> 24) & 0xFF) * (1f / 255f),
        ((_packed >> 16) & 0xFF) * (1f / 255f),
        ((_packed >> 8) & 0xFF) * (1f / 255f));

    public float Roughness => (_packed & 0xFF) * (1f / 255f);

    public static SurfaceReflectance FromPacked(uint packed) => new(packed);

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

    public static SurfaceReflectance FromSpecular(float specularStrength, float shininess)
    {
        var f0 = 0.08f * MathF.Max(specularStrength, 0f);
        var roughness = MathF.Sqrt(2f / (MathF.Max(shininess, 0f) + 2f));

        return new SurfaceReflectance(Pack(f0, f0, f0, roughness));
    }

    public static SurfaceReflectance FromMaterial(Material? material) =>
        material is null
            ? None
            : FromSpecular(material.SpecularStrength, material.Shininess);

    private static uint Pack(float r, float g, float b, float roughness)
    {
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
