using SoftEngine.Core.Geometry;
using SoftEngine.Core.Scenes;

namespace SoftEngine.Core.Editing;

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

    public IMesh Mesh => _mesh;

    public int Index => _index;

    public bool IsAdding => _adding;

    public static MeshListEdit Add(IWorld world, IMesh mesh, int index, string description = "Add")
    {
        ArgumentNullException.ThrowIfNull(world, nameof(world));
        ArgumentNullException.ThrowIfNull(mesh, nameof(mesh));

        var at = System.Math.Clamp(index, 0, world.Meshes.Count);
        var edit = new MeshListEdit(world, mesh, at, adding: true, $"{description} {mesh.GetType().Name}");

        edit.Apply();

        return edit;
    }

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
