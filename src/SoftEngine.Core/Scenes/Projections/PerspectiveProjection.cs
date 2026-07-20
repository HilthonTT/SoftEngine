using System.Numerics;

namespace SoftEngine.Core.Scenes.Projections;

public sealed class PerspectiveProjection(float fov, float zNeaer, float zFar) : IProjection
{
    public Matrix4x4 ProjectionMatrix(float width, float height) =>
        Matrix4x4.CreatePerspectiveFieldOfView(fov, width / height, zNeaer, zFar);
}
