using SoftEngine.Core.Geometry;
using SoftEngine.Core.Math;
using System.Numerics;

namespace SoftEngine.Core.Editing;

/// <summary>
/// A mesh's whole local transform, copied out by value.
///
/// <para>
/// The Euler angles are stored as three floats rather than as the <see cref="Rotation3D"/> the
/// mesh carries, and that is the entire point of the type. <see cref="Rotation3D"/> is a mutable
/// class: holding a reference to a mesh's own instance and calling it a snapshot records nothing
/// at all, because the next drag mutates the object the snapshot is pointing at. A value copy
/// cannot be aliased, so a state captured before a change still describes it afterwards.
/// </para>
/// </summary>
public readonly record struct TransformState(Vector3 Position, Vector3 Scale, float Pitch, float Yaw, float Roll)
{
    /// <summary>Reads the transform a mesh currently has.</summary>
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

    /// <summary>Writes this transform back onto a mesh.</summary>
    public void ApplyTo(IMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh, nameof(mesh));

        mesh.Position = Position;
        mesh.Scale = Scale;

        // A fresh instance rather than mutating the mesh's own: two meshes that were handed the
        // same Rotation3D would otherwise turn together.
        mesh.Rotation = new Rotation3D(Pitch, Yaw, Roll);
    }
}
