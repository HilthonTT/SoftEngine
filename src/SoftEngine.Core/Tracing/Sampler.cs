using System.Numerics;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Tracing;

/// <summary>
/// The random numbers a path is built from: a counter-based generator seeded by which pixel and
/// which sample it belongs to.
///
/// <para>
/// A shared generator would be wrong twice over. It would be a lock or a false-sharing hotspot in a
/// renderer whose whole point is that every pixel is independent, and — worse — it would make the
/// image depend on the order the rows happened to be scheduled in, so two runs of the same scene
/// would differ. Seeding from the pixel's own coordinates instead makes each pixel's sequence a
/// pure function of where it is, which is both parallel by construction and reproducible.
/// </para>
///
/// <para>
/// The mixing is PCG's output permutation over a 64-bit linear congruential state — cheap, and
/// without the correlation between nearby seeds that a plain LCG would show, which matters here
/// because adjacent pixels <em>are</em> nearby seeds.
/// </para>
/// </summary>
internal struct Sampler
{
    private ulong _state;

    public Sampler(uint seed, int pixel, int sample)
    {
        // Fold the three identifiers into one state. The odd multipliers are the usual mixing
        // constants; what matters is only that (pixel, sample) pairs land far apart.
        _state = Mix(seed ^ 0x853C49E6748FEA9BUL, ((ulong)(uint)pixel << 32) | (uint)sample);
    }

    /// <summary>A float in [0, 1).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Next()
    {
        // 24 bits is exactly the float's mantissa: every value is representable and evenly spaced,
        // where dividing a full 32-bit integer would round unevenly at the top of the range.
        return (NextUInt() >> 8) * (1f / (1 << 24));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector2 NextPair() => new(Next(), Next());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint NextUInt()
    {
        _state = _state * 6364136223846793005UL + 1442695040888963407UL;

        var xorshifted = (uint)(((_state >> 18) ^ _state) >> 27);
        var rotation = (int)(_state >> 59);

        return (xorshifted >> rotation) | (xorshifted << ((-rotation) & 31));
    }

    private static ulong Mix(ulong a, ulong b)
    {
        var x = a ^ (b + 0x9E3779B97F4A7C15UL + (a << 6) + (a >> 2));

        x ^= x >> 33;
        x *= 0xFF51AFD7ED558CCDUL;
        x ^= x >> 33;
        x *= 0xC4CEB9FE1A85EC53UL;
        x ^= x >> 33;

        return x;
    }

    /// <summary>
    /// A direction drawn from the cosine-weighted hemisphere around <paramref name="normal"/> —
    /// the distribution a Lambertian surface reflects in, so the weight it would need cancels
    /// against the probability of having been chosen and the estimator is just the albedo.
    ///
    /// Malley's method: a uniform point on the disc, lifted onto the hemisphere. Projecting a
    /// uniform disc up gives exactly the cosine density, with no trigonometry beyond the one angle.
    /// </summary>
    public Vector3 NextCosineDirection(Vector3 normal)
    {
        var radius = MathF.Sqrt(Next());
        var angle = MathF.Tau * Next();

        var x = radius * MathF.Cos(angle);
        var y = radius * MathF.Sin(angle);
        var z = MathF.Sqrt(MathF.Max(0f, 1f - radius * radius));

        var (tangent, bitangent) = Shading.Ggx.BasisAround(normal);

        return Vector3.Normalize(tangent * x + bitangent * y + normal * z);
    }
}
