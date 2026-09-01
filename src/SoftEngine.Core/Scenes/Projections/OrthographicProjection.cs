using System.Numerics;

namespace SoftEngine.Core.Scenes.Projections;

public sealed class OrthographicProjection(float viewHeight, float zNear, float zFar) : IProjection
{
    public float ViewHeight { get; } = viewHeight;

    public float ZNear { get; } = zNear;

    public float ZFar { get; } = zFar;

    public bool IsOrthographic => true;

    public Matrix4x4 ProjectionMatrix(float width, float height) =>
        Matrix4x4.CreateOrthographic(ViewHeight * (width / height), ViewHeight, ZNear, ZFar);
}
