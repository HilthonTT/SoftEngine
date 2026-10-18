using SoftEngine.Core.Scenes.Cameras;
using System.Numerics;

namespace SoftEngine.Benchmarks;

internal sealed class FixedCamera(Vector3 position, Vector3 target) : ICamera
{
    public Vector3 Position { get; set; } = position;

    public Vector3 Target { get; set; } = target;

    public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Target, Vector3.UnitY);
}
