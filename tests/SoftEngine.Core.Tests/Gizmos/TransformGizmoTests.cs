using SoftEngine.Core.Buffers;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Geometry.Primitives;
using SoftEngine.Core.Gizmos;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Cameras;
using SoftEngine.Core.Scenes.Graph;
using SoftEngine.Core.Scenes.Projections;
using System.Numerics;

namespace SoftEngine.Core.Tests.Gizmos;

public class TransformGizmoTests
{
    private const int Size = 256;
    private const int Center = Size / 2;

    private sealed class FixedCamera(Vector3 position) : ICamera
    {
        public Vector3 Position { get; set; } = position;

        public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Vector3.Zero, Vector3.UnitY);
    }

    private static (Scene Scene, TransformGizmo Gizmo, Cube Cube) Setup(GizmoMode mode)
    {
        var cube = new Cube();

        var scene = new Scene
        {
            Surface = new FrameBuffer(Size, Size),
            Camera = new FixedCamera(new Vector3(0, 0, 8f)),
            Projection = new PerspectiveProjection(MathF.PI / 4f, 0.1f, 100f),
            World = new SimpleWorld { Meshes = [cube], Lights = [] },
        };

        return (scene, new TransformGizmo { Mode = mode, Target = cube }, cube);
    }

    private static (int X, int Y) Project(Scene scene, Vector3 world)
    {
        var matrix = scene.Camera.ViewMatrix * scene.Projection.ProjectionMatrix(Size, Size);
        var clip = Vector4.Transform(world, matrix);

        var screen = scene.Surface.ToScreen3(clip);

        return ((int)screen.X, (int)screen.Y);
    }

    private static (int X, int Y) HandleTip(Scene scene, TransformGizmo gizmo, GizmoAxis axis, float along = 0.7f)
    {
        var origin = gizmo.Origin;
        var scale = TransformGizmo.HandleScale(scene, origin);

        return Project(scene, origin + TransformGizmo.Direction(axis) * scale * along);
    }

    [Fact]
    public void Hit_OnAnAxisHandle_FindsThatAxis()
    {
        var (scene, gizmo, _) = Setup(GizmoMode.Translate);

        var (x, y) = HandleTip(scene, gizmo, GizmoAxis.X);

        Assert.Equal(GizmoAxis.X, gizmo.Hit(scene, x, y, out _));
    }

    [Fact]
    public void Hit_WellAwayFromEveryHandle_FindsNothing()
    {
        var (scene, gizmo, _) = Setup(GizmoMode.Translate);

        Assert.Equal(GizmoAxis.None, gizmo.Hit(scene, 4, 4, out _));
    }

    [Fact]
    public void Hit_OnTheAxisBehindTheOrigin_FindsNothing()
    {
        var (scene, gizmo, _) = Setup(GizmoMode.Translate);

        var (x, y) = HandleTip(scene, gizmo, GizmoAxis.X, along: -0.7f);

        Assert.NotEqual(GizmoAxis.X, gizmo.Hit(scene, x, y, out _));
    }

    [Fact]
    public void Hit_WithNoTarget_FindsNothing()
    {
        var (scene, gizmo, _) = Setup(GizmoMode.Translate);
        gizmo.Target = null;

        Assert.Equal(GizmoAxis.None, gizmo.Hit(scene, Center, Center, out _));
    }

    [Fact]
    public void Hit_WithTheGizmoOff_FindsNothing()
    {
        var (scene, gizmo, _) = Setup(GizmoMode.Translate);
        gizmo.Mode = GizmoMode.Off;

        var (x, y) = HandleTip(scene, gizmo, GizmoAxis.X);

        Assert.Equal(GizmoAxis.None, gizmo.Hit(scene, x, y, out _));
    }

    [Fact]
    public void Drag_AlongAnAxis_MovesTheMeshThatWay()
    {
        var (scene, gizmo, cube) = Setup(GizmoMode.Translate);

        var (x, y) = HandleTip(scene, gizmo, GizmoAxis.X);

        Assert.True(gizmo.Begin(scene, x, y));

        gizmo.Drag(scene, x + 40, y);
        gizmo.End();

        Assert.True(cube.Position.X > 0.1f, $"expected the cube to move along +X, got {cube.Position}");
        Assert.Equal(0f, cube.Position.Y, 3);
        Assert.Equal(0f, cube.Position.Z, 3);
    }

    [Fact]
    public void Drag_ReturningToWhereItStarted_LeavesTheMeshWhereItWas()
    {
        var (scene, gizmo, cube) = Setup(GizmoMode.Translate);

        var (x, y) = HandleTip(scene, gizmo, GizmoAxis.X);

        gizmo.Begin(scene, x, y);

        gizmo.Drag(scene, x + 70, y);
        gizmo.Drag(scene, x - 30, y);
        gizmo.Drag(scene, x, y);

        gizmo.End();

        Assert.Equal(0f, cube.Position.X, 3);
    }

    [Fact]
    public void Drag_OnAScaleHandle_StretchesOnlyThatAxis()
    {
        var (scene, gizmo, cube) = Setup(GizmoMode.Scale);

        var (x, y) = HandleTip(scene, gizmo, GizmoAxis.Y);

        Assert.True(gizmo.Begin(scene, x, y));

        gizmo.Drag(scene, x, y - 40);
        gizmo.End();

        Assert.True(cube.Scale.Y > 1.05f, $"expected the cube to stretch along Y, got {cube.Scale}");
        Assert.Equal(1f, cube.Scale.X, 3);
        Assert.Equal(1f, cube.Scale.Z, 3);
    }

    [Fact]
    public void Drag_AScaleHandleFarInward_NeverReachesZero()
    {
        var (scene, gizmo, cube) = Setup(GizmoMode.Scale);

        var (x, y) = HandleTip(scene, gizmo, GizmoAxis.Y);

        gizmo.Begin(scene, x, y);
        gizmo.Drag(scene, x, y + 4000);
        gizmo.End();

        Assert.True(cube.Scale.Y > 0f, $"scale collapsed to {cube.Scale.Y}");
    }

    [Fact]
    public void Drag_OnARotationRing_TurnsTheMeshAboutThatAxis()
    {
        var (scene, gizmo, cube) = Setup(GizmoMode.Rotate);

        var (x, y) = HandleTip(scene, gizmo, GizmoAxis.X, along: 1f);

        Assert.True(gizmo.Begin(scene, x, y));
        Assert.Equal(GizmoAxis.Z, gizmo.ActiveAxis);

        gizmo.Drag(scene, x, y - 30);
        gizmo.End();

        Assert.NotEqual(0f, cube.Rotation.ZRoll);
        Assert.Equal(0f, cube.Rotation.XPitch, 5);
        Assert.Equal(0f, cube.Rotation.YYaw, 5);
    }

    [Fact]
    public void Drag_WithTranslateSnapping_LandsOnAMultipleOfTheStep()
    {
        var (scene, gizmo, cube) = Setup(GizmoMode.Translate);

        gizmo.Snap.Enabled = true;
        gizmo.Snap.TranslateStep = 0.5f;

        var (x, y) = HandleTip(scene, gizmo, GizmoAxis.X);

        gizmo.Begin(scene, x, y);
        gizmo.Drag(scene, x + 37, y);
        gizmo.End();

        Assert.Equal(cube.Position.X, MathF.Round(cube.Position.X / 0.5f) * 0.5f, 4);
    }

    [Fact]
    public void Drag_WithSnapping_PutsAMeshThatStartedOffTheGridOntoIt()
    {
        var (scene, gizmo, cube) = Setup(GizmoMode.Translate);

        cube.Position = new Vector3(0.37f, 0f, 0f);

        gizmo.Snap.Enabled = true;
        gizmo.Snap.TranslateStep = 1f;

        var (x, y) = HandleTip(scene, gizmo, GizmoAxis.X);

        gizmo.Begin(scene, x, y);
        gizmo.Drag(scene, x + 40, y);
        gizmo.End();

        Assert.Equal(cube.Position.X, MathF.Round(cube.Position.X), 4);
    }

    [Fact]
    public void Drag_WithRotationSnapping_LandsOnAMultipleOfTheAngle()
    {
        var (scene, gizmo, cube) = Setup(GizmoMode.Rotate);

        var step = 15f * MathF.PI / 180f;

        gizmo.Snap.Enabled = true;
        gizmo.Snap.RotateStep = step;

        var (x, y) = HandleTip(scene, gizmo, GizmoAxis.X, along: 1f);

        gizmo.Begin(scene, x, y);
        gizmo.Drag(scene, x, y - 30);
        gizmo.End();

        Assert.Equal(cube.Rotation.ZRoll, MathF.Round(cube.Rotation.ZRoll / step) * step, 4);
    }

    [Fact]
    public void Drag_WithScaleSnapping_FarInward_NeverReachesZero()
    {
        var (scene, gizmo, cube) = Setup(GizmoMode.Scale);

        gizmo.Snap.Enabled = true;
        gizmo.Snap.ScaleStep = 0.5f;

        var (x, y) = HandleTip(scene, gizmo, GizmoAxis.Y);

        gizmo.Begin(scene, x, y);
        gizmo.Drag(scene, x, y + 4000);
        gizmo.End();

        Assert.True(cube.Scale.Y > 0f, $"scale collapsed to {cube.Scale.Y}");
    }

    [Fact]
    public void Drag_WithSnappingOff_IsUnchangedByTheSnapSettings()
    {
        var (sceneA, gizmoA, cubeA) = Setup(GizmoMode.Translate);
        var (sceneB, gizmoB, cubeB) = Setup(GizmoMode.Translate);

        gizmoB.Snap.TranslateStep = 0.25f;
        gizmoB.Snap.Enabled = false;

        var (x, y) = HandleTip(sceneA, gizmoA, GizmoAxis.X);

        gizmoA.Begin(sceneA, x, y);
        gizmoA.Drag(sceneA, x + 33, y);
        gizmoA.End();

        gizmoB.Begin(sceneB, x, y);
        gizmoB.Drag(sceneB, x + 33, y);
        gizmoB.End();

        Assert.Equal(cubeA.Position.X, cubeB.Position.X);
    }

    [Fact]
    public void End_AfterADragThatMoved_ReturnsAnUndoableEdit()
    {
        var (scene, gizmo, cube) = Setup(GizmoMode.Translate);

        var (x, y) = HandleTip(scene, gizmo, GizmoAxis.X);

        gizmo.Begin(scene, x, y);
        gizmo.Drag(scene, x + 40, y);

        var edit = gizmo.End();

        Assert.NotNull(edit);
        Assert.NotEqual(0f, cube.Position.X);

        edit.Revert();

        Assert.Equal(Vector3.Zero, cube.Position);
    }

    [Fact]
    public void End_AfterADragThatMovedNothing_ReturnsNull()
    {
        var (scene, gizmo, _) = Setup(GizmoMode.Translate);

        var (x, y) = HandleTip(scene, gizmo, GizmoAxis.X);

        gizmo.Begin(scene, x, y);

        Assert.Null(gizmo.End());
    }

    [Fact]
    public void Cancel_MidDrag_PutsTheMeshBack()
    {
        var (scene, gizmo, cube) = Setup(GizmoMode.Translate);

        var (x, y) = HandleTip(scene, gizmo, GizmoAxis.X);

        gizmo.Begin(scene, x, y);
        gizmo.Drag(scene, x + 60, y);

        Assert.NotEqual(0f, cube.Position.X);

        gizmo.Cancel();

        Assert.Equal(Vector3.Zero, cube.Position);
        Assert.False(gizmo.IsDragging);
    }

    [Fact]
    public void Drag_AParentedMesh_MovesWithTheCursorAndNotWithItsParentsScale()
    {
        var (scene, gizmo, cube) = Setup(GizmoMode.Translate);

        var node = new SceneNode("rig") { Scale = new Vector3(8f) };
        node.UpdateWorldMatrices();
        cube.Parent = node;

        var (x, y) = HandleTip(scene, gizmo, GizmoAxis.X);

        gizmo.Begin(scene, x, y);
        gizmo.Drag(scene, x + 40, y);
        gizmo.End();

        var moved = Vector3.Transform(Vector3.Zero, ((IMesh)cube).WorldMatrix);

        Assert.True(moved.X > 0.1f);
        Assert.Equal(moved.X / 8f, cube.Position.X, 3);
    }

    [Fact]
    public void HandleScale_UnderPerspective_GrowsWithDistance()
    {
        var (scene, gizmo, _) = Setup(GizmoMode.Translate);

        var near = TransformGizmo.HandleScale(scene, Vector3.Zero);

        scene.Camera.Position = new Vector3(0, 0, 80f);
        var far = TransformGizmo.HandleScale(scene, Vector3.Zero);

        Assert.True(far > near * 5f, $"a gizmo ten times further away should be about ten times larger in world units ({near} → {far})");

        var (x, y) = HandleTip(scene, gizmo, GizmoAxis.X);
        Assert.Equal(GizmoAxis.X, gizmo.Hit(scene, x, y, out _));
    }

    [Fact]
    public void HandleScale_UnderAParallelProjection_DoesNotDependOnDistance()
    {
        var (scene, _, _) = Setup(GizmoMode.Translate);
        scene.Projection = new OrthographicProjection(10f, 0.1f, 200f);

        var near = TransformGizmo.HandleScale(scene, Vector3.Zero);
        var far = TransformGizmo.HandleScale(scene, new Vector3(0, 0, -50f));

        Assert.Equal(near, far, 4);
    }

    [Fact]
    public void Hover_MidDrag_KeepsTheGrabbedHandleHighlighted()
    {
        var (scene, gizmo, _) = Setup(GizmoMode.Translate);

        var (x, y) = HandleTip(scene, gizmo, GizmoAxis.X);

        gizmo.Begin(scene, x, y);
        gizmo.Hover(scene, 4, 4);

        Assert.Equal(GizmoAxis.X, gizmo.HoveredAxis);
        Assert.Equal(GizmoAxis.X, gizmo.ActiveAxis);
    }

    [Theory]
    [InlineData(GizmoMode.Translate)]
    [InlineData(GizmoMode.Rotate)]
    [InlineData(GizmoMode.Scale)]
    public void Render_WithAGizmo_DrawsHandlesOverTheFrame(GizmoMode mode)
    {
        var (scene, gizmo, _) = Setup(mode);

        var renderer = new Renderer();
        scene.Surface.Stats = renderer.Stats;

        renderer.Render(scene, new ClassicPainter());
        var without = (int[])scene.Surface.Screen.Clone();

        renderer.Settings.Gizmo = gizmo;
        renderer.Render(scene, new ClassicPainter());

        var changed = 0;
        for (var i = 0; i < without.Length; i++)
        {
            if (without[i] != scene.Surface.Screen[i])
            {
                changed++;
            }
        }

        Assert.True(changed > 50, $"{mode} drew {changed} pixels");
    }

    [Fact]
    public void Render_WithTheGizmoOff_DrawsNothingExtra()
    {
        var (scene, gizmo, _) = Setup(GizmoMode.Off);

        var renderer = new Renderer();
        scene.Surface.Stats = renderer.Stats;

        renderer.Render(scene, new ClassicPainter());
        var without = (int[])scene.Surface.Screen.Clone();

        renderer.Settings.Gizmo = gizmo;
        renderer.Render(scene, new ClassicPainter());

        Assert.Equal(without, scene.Surface.Screen);
    }

    [Fact]
    public void Drag_WithoutHavingGrabbedAnything_ChangesNothing()
    {
        var (scene, gizmo, cube) = Setup(GizmoMode.Translate);

        gizmo.Drag(scene, Center + 50, Center);

        Assert.Equal(Vector3.Zero, cube.Position);
    }
}
