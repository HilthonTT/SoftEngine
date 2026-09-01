using SoftEngine.Core.Rasterization.Varyings;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Rasterization;

public readonly struct Edge<TVarying>(Vector3 a, Vector3 b, TVarying va, TVarying vb, float wa, float wb)
    where TVarying : struct, IVarying<TVarying>
{
    public readonly Vector3 A = a;
    public readonly Vector3 B = b;
    public readonly TVarying VA = va;
    public readonly TVarying VB = vb;

    public readonly float WA = wa;
    public readonly float WB = wb;

    public float InvHeight
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => A.Y == B.Y ? 1f : 1f / (B.Y - A.Y);
    }
}
