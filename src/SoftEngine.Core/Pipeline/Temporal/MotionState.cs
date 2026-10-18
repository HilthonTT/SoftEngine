using SoftEngine.Core.Geometry;
using SoftEngine.Core.Scenes;
using System.Numerics;

namespace SoftEngine.Core.Pipeline.Temporal;

public sealed class MotionState
{
    private Dictionary<IMesh, Matrix4x4> _previous = new(ReferenceEqualityComparer.Instance);
    private Dictionary<IMesh, Matrix4x4> _current = new(ReferenceEqualityComparer.Instance);

    public Matrix4x4 PreviousViewProjection { get; private set; } = Matrix4x4.Identity;

    public bool HasHistory { get; private set; }

    public Matrix4x4 PreviousWorldMatrix(IMesh mesh, in Matrix4x4 current) =>
        _previous.TryGetValue(mesh, out var previous) ? previous : current;

    public void Advance(IWorld world, in Matrix4x4 viewProjection)
    {
        ArgumentNullException.ThrowIfNull(world, nameof(world));

        _current.Clear();

        foreach (var mesh in world.Meshes)
        {
            _current[mesh] = mesh.WorldMatrix;
        }

        (_previous, _current) = (_current, _previous);

        PreviousViewProjection = viewProjection;
        HasHistory = true;
    }

    public void Reset()
    {
        _previous.Clear();
        _current.Clear();

        PreviousViewProjection = Matrix4x4.Identity;
        HasHistory = false;
    }
}
