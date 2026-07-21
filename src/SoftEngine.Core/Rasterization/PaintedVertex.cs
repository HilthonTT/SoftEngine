using System.Numerics;

namespace SoftEngine.Core.Rasterization;

public readonly struct PaintedVertex(Vector3 norm, Vector3 screenProj, Vector3 world)
{
    public Vector3 Norm { get; init; } = norm;

    public Vector3 ScreenProj { get; init; } = screenProj;

    public Vector3 World { get; init; } = world;
}
