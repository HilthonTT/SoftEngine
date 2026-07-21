using System.Numerics;

namespace SoftEngine.Core.Scenes.Projections;

public interface IProjection
{
    /// <summary>Distance to the near clip plane.</summary>
    float ZNear { get; }

    /// <summary>Distance to the far clip plane.</summary>
    float ZFar { get; }

    Matrix4x4 ProjectionMatrix(float w, float h);
}
