using System.Numerics;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Tracing;

internal struct Sampler
{
    private ulong _state;

    public Sampler(uint seed, int pixel, int sample)
    {
        _state = Mix(seed ^ 0x853C49E6748FEA9BUL, ((ulong)(uint)pixel << 32) | (uint)sample);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Next()
    {
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
