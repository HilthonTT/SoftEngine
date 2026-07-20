using System.Numerics;

namespace SoftEngine.Core.Scenes.Projections;

public interface IProjection
{
    Matrix4x4 ProjectionMatrix(float w, float h);
}
