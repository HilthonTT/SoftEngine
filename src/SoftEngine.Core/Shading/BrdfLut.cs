using System.Numerics;

namespace SoftEngine.Core.Shading;

public static class BrdfLut
{
    public const int Resolution = 32;

    private const int SampleCount = 256;

    private static readonly Lazy<Vector2[]> _table = new(Build, LazyThreadSafetyMode.ExecutionAndPublication);

    public static Vector2[] Table => _table.Value;

    public static Vector2 Sample(float nDotV, float roughness)
    {
        var table = _table.Value;

        var fx = System.Math.Clamp(nDotV, 0f, 1f) * Resolution - 0.5f;
        var fy = System.Math.Clamp(roughness, 0f, 1f) * Resolution - 0.5f;

        var x0 = (int)MathF.Floor(fx);
        var y0 = (int)MathF.Floor(fy);

        var tx = fx - x0;
        var ty = fy - y0;

        var xa = System.Math.Clamp(x0, 0, Resolution - 1);
        var xb = System.Math.Clamp(x0 + 1, 0, Resolution - 1);
        var ya = System.Math.Clamp(y0, 0, Resolution - 1) * Resolution;
        var yb = System.Math.Clamp(y0 + 1, 0, Resolution - 1) * Resolution;

        var top = Vector2.Lerp(table[xa + ya], table[xb + ya], tx);
        var bottom = Vector2.Lerp(table[xa + yb], table[xb + yb], tx);

        return Vector2.Lerp(top, bottom, ty);
    }

    public static Vector2 Integrate(float nDotV, float roughness)
    {
        nDotV = System.Math.Clamp(nDotV, 1e-3f, 1f);

        var alpha = Ggx.Alpha(roughness);

        var view = new Vector3(MathF.Sqrt(1f - nDotV * nDotV), 0f, nDotV);

        var scale = 0f;
        var bias = 0f;

        for (var i = 0; i < SampleCount; i++)
        {
            var half = Ggx.ImportanceSampleHalfVector(Ggx.Hammersley(i, SampleCount), alpha);

            var vDotH = Vector3.Dot(view, half);
            var light = 2f * vDotH * half - view;

            var nDotL = light.Z;
            if (nDotL <= 0f)
            {
                continue;
            }

            var nDotH = MathF.Max(half.Z, 0f);
            vDotH = MathF.Max(vDotH, 0f);

            var visibility = Ggx.Visibility(nDotV, nDotL, alpha);
            var weight = 4f * visibility * nDotL * vDotH / MathF.Max(nDotH, 1e-9f);

            var fresnel = Ggx.FresnelWeight(vDotH);

            scale += (1f - fresnel) * weight;
            bias += fresnel * weight;
        }

        return new Vector2(scale / SampleCount, bias / SampleCount);
    }

    private static Vector2[] Build()
    {
        var table = new Vector2[Resolution * Resolution];

        for (var y = 0; y < Resolution; y++)
        {
            var roughness = (y + 0.5f) / Resolution;

            for (var x = 0; x < Resolution; x++)
            {
                table[x + y * Resolution] = Integrate((x + 0.5f) / Resolution, roughness);
            }
        }

        return table;
    }
}
