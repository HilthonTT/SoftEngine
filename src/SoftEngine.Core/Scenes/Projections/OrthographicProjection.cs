using System.Numerics;

namespace SoftEngine.Core.Scenes.Projections;

/// <summary>
/// A parallel projection: the view volume is a box rather than a frustum, so geometry
/// keeps its size however far away it is. <paramref name="viewHeight"/> is the vertical
/// extent of that box in world units; the width follows the viewport's aspect ratio,
/// exactly as the field of view does for <see cref="PerspectiveProjection"/>.
///
/// Clip-space w is 1 everywhere here, which is why <see cref="IsOrthographic"/> is set:
/// the framebuffer's usual depth mapping is a function of w and would collapse to a
/// constant, so it switches to reading the projected z directly.
/// </summary>
public sealed class OrthographicProjection(float viewHeight, float zNear, float zFar) : IProjection
{
    /// <summary>Vertical extent of the view volume, in world units.</summary>
    public float ViewHeight { get; } = viewHeight;

    public float ZNear { get; } = zNear;

    public float ZFar { get; } = zFar;

    public bool IsOrthographic => true;

    public Matrix4x4 ProjectionMatrix(float width, float height) =>
        Matrix4x4.CreateOrthographic(ViewHeight * (width / height), ViewHeight, ZNear, ZFar);
}
