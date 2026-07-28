using SoftEngine.Core.Geometry;

namespace SoftEngine.Core.Editing;

/// <summary>
/// A change to one mesh's local transform — what a completed gizmo drag amounts to.
/// </summary>
/// <remarks>
/// The whole transform is recorded on both sides even when only one of the three was dragged.
/// Storing just the component that moved would be smaller and would be wrong the moment two
/// edits interleave: undoing a rotation that only knows about its own axis would leave a
/// translation that happened in between applied to the mesh but absent from the history's model
/// of it. A transform is small, and restoring all of it is the only version with no ordering
/// rule to get wrong.
/// </remarks>
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

        // A mesh has no name to use, so its type is the most specific thing there is to say.
        Description = $"{verb} {mesh.GetType().Name}";
    }

    public string Description { get; }

    /// <summary>The mesh this edit moves, so a caller can re-select it when stepping through history.</summary>
    public IMesh Mesh => _mesh;

    /// <summary>
    /// The edit from <paramref name="before"/> to the mesh's current transform, or null when
    /// the two are the same.
    ///
    /// <para>
    /// The null case is what keeps the history usable. A click that grabs a handle and releases
    /// it without moving is a drag as far as the gizmo is concerned, and recording it would put
    /// an entry on the stack that undoes nothing — so the first Ctrl+Z after a misclick would
    /// appear to do nothing at all, and the user would press it again and lose real work.
    /// </para>
    /// </summary>
    public static TransformEdit? Between(IMesh mesh, TransformState before, string verb)
    {
        ArgumentNullException.ThrowIfNull(mesh, nameof(mesh));

        var after = TransformState.Of(mesh);

        return after == before ? null : new TransformEdit(mesh, before, after, verb);
    }

    public void Apply() => _after.ApplyTo(_mesh);

    public void Revert() => _before.ApplyTo(_mesh);
}
