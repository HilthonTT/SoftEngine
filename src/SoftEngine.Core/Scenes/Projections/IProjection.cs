using System.Numerics;

namespace SoftEngine.Core.Scenes.Projections;

public interface IProjection
{
    /// <summary>Distance to the near clip plane.</summary>
    float ZNear { get; }

    /// <summary>Distance to the far clip plane.</summary>
    float ZFar { get; }

    /// <summary>
    /// Whether the projection is parallel, so clip-space w carries no depth. Perspective
    /// projections leave this false and the framebuffer derives device depth from w;
    /// orthographic ones set it and the projected z is already the device depth.
    /// </summary>
    bool IsOrthographic => false;

    Matrix4x4 ProjectionMatrix(float w, float h);
}
