using SoftEngine.Core.Buffers;
using SoftEngine.Core.Geometry.Primitives;
using SoftEngine.Core.Gizmos;
using SoftEngine.Core.Math;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Cameras;
using SoftEngine.Core.Scenes.Projections;
using System.Numerics;

namespace SoftEngine.Core.Tests.Gizmos;

public class ModalRotateTests
{
    private const int Size = 256;
    private const int Center = Size / 2;

    private sealed class FixedCamera(Vector3 position) : ICamera
    {
        public Vector3 Position { get; set; } = position;

        public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Vector3.Zero, Vector3.UnitY);
    }

    private static (Scene Scene, ModalTransform Transform, Cube Cube) Setup(Vector3 eye)
    {
        var cube = new Cube();

        var scene = new Scene
        {
            Surface = new FrameBuffer(Size, Size),
            Camera = new FixedCamera(eye),
            Projection = new PerspectiveProjection(MathF.PI / 4f, 0.1f, 100f),
            World = new SimpleWorld { Meshes = [cube], Lights = [] },
        };

        return (scene, new ModalTransform(), cube);
    }

    private static Vector3 Turned(Rotation3D rotation, Vector3 direction) =>
        Vector3.Transform(direction, rotation.ToQuaternion());

    private static void AssertClose(Vector3 expected, Vector3 actual, float tolerance = 1e-3f) =>
        Assert.True(Vector3.Distance(expected, actual) < tolerance, $"Expected {expected}, got {actual}.");

    [Theory]
    [InlineData(0f, 0f, 0f)]
    [InlineData(0.3f, -0.7f, 1.1f)]
    [InlineData(-1.2f, 2.5f, -0.4f)]
    [InlineData(1.5f, 0.2f, 3.0f)]
    public void Quaternion_RoundTrip_KeepsTheSameOrientation(float pitch, float yaw, float roll)
    {
        var rotation = new Rotation3D(pitch, yaw, roll);

        var back = Rotation3D.FromQuaternion(rotation.ToQuaternion());

        // Euler angles are not unique, so compare what they do rather than what they are.
        var expected = Matrix4x4.CreateFromYawPitchRoll(yaw, pitch, roll);
        var actual = Matrix4x4.CreateFromYawPitchRoll(back.YYaw, back.XPitch, back.ZRoll);

        AssertClose(Vector3.Transform(Vector3.UnitX, expected), Vector3.Transform(Vector3.UnitX, actual));
        AssertClose(Vector3.Transform(Vector3.UnitY, expected), Vector3.Transform(Vector3.UnitY, actual));
    }

    [Fact]
    public void Quaternion_AtGimbalLock_StillRoundTrips()
    {
        var rotation = new Rotation3D(MathF.PI / 2f, 0.4f, 0.9f);

        var back = Rotation3D.FromQuaternion(rotation.ToQuaternion());

        AssertClose(Turned(rotation, Vector3.UnitX), Turned(back, Vector3.UnitX));
        AssertClose(Turned(rotation, Vector3.UnitZ), Turned(back, Vector3.UnitZ));
    }

    [Fact]
    public void Rotate_Free_TurnsAboutTheViewAxis()
    {
        var (scene, transform, cube) = Setup(new Vector3(0, 0, 8f));

        Assert.True(transform.Begin(scene, cube, GizmoMode.Rotate, Center + 40, Center));

        // A quarter turn of the cursor around the object, right to straight above it.
        transform.Update(scene, Center, Center - 40);

        AssertClose(Vector3.UnitY, Turned(cube.Rotation, Vector3.UnitX), 0.05f);
        AssertClose(Vector3.UnitZ, Turned(cube.Rotation, Vector3.UnitZ), 0.05f);
    }

    [Fact]
    public void Rotate_ConstrainedToY_LeavesTheUpAxisAlone()
    {
        var (scene, transform, cube) = Setup(new Vector3(0, 6f, 8f));

        transform.Begin(scene, cube, GizmoMode.Rotate, Center + 50, Center);
        transform.Constrain(GizmoAxis.Y);
        transform.Update(scene, Center - 30, Center + 40);

        AssertClose(Vector3.UnitY, Turned(cube.Rotation, Vector3.UnitY));
        Assert.True(MathF.Abs(cube.Rotation.YYaw) > 0.1f, $"Yaw stayed at {cube.Rotation.YYaw}.");
    }

    [Fact]
    public void Rotate_ComposesWithTheRotationItStartedFrom()
    {
        var (scene, transform, cube) = Setup(new Vector3(0, 0, 8f));
        var start = new Rotation3D(0.3f, -0.7f, 1.1f);
        cube.Rotation = start;

        transform.Begin(scene, cube, GizmoMode.Rotate, Center + 40, Center);
        transform.Update(scene, Center, Center - 40);

        // The turn is about the world's view axis, applied after the orientation the mesh had.
        var quarter = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f);
        var expected = Vector3.Transform(Turned(start, Vector3.UnitX), quarter);

        AssertClose(expected, Turned(cube.Rotation, Vector3.UnitX), 0.05f);
    }

    [Fact]
    public void Rotate_Cancel_PutsTheRotationBack()
    {
        var (scene, transform, cube) = Setup(new Vector3(0, 0, 8f));
        var start = new Rotation3D(0.3f, -0.7f, 1.1f);
        cube.Rotation = start;

        transform.Begin(scene, cube, GizmoMode.Rotate, Center + 40, Center);
        transform.Update(scene, Center, Center - 40);
        transform.Cancel();

        Assert.Equal(start, cube.Rotation);
    }

    [Fact]
    public void Rotate_Confirm_RecordsAnUndoableRotate()
    {
        var (scene, transform, cube) = Setup(new Vector3(0, 0, 8f));

        transform.Begin(scene, cube, GizmoMode.Rotate, Center + 40, Center);
        transform.Update(scene, Center, Center - 40);

        var edit = transform.Confirm();

        Assert.NotNull(edit);
        Assert.Contains("Rotate", edit!.Description, StringComparison.Ordinal);

        edit.Revert();
        AssertClose(Vector3.UnitX, Turned(cube.Rotation, Vector3.UnitX));
    }

    [Fact]
    public void Rotate_Describe_NamesTheAngle()
    {
        var (scene, transform, cube) = Setup(new Vector3(0, 0, 8f));

        transform.Begin(scene, cube, GizmoMode.Rotate, Center + 40, Center);
        transform.Update(scene, Center, Center - 40);

        Assert.StartsWith("Rotate about view", transform.Describe(), StringComparison.Ordinal);
    }
}
