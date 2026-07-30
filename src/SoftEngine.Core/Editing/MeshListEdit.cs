using SoftEngine.Core.Geometry;
using SoftEngine.Core.Scenes;

namespace SoftEngine.Core.Editing;

/// <summary>
/// A mesh added to or removed from the world, undoably.
///
/// <para>
/// The mesh itself is kept alive by the command, which is the whole trick to making a deletion
/// reversible: nothing has to be rebuilt or re-imported to undo one, because the object was never
/// destroyed — only unlisted. The cost is that a deleted mesh's geometry stays in memory until the
/// history forgets the command, which is exactly the trade every undo stack makes.
/// </para>
///
/// <para>
/// Position in the list matters and is restored. A scene document addresses meshes by index, and so
/// does <see cref="Diagnostics.SceneObjectIds"/> — so putting a mesh back at the end of the list
/// instead of where it was would silently renumber everything after it, and the debugger's
/// <c>obj:7</c> would start meaning something else.
/// </para>
/// </summary>
public sealed class MeshListEdit : IEditCommand
{
    private readonly IWorld _world;
    private readonly IMesh _mesh;
    private readonly int _index;
    private readonly bool _adding;

    private MeshListEdit(IWorld world, IMesh mesh, int index, bool adding, string description)
    {
        _world = world;
        _mesh = mesh;
        _index = index;
        _adding = adding;

        Description = description;
    }

    public string Description { get; }

    /// <summary>The mesh this edit adds or removes, so a caller can select or deselect it.</summary>
    public IMesh Mesh => _mesh;

    /// <summary>Where in the world's list it sits.</summary>
    public int Index => _index;

    /// <summary>Whether this command's <see cref="Apply"/> puts the mesh in rather than taking it out.</summary>
    public bool IsAdding => _adding;

    /// <summary>
    /// Inserts <paramref name="mesh"/> at <paramref name="index"/>, or appends it when the index is
    /// past the end. Applied immediately, so the caller has the mesh in the world by the time this
    /// returns.
    /// </summary>
    public static MeshListEdit Add(IWorld world, IMesh mesh, int index, string description = "Add")
    {
        ArgumentNullException.ThrowIfNull(world, nameof(world));
        ArgumentNullException.ThrowIfNull(mesh, nameof(mesh));

        var at = System.Math.Clamp(index, 0, world.Meshes.Count);
        var edit = new MeshListEdit(world, mesh, at, adding: true, $"{description} {mesh.GetType().Name}");

        edit.Apply();

        return edit;
    }

    /// <summary>
    /// Removes a mesh, or returns null when it is not in the world — which is not an error worth
    /// throwing over: a delete of something already deleted is a no-op, and the history is better off
    /// with nothing on it than with a command that removes nothing.
    /// </summary>
    public static MeshListEdit? Remove(IWorld world, IMesh mesh, string description = "Delete")
    {
        ArgumentNullException.ThrowIfNull(world, nameof(world));
        ArgumentNullException.ThrowIfNull(mesh, nameof(mesh));

        var index = world.Meshes.IndexOf(mesh);

        if (index < 0)
        {
            return null;
        }

        var edit = new MeshListEdit(world, mesh, index, adding: false, $"{description} {mesh.GetType().Name}");

        edit.Apply();

        return edit;
    }

    public void Apply()
    {
        if (_adding)
        {
            Insert();
        }
        else
        {
            _world.Meshes.Remove(_mesh);
        }
    }

    public void Revert()
    {
        if (_adding)
        {
            _world.Meshes.Remove(_mesh);
        }
        else
        {
            Insert();
        }
    }

    private void Insert()
    {
        if (_world.Meshes.Contains(_mesh))
        {
            return;
        }

        _world.Meshes.Insert(System.Math.Clamp(_index, 0, _world.Meshes.Count), _mesh);
    }
}
