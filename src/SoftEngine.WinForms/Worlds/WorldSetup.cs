using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Projections;
using System.Numerics;

namespace SoftEngine.WinForms.Worlds;

internal sealed record WorldSetup(SimpleWorld World, Vector3 CameraPosition, PerspectiveProjection? Projection)
{
    /// <summary>
    /// Length of a joint's axis tick in the skeleton gizmo. Worlds are authored anywhere
    /// from 2 to 1500 units across, and one fixed size is either invisible on the large
    /// ones or swamps the small ones.
    /// </summary>
    public float SkeletonTickSize { get; init; } = 1f;
}
