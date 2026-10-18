using SoftEngine.Core.Scenes.Cameras;
using System.Numerics;

namespace SoftEngine.Cli.Rendering;

internal sealed class OrbitCamera : ICamera
{
    public Vector3 Target { get; set; }

    public Vector3 Position { get; set; }

    public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Target, Up());

    public void Orbit(float yaw, float pitch, float distance)
    {
        var cosPitch = MathF.Cos(pitch);

        var direction = new Vector3(
            MathF.Sin(yaw) * cosPitch,
            MathF.Sin(pitch),
            -MathF.Cos(yaw) * cosPitch);

        Position = Target - direction * distance;
    }

    private Vector3 Up()
    {
        var forward = Target - Position;

        if (forward.LengthSquared() < 1e-12f)
        {
            return Vector3.UnitY;
        }

        forward = Vector3.Normalize(forward);

        return MathF.Abs(forward.Y) > 0.9995f
            ? new Vector3(0f, 0f, forward.Y > 0f ? -1f : 1f)
            : Vector3.UnitY;
    }
}
