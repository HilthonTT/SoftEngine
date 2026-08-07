using SoftEngine.Core.Buffers;
using SoftEngine.Core.Editing;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Geometry.Primitives;
using SoftEngine.Core.Gizmos;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Cameras;
using SoftEngine.Core.Scenes.Projections;
using System.Numerics;

namespace SoftEngine.Core.Tests;

/// <summary>
/// The keyboard-driven move and scale. It has no handles, so there is nothing to hit-test and
/// nothing to draw — which leaves exactly one question worth asking of it, in several forms: does
/// the mesh end up where the cursor says it should, and does everything else about it stay put.
/// </summary>
public class ModalTransformTests
{
    private const int Size = 256;
    private const int Center = Size / 2;

    private sealed class FixedCamera(Vector3 position) : ICamera
    {
        public Vector3 Position { get; set; } = position;

        public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Vector3.Zero, Vector3.UnitY);
    }

    /// <summary>A cube at the origin seen straight down -Z, so screen X is world X and screen Y is world -Y.</summary>
    private static (Scene Scene, ModalTransform Transform, Cube Cube) Setup()
    {
        var cube = new Cube();

        var scene = new Scene
        {
            Surface = new FrameBuffer(Size, Size),
            Camera = new FixedCamera(new Vector3(0, 0, 8f)),
            Projection = new PerspectiveProjection(MathF.PI / 4f, 0.1f, 100f),
            World = new SimpleWorld { Meshes = [cube], Lights = [] },
        };

        return (scene, new ModalTransform(), cube);
    }

    [Fact]
    public void Begin_TakesTheMeshAndTheMode()
    {
        var (scene, transform, cube) = Setup();

        Assert.True(transform.Begin(scene, cube, GizmoMode.Translate, Center, Center));

        Assert.True(transform.IsActive);
        Assert.Same(cube, transform.Target);
        Assert.Equal(GizmoAxis.None, transform.Axis);
    }

    [Fact]
    public void Begin_WithNoMode_StartsNothing()
    {
        var (scene, transform, cube) = Setup();

        Assert.False(transform.Begin(scene, cube, GizmoMode.Off, Center, Center));
        Assert.False(transform.IsActive);
    }

    /// <summary>
    /// The whole point of the free gesture: the mesh follows the cursor across the frame, in the
    /// plane facing the viewer. Dragging right takes it along +X, dragging up along +Y — screen Y
    /// grows downward and world Y upward — and the depth it was at is left alone.
    /// </summary>
    [Fact]
    public void Translate_Free_FollowsTheCursorInThePlaneFacingTheViewer()
    {
        var (scene, transform, cube) = Setup();

        transform.Begin(scene, cube, GizmoMode.Translate, Center, Center);
        transform.Update(scene, Center + 40, Center - 25);

        Assert.True(cube.Position.X > 0f, $"Dragging right moved it to {cube.Position.X} on X.");
        Assert.True(cube.Position.Y > 0f, $"Dragging up moved it to {cube.Position.Y} on Y.");
        Assert.Equal(0f, cube.Position.Z, 4);
    }

    /// <summary>
    /// Constrained, the same cursor may only move it one way. The mesh has to stay on the axis
    /// however far off it the pointer wanders, which is the entire reason to press X.
    /// </summary>
    [Fact]
    public void Translate_ConstrainedToX_MovesOnlyAlongX()
    {
        var (scene, transform, cube) = Setup();

        transform.Begin(scene, cube, GizmoMode.Translate, Center, Center);
        transform.Constrain(GizmoAxis.X);
        transform.Update(scene, Center + 40, Center - 25);

        Assert.True(cube.Position.X > 0f);
        Assert.Equal(0f, cube.Position.Y, 4);
        Assert.Equal(0f, cube.Position.Z, 4);
    }

    /// <summary>
    /// Blender's "X, X". Releasing the constraint has to give the free gesture back rather than
    /// leaving the mesh stuck on the axis it was pressed against.
    /// </summary>
    [Fact]
    public void Constrain_TheSameAxisTwice_LetsItGoAgain()
    {
        var (scene, transform, cube) = Setup();

        transform.Begin(scene, cube, GizmoMode.Translate, Center, Center);

        transform.Constrain(GizmoAxis.Y);
        Assert.Equal(GizmoAxis.Y, transform.Axis);

        transform.Constrain(GizmoAxis.Y);
        Assert.Equal(GizmoAxis.None, transform.Axis);

        transform.Update(scene, Center + 40, Center - 25);
        Assert.True(cube.Position.X > 0f, "Released, it should be free to move on X again.");
    }

    /// <summary>
    /// A constraint applied half way through re-reads the gesture from where it began rather than
    /// from where the mesh had got to — so naming the axis late lands it exactly where naming it
    /// at the start would have, instead of adding whatever it had already travelled.
    /// </summary>
    [Fact]
    public void Constrain_MidGesture_LandsWhereConstrainingFromTheStartWould()
    {
        var (sceneA, lateConstraint, cubeA) = Setup();

        lateConstraint.Begin(sceneA, cubeA, GizmoMode.Translate, Center, Center);
        lateConstraint.Update(sceneA, Center + 30, Center - 30);
        lateConstraint.Constrain(GizmoAxis.X);
        lateConstraint.Update(sceneA, Center + 40, Center - 25);

        var (sceneB, fromTheStart, cubeB) = Setup();

        fromTheStart.Begin(sceneB, cubeB, GizmoMode.Translate, Center, Center);
        fromTheStart.Constrain(GizmoAxis.X);
        fromTheStart.Update(sceneB, Center + 40, Center - 25);

        Assert.Equal(cubeB.Position.X, cubeA.Position.X, 4);
        Assert.Equal(cubeB.Position.Y, cubeA.Position.Y, 4);
    }

    /// <summary>
    /// Scale reads the cursor's distance from the mesh's centre, so pushing away from it grows the
    /// mesh and coming back in shrinks it — on all three axes at once, unconstrained.
    /// </summary>
    [Fact]
    public void Scale_Free_GrowsWithTheCursorsDistanceFromTheCentre()
    {
        var (scene, transform, cube) = Setup();

        transform.Begin(scene, cube, GizmoMode.Scale, Center + 30, Center);

        transform.Update(scene, Center + 60, Center);
        var grown = cube.Scale;

        transform.Update(scene, Center + 15, Center);
        var shrunk = cube.Scale;

        Assert.True(grown.X > 1f, $"Pushing out should grow it; got {grown.X}.");
        Assert.Equal(grown.X, grown.Y, 4);
        Assert.Equal(grown.X, grown.Z, 4);

        Assert.True(shrunk.X < 1f, $"Coming back in should shrink it; got {shrunk.X}.");
    }

    [Fact]
    public void Scale_ConstrainedToY_StretchesOnlyThatAxis()
    {
        var (scene, transform, cube) = Setup();

        transform.Begin(scene, cube, GizmoMode.Scale, Center + 30, Center);
        transform.Constrain(GizmoAxis.Y);
        transform.Update(scene, Center + 60, Center);

        Assert.Equal(1f, cube.Scale.X, 4);
        Assert.True(cube.Scale.Y > 1f);
        Assert.Equal(1f, cube.Scale.Z, 4);
    }

    /// <summary>A scale can approach zero but must never arrive: a zero matrix cannot be inverted.</summary>
    [Fact]
    public void Scale_DraggingThroughTheCentre_NeverReachesZero()
    {
        var (scene, transform, cube) = Setup();

        transform.Begin(scene, cube, GizmoMode.Scale, Center + 60, Center);
        transform.Update(scene, Center, Center);

        Assert.True(cube.Scale.X > 0f, $"Scale collapsed to {cube.Scale.X}.");
    }

    /// <summary>
    /// The case that rules out measuring a scale as the ratio of two distances from the centre:
    /// pressing S with the pointer on the mesh you are looking at leaves that ratio a pixel or two
    /// over a pixel or two, and the next small movement multiplies the mesh by tens. Measuring the
    /// cursor's <em>travel</em> against a handle length has no such point to avoid.
    /// </summary>
    [Fact]
    public void Scale_StartedDeadOnTheCentre_StillScalesByHandFuls()
    {
        var (scene, transform, cube) = Setup();

        transform.Begin(scene, cube, GizmoMode.Scale, Center, Center);
        transform.Update(scene, Center + 50, Center);

        Assert.InRange(cube.Scale.X, 1f, 5f);
    }

    [Fact]
    public void Confirm_HandsBackAnEditThatUndoesTheMove()
    {
        var (scene, transform, cube) = Setup();

        transform.Begin(scene, cube, GizmoMode.Translate, Center, Center);
        transform.Update(scene, Center + 40, Center);

        var moved = cube.Position;
        var edit = transform.Confirm();

        Assert.NotNull(edit);
        Assert.False(transform.IsActive);
        Assert.Equal(moved, cube.Position);

        edit.Revert();
        Assert.Equal(Vector3.Zero, cube.Position);

        edit.Apply();
        Assert.Equal(moved, cube.Position);
    }

    /// <summary>
    /// A G pressed and confirmed without touching the mouse is not an edit, and putting one on the
    /// history would make the next Ctrl+Z appear to do nothing.
    /// </summary>
    [Fact]
    public void Confirm_WithoutMoving_HandsBackNothing()
    {
        var (scene, transform, cube) = Setup();

        transform.Begin(scene, cube, GizmoMode.Translate, Center, Center);

        Assert.Null(transform.Confirm());
    }

    [Fact]
    public void Cancel_PutsTheMeshBackExactlyWhereItWas()
    {
        var (scene, transform, cube) = Setup();

        cube.Position = new Vector3(1f, 2f, 3f);
        cube.Scale = new Vector3(2f, 2f, 2f);

        transform.Begin(scene, cube, GizmoMode.Translate, Center, Center);
        transform.Update(scene, Center + 40, Center - 40);
        transform.Cancel();

        Assert.False(transform.IsActive);
        Assert.Equal(new Vector3(1f, 2f, 3f), cube.Position);
        Assert.Equal(new Vector3(2f, 2f, 2f), cube.Scale);
    }

    /// <summary>
    /// Starting a second gesture while one is running must not leave the first one's half-finished
    /// change behind — the keystroke that starts it is the user changing their mind, not adding to
    /// it.
    /// </summary>
    [Fact]
    public void Begin_WhileAnotherIsRunning_AbandonsTheFirst()
    {
        var (scene, transform, cube) = Setup();

        transform.Begin(scene, cube, GizmoMode.Translate, Center, Center);
        transform.Update(scene, Center + 40, Center);

        transform.Begin(scene, cube, GizmoMode.Scale, Center + 30, Center);

        Assert.Equal(Vector3.Zero, cube.Position);
        Assert.Equal(GizmoMode.Scale, transform.Mode);
    }

    /// <summary>
    /// Snapping rounds the mesh's <em>resulting</em> position rather than the distance it
    /// travelled, which is what makes two meshes moved onto the same gridline actually meet.
    /// </summary>
    [Fact]
    public void Translate_Snapped_LandsOnTheGridRatherThanBesideIt()
    {
        var (scene, transform, cube) = Setup();

        cube.Position = new Vector3(0.37f, 0f, 0f);

        transform.Snap.Enabled = true;
        transform.Snap.TranslateStep = 1f;

        transform.Begin(scene, cube, GizmoMode.Translate, Center, Center);
        transform.Constrain(GizmoAxis.X);
        transform.Update(scene, Center + 40, Center);

        Assert.Equal(MathF.Round(cube.Position.X), cube.Position.X, 4);
    }

    /// <summary>
    /// The edit is named after what it did, because the Edit menu shows that name and "Undo" on
    /// its own tells you nothing about which change is about to come back.
    /// </summary>
    [Theory]
    [InlineData(GizmoMode.Translate, "Move Cube")]
    [InlineData(GizmoMode.Scale, "Scale Cube")]
    public void Confirm_NamesTheEditAfterTheGesture(GizmoMode mode, string expected)
    {
        var (scene, transform, cube) = Setup();

        transform.Begin(scene, cube, mode, Center + 30, Center);
        transform.Update(scene, Center + 70, Center + 20);

        var edit = transform.Confirm();

        Assert.NotNull(edit);
        Assert.Equal(expected, edit.Description);
    }
}
