using System.Numerics;

namespace SoftEngine.Core.Scenes.Projections;

public interface IProjection
{
    float ZNear { get; }

    float ZFar { get; }

    bool IsOrthographic => false;

    Matrix4x4 ProjectionMatrix(float w, float h);
}
