using SoftEngine.Core.Editing;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Geometry.Primitives;
using SoftEngine.Core.Gizmos;
using SoftEngine.WinForms.Cameras;
using System.Numerics;

namespace SoftEngine.WinForms;

public sealed partial class MainScreen
{
    private static readonly (PrimitiveShape Shape, string Label)[] AddablePrimitives =
    [
        (PrimitiveShape.Plane, "&Plane"),
        (PrimitiveShape.Box, "&Cube"),
        (PrimitiveShape.UvSphere, "&UV sphere"),
        (PrimitiveShape.IcoSphere, "&Ico sphere"),
        (PrimitiveShape.Cylinder, "C&ylinder"),
        (PrimitiveShape.Cone, "Co&ne"),
        (PrimitiveShape.Torus, "&Torus"),
    ];

    private const float AddedPrimitiveSize = 0.08f;

    private ContextMenuStrip? _addMenu;

    private readonly ModalTransform _transform = new();

    private void InitializePrimitives()
    {
        _addMenu = BuildAddMenu();
        mnuAdd.DropDownItems.AddRange(BuildAddItems());

        panel3D1.AddRequested += (s, at) => _addMenu?.Show(at);

        _transform.Snap = _gizmo.Snap;
        panel3D1.Transform = _transform;

        panel3D1.DeleteRequested += (s, mesh) => DeleteMesh(mesh);

        mnuDelete.Click += (s, e) =>
        {
            if (panel3D1.Picked?.Mesh is { } mesh)
            {
                DeleteMesh(mesh);
            }
        };
    }

    private void DeleteMesh(IMesh mesh)
    {
        if (panel3D1.Scene?.World is not { } world)
        {
            return;
        }

        var edit = MeshListEdit.Remove(world, mesh);

        if (edit is null)
        {
            return;
        }

        _history.Push(edit);

        panel3D1.ClearPick();

        panel3D1.ResetTemporalHistory();
        panel3D1.Invalidate();

        UpdateStatus();
    }

    private ContextMenuStrip BuildAddMenu()
    {
        var menu = new ContextMenuStrip(components)
        {
            BackColor = Theme.Surface,
            ForeColor = Theme.TextPrimary,
            ShowImageMargin = false,
        };

        menu.Items.AddRange(BuildAddItems());

        return menu;
    }

    private ToolStripItem[] BuildAddItems() =>
    [
        .. AddablePrimitives.Select(entry =>
        {
            var item = new ToolStripMenuItem(entry.Label);
            item.Click += (s, e) => AddPrimitive(entry.Shape);

            return (ToolStripItem)item;
        }),
    ];

    private void AddPrimitive(PrimitiveShape shape)
    {
        if (panel3D1.Scene?.World is not { } world)
        {
            return;
        }

        var mesh = PrimitiveFactory.Create(shape, AddedPrimitiveSize * MathF.Max(panel3D1.ReferenceDistance, 0.0001f));
        mesh.Position = AddPosition();

        _history.Push(MeshListEdit.Add(world, mesh, world.Meshes.Count));

        panel3D1.SelectMesh(mesh);

        panel3D1.ResetTemporalHistory();
        panel3D1.Invalidate();

        UpdateStatus();
    }

    private Vector3 AddPosition() =>
        panel3D1.Scene?.Camera is ArcBallCamera arcBall ? arcBall.Pivot : Vector3.Zero;
}
