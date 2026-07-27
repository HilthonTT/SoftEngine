using System.Numerics;

namespace SoftEngine.Core.Shading;

/// <summary>
/// The environment half of the split-sum approximation: how much of the light arriving from
/// an environment a surface reflects, as a function of viewing angle and roughness alone.
///
/// Lighting a surface from an environment means integrating the incoming light against the
/// material's BRDF over the whole hemisphere, per pixel — which nothing real-time can do.
/// The split-sum approximation factors that integral into two halves that can each be
/// precomputed: the light, prefiltered by roughness (<see cref="PrefilteredEnvironment"/>),
/// times the BRDF integrated against a <em>white</em> environment, which is this.
///
/// The second half depends on nothing but <c>n·v</c> and roughness — not on the environment,
/// not on the scene, not even on the material's colour, because Fresnel's F0 factors out of
/// it into a scale and a bias. Two numbers per (angle, roughness) pair, and the same table
/// serves every material in every scene: <c>reflectance = F0 · scale + bias</c>.
///
/// The table is built once, on first use, by numerically integrating GGX with importance
/// sampling — the same integral <see cref="Ggx"/> describes, evaluated ahead of time instead
/// of per pixel.
/// </summary>
public static class BrdfLut
{
    /// <summary>Samples per axis. The surface is smooth in both, so a small table interpolates cleanly.</summary>
    public const int Resolution = 32;

    private const int SampleCount = 256;

    // (scale, bias) per texel, row-major with n·v across and roughness down.
    private static readonly Lazy<Vector2[]> _table = new(Build, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The raw table, built on first access.</summary>
    public static Vector2[] Table => _table.Value;

    /// <summary>
    /// The scale and bias to apply to a surface's F0, bilinearly interpolated. Their sum is
    /// the fraction of a white environment a surface with F0 = 1 reflects, which is at most
    /// 1 and approaches it as the surface smooths.
    /// </summary>
    public static Vector2 Sample(float nDotV, float roughness)
    {
        var table = _table.Value;

        // Texel centres sit at (i + 0.5) / Resolution, so shift by half a texel before
        // splitting into an index and a blend fraction.
        var fx = System.Math.Clamp(nDotV, 0f, 1f) * Resolution - 0.5f;
        var fy = System.Math.Clamp(roughness, 0f, 1f) * Resolution - 0.5f;

        var x0 = (int)MathF.Floor(fx);
        var y0 = (int)MathF.Floor(fy);

        var tx = fx - x0;
        var ty = fy - y0;

        // Clamp rather than wrap: the far edge of the table is grazing incidence and full
        // roughness, and neither continues round to the other side of it.
        var xa = System.Math.Clamp(x0, 0, Resolution - 1);
        var xb = System.Math.Clamp(x0 + 1, 0, Resolution - 1);
        var ya = System.Math.Clamp(y0, 0, Resolution - 1) * Resolution;
        var yb = System.Math.Clamp(y0 + 1, 0, Resolution - 1) * Resolution;

        var top = Vector2.Lerp(table[xa + ya], table[xb + ya], tx);
        var bottom = Vector2.Lerp(table[xa + yb], table[xb + yb], tx);

        return Vector2.Lerp(top, bottom, ty);
    }

    /// <summary>
    /// Integrates the specular BRDF against a white environment for one (angle, roughness)
    /// pair. Splitting Fresnel's <c>F0 + (1 - F0)(1 - v·h)^5</c> into the part that scales
    /// F0 and the part that does not is what makes the result independent of the material.
    /// </summary>
    public static Vector2 Integrate(float nDotV, float roughness)
    {
        // At exactly zero the view direction lies in the surface and the frame degenerates.
        nDotV = System.Math.Clamp(nDotV, 1e-3f, 1f);

        var alpha = Ggx.Alpha(roughness);

        // A view direction in the tangent frame: only its angle to the normal matters, so it
        // can be placed in the XZ plane and the frame's rotation about Z ignored.
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

            // The sample was drawn from D, so D cancels out of the estimator and what
            // remains is the geometry term reweighted by the sampling density.
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
