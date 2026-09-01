using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Projections;
using System.Numerics;

namespace SoftEngine.WinForms.Demos;

internal sealed record WorldSetup(SimpleWorld World, Vector3 CameraPosition, PerspectiveProjection? Projection)
{
    public float SkeletonTickSize { get; init; } = 1f;
}
