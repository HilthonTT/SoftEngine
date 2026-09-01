using SoftEngine.Core.Geometry;

namespace SoftEngine.Core.Editing;

public sealed class TransformEdit : IEditCommand
{
    private readonly IMesh _mesh;
    private readonly TransformState _before;
    private readonly TransformState _after;

    public TransformEdit(IMesh mesh, TransformState before, TransformState after, string verb)
    {
        ArgumentNullException.ThrowIfNull(mesh, nameof(mesh));

        _mesh = mesh;
        _before = before;
        _after = after;

        Description = $"{verb} {mesh.GetType().Name}";
    }

    public string Description { get; }

    public IMesh Mesh => _mesh;

    public static TransformEdit? Between(IMesh mesh, TransformState before, string verb)
    {
        ArgumentNullException.ThrowIfNull(mesh, nameof(mesh));

        var after = TransformState.Of(mesh);

        return after == before ? null : new TransformEdit(mesh, before, after, verb);
    }

    public void Apply() => _after.ApplyTo(_mesh);

    public void Revert() => _before.ApplyTo(_mesh);
}
