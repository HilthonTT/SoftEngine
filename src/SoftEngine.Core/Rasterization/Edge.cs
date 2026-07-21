using System.Numerics;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Rasterization;

/// <summary>
/// One edge of a half-triangle: screen-space endpoints plus their varyings.
/// A is always the upper (smaller Y) endpoint.
/// </summary>
public readonly struct Edge<TVarying>(Vector3 a, Vector3 b, TVarying va, TVarying vb)
    where TVarying : struct, IVarying<TVarying>
{
    public readonly Vector3 A = a;
    public readonly Vector3 B = b;
    public readonly TVarying VA = va;
    public readonly TVarying VB = vb;

    /// <summary>1 / height, or 1 for a horizontal edge (gradient is then clamped to 0..1 anyway).</summary>
    public float InvHeight
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => A.Y == B.Y ? 1f : 1f / (B.Y - A.Y);
    }
}
