using SoftEngine.Core.Geometry;
using SoftEngine.Core.Math;
using System.Numerics;

namespace SoftEngine.Core.Editing;

public readonly record struct TransformState(Vector3 Position, Vector3 Scale, float Pitch, float Yaw, float Roll)
{
    public static TransformState Of(IMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh, nameof(mesh));

        return new TransformState(
            mesh.Position,
            mesh.Scale,
            mesh.Rotation.XPitch,
            mesh.Rotation.YYaw,
            mesh.Rotation.ZRoll);
    }

    public void ApplyTo(IMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh, nameof(mesh));

        mesh.Position = Position;
        mesh.Scale = Scale;

        mesh.Rotation = new Rotation3D(Pitch, Yaw, Roll);
    }
}
