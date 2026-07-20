using System.Numerics;

namespace SoftEngine.Core.Scenes.Cameras;

public interface ICamera
{
    Matrix4x4 ViewMatrix { get; }

    Vector3 Position { get; }
}
