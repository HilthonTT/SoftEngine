using System.Numerics;

namespace SoftEngine.Core.Scenes.Projections;

public sealed class PerspectiveProjection(float fov, float zNear, float zFar) : IProjection
{
    public float ZNear { get; } = zNear;

    public float ZFar { get; } = zFar;

    public Matrix4x4 ProjectionMatrix(float width, float height) =>
        Matrix4x4.CreatePerspectiveFieldOfView(fov, width / height, ZNear, ZFar);
}
