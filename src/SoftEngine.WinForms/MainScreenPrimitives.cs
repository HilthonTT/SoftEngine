using SoftEngine.Core.Editing;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Geometry.Primitives;
using SoftEngine.Core.Gizmos;
using SoftEngine.WinForms.Cameras;
using System.Numerics;

namespace SoftEngine.WinForms;

/// <summary>
/// Building a scene rather than looking at one: Shift+A over the viewport offers the generated
/// primitives, and the chosen one lands where the view is centred, sized to the world it is
/// joining, selected, and on the undo stack.
///
/// <para>
/// Named <c>MainScreenPrimitives.cs</c> rather than <c>MainScreen.Primitives.cs</c> for the reason
/// spelled out in <c>MainScreenWorkspace.cs</c>: a dotted partial of a <see cref="Form"/> invites
/// Visual Studio to generate a <c>.resx</c> whose resource name collides with the form's own.
/// </para>
/// </summary>
public sealed partial class MainScreen
{
    /// <summary>
    /// What the add menu offers, in Blender's order, with Blender's names — the labels are what
    /// somebody arriving from there will look for, even where this engine's class is called
    /// something else (a <see cref="Box"/> is what everyone means by "cube").
    /// </summary>
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

    /// <summary>
    /// How much of the framing distance a new primitive spans from its centre. Chosen against the
    /// 40° field of view every world is rendered with: what fills the frame at that distance has a
    /// radius of about 0.34 of it, so this is roughly a quarter of the height of the view — big
    /// enough to be unmistakably there, small enough not to swallow what is already in the scene.
    /// </summary>
    private const float AddedPrimitiveSize = 0.08f;

    private ContextMenuStrip? _addMenu;

    /// <summary>
    /// The keyboard-driven move and scale. It shares the gizmo's snap increments deliberately:
    /// Ctrl+G is a statement about the scene being built, not about which of the two tools is
    /// building it, and a grid that applied to dragged handles but not to G would be a bug
    /// nobody could describe.
    /// </summary>
    private readonly ModalTransform _transform = new();

    private void InitializePrimitives()
    {
        // One list, two ways in. The chord is the one that will actually get used, and the menu
        // is the only reason anybody would find out the chord exists.
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

    /// <summary>
    /// Takes a mesh out of the world, undoably. Nothing is destroyed — the command holds the mesh
    /// itself — so undo puts back the object that was there rather than a rebuilt copy of it.
    /// </summary>
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

        // Before anything else looks at the world: the pick addresses meshes by their position in
        // the list, and the mesh at that position is now a different one.
        panel3D1.ClearPick();

        panel3D1.ResetTemporalHistory();
        panel3D1.Invalidate();

        UpdateStatus();
    }

    private ContextMenuStrip BuildAddMenu()
    {
        // Owned by the form's container: it is not in the control tree, so nothing else would
        // ever dispose it.
        var menu = new ContextMenuStrip(components)
        {
            BackColor = Theme.Surface,
            ForeColor = Theme.TextPrimary,
            ShowImageMargin = false,
        };

        menu.Items.AddRange(BuildAddItems());

        return menu;
    }

    /// <summary>
    /// A fresh set of items each call: a <see cref="ToolStripItem"/> belongs to one owner, so the
    /// menu bar and the context menu cannot share them however alike they look.
    /// </summary>
    private ToolStripItem[] BuildAddItems() =>
    [
        .. AddablePrimitives.Select(entry =>
        {
            var item = new ToolStripMenuItem(entry.Label);
            item.Click += (s, e) => AddPrimitive(entry.Shape);

            return (ToolStripItem)item;
        }),
    ];

    /// <summary>
    /// Puts one of the generated primitives into the world, at the centre of the view and scaled
    /// to it.
    /// </summary>
    private void AddPrimitive(PrimitiveShape shape)
    {
        if (panel3D1.Scene?.World is not { } world)
        {
            return;
        }

        var mesh = PrimitiveFactory.Create(shape, AddedPrimitiveSize * MathF.Max(panel3D1.ReferenceDistance, 0.0001f));
        mesh.Position = AddPosition();

        // Appended, and the index recorded by the command, so undoing and redoing the add puts
        // the mesh back where it was in a list that may have grown since.
        _history.Push(MeshListEdit.Add(world, mesh, world.Meshes.Count));

        // The object you have just added is the object you want to move, so the add and the
        // selection are one gesture — which is the whole point of adding at the view centre
        // rather than at an origin you would then have to go and find.
        panel3D1.SelectMesh(mesh);

        // New geometry changes the picture without the camera or anything in it having moved,
        // which is precisely the case a temporal pass would otherwise blend across.
        panel3D1.ResetTemporalHistory();
        panel3D1.Invalidate();

        UpdateStatus();
    }

    /// <summary>
    /// Where an added primitive goes: the point the view is centred on, which is the closest
    /// thing this viewer has to Blender's 3D cursor. The world origin is the fallback for a
    /// camera that cannot say — and is where a freshly loaded world is looking anyway.
    /// </summary>
    private Vector3 AddPosition() =>
        panel3D1.Scene?.Camera is ArcBallCamera arcBall ? arcBall.Pivot : Vector3.Zero;
}
