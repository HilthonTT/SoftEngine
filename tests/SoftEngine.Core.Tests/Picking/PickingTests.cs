using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Geometry.Primitives;
using SoftEngine.Core.Math;
using SoftEngine.Core.Picking;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Cameras;
using SoftEngine.Core.Scenes.Graph;
using SoftEngine.Core.Scenes.Projections;
using System.Numerics;

namespace SoftEngine.Core.Tests.Picking;

public class PickingTests
{
    private sealed class FixedCamera(Vector3 position) : ICamera
    {
        public Vector3 Position { get; set; } = position;

        public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Vector3.Zero, Vector3.UnitY);
    }

    private const int Size = 65;
    private const int Centre = Size / 2;

    private static Scene SceneWith(params IMesh[] meshes)
    {
        var world = new SimpleWorld();
        world.Meshes.AddRange(meshes);

        return new Scene
        {
            World = world,
            Camera = new FixedCamera(new Vector3(0, 0, -10f)),
            Projection = new PerspectiveProjection(MathF.PI / 4f, 0.1f, 100f),
            Surface = new FrameBuffer(Size, Size) { Stats = new RenderStats() },
        };
    }

    #region The ray

    [Fact]
    public void TheRayThroughTheMiddleOfTheFrameLooksStraightDownTheViewAxis()
    {
        var scene = SceneWith(new Cube());

        var ray = ScenePicker.RayThrough(scene, (Size - 1) / 2f, (Size - 1) / 2f);

        Approx.Equal(new Vector3(0, 0, -10f), ray.Origin, 1e-3f);
        Approx.Equal(Vector3.UnitZ, ray.Direction, 1e-3f);
    }

    [Fact]
    public void RaysSpreadSymmetricallyAboutTheCentreOfTheFrame()
    {
        var scene = SceneWith(new Cube());

        var middle = (Size - 1) / 2f;

        var left = ScenePicker.RayThrough(scene, 0, middle);
        var right = ScenePicker.RayThrough(scene, Size - 1, middle);
        var above = ScenePicker.RayThrough(scene, middle, 0);
        var below = ScenePicker.RayThrough(scene, middle, Size - 1);

        Approx.Equal(new Vector3(0, 0, -10f), right.Origin, 1e-3f);

        Assert.True(left.Direction.X * right.Direction.X < 0f, "the sides of a row should lean opposite ways");
        Assert.Equal(0f, left.Direction.Y, 3);
        Assert.Equal(0f, right.Direction.Y, 3);

        Assert.True(above.Direction.Y * below.Direction.Y < 0f, "the ends of a column should lean opposite ways");
        Assert.Equal(0f, above.Direction.X, 3);
        Assert.Equal(0f, below.Direction.X, 3);

        Assert.Equal(MathF.Abs(left.Direction.X), MathF.Abs(above.Direction.Y), 3);
    }

    [Fact]
    public void ARayHitsTheTriangleItPassesThroughAndMissesTheOnesItDoesNot()
    {
        var a = new Vector3(-1, -1, 0);
        var b = new Vector3(1, -1, 0);
        var c = new Vector3(0, 1, 0);

        var through = new Ray(new Vector3(0, 0, -5f), Vector3.UnitZ);
        Assert.True(ScenePicker.IntersectsTriangle(through, a, b, c, out var distance));
        Assert.Equal(5f, distance, 4);

        var past = new Ray(new Vector3(3f, 0, -5f), Vector3.UnitZ);
        Assert.False(ScenePicker.IntersectsTriangle(past, a, b, c, out _));

        var backwards = new Ray(new Vector3(0, 0, 5f), Vector3.UnitZ);
        Assert.False(ScenePicker.IntersectsTriangle(backwards, a, b, c, out _));
    }

    [Fact]
    public void ATriangleIsPickableFromBothSides()
    {
        var a = new Vector3(-1, -1, 0);
        var b = new Vector3(1, -1, 0);
        var c = new Vector3(0, 1, 0);

        var front = new Ray(new Vector3(0, 0, -5f), Vector3.UnitZ);
        var back = new Ray(new Vector3(0, 0, 5f), -Vector3.UnitZ);

        Assert.True(ScenePicker.IntersectsTriangle(front, a, b, c, out _));
        Assert.True(ScenePicker.IntersectsTriangle(back, a, b, c, out _));
    }

    [Fact]
    public void ARayMissesASphereItPassesBeside()
    {
        var ray = new Ray(new Vector3(0, 0, -10f), Vector3.UnitZ);

        Assert.True(ray.IntersectsSphere(Vector3.Zero, 1f, out var entry));
        Assert.Equal(9f, entry, 4);

        Assert.False(ray.IntersectsSphere(new Vector3(5f, 0, 0), 1f, out _));

        Assert.True(new Ray(Vector3.Zero, Vector3.UnitZ).IntersectsSphere(Vector3.Zero, 1f, out var inside));
        Assert.Equal(0f, inside);

        Assert.False(ray.IntersectsSphere(new Vector3(0, 0, -20f), 1f, out _));
    }

    #endregion

    #region Picking a scene

    [Fact]
    public void ClickingAModelPicksTheFaceUnderTheCursor()
    {
        var cube = new Cube();
        var scene = SceneWith(cube);

        var hit = ScenePicker.Pick(scene, Centre, Centre);

        Assert.NotNull(hit);
        Assert.Same(cube, hit.Value.Mesh);
        Assert.Equal(0, hit.Value.MeshIndex);

        Assert.Equal(9.5f, hit.Value.Distance, 2);
        Assert.Equal(-0.5f, hit.Value.Point.Z, 3);
        Assert.InRange(hit.Value.Point.X, -0.1f, 0.1f);
        Assert.InRange(hit.Value.Point.Y, -0.1f, 0.1f);

        Assert.True(Vector3.Dot(hit.Value.Normal, -Vector3.UnitZ) > 0.9f);
    }

    [Fact]
    public void ClickingTheBackgroundPicksNothing()
    {
        var scene = SceneWith(new Cube());

        Assert.Null(ScenePicker.Pick(scene, 0, 0));
    }

    [Fact]
    public void TheNearestMeshWins()
    {
        var far = new Cube { Position = new Vector3(0, 0, 3f) };
        var near = new Cube { Position = new Vector3(0, 0, -3f) };

        var scene = SceneWith(far, near);

        var hit = ScenePicker.Pick(scene, Centre, Centre);

        Assert.NotNull(hit);
        Assert.Same(near, hit.Value.Mesh);
        Assert.Equal(1, hit.Value.MeshIndex);
    }

    [Fact]
    public void AMeshSwitchedOffInTheObjectTableIsNotPickable()
    {
        var behind = new Cube { Position = new Vector3(0, 0, 3f) };
        var front = new Cube { Position = new Vector3(0, 0, -3f), Visible = false };

        var scene = SceneWith(behind, front);

        var hit = ScenePicker.Pick(scene, Centre, Centre);

        Assert.NotNull(hit);
        Assert.Same(behind, hit.Value.Mesh);
    }

    [Fact]
    public void TransparentGeometryIsStillPickable()
    {
        var glass = new Cube { Opacity = 0.4f };
        var scene = SceneWith(glass);

        Assert.Same(glass, ScenePicker.Pick(scene, Centre, Centre)?.Mesh);

        glass.Opacity = 0f;
        Assert.Null(ScenePicker.Pick(scene, Centre, Centre));
    }

    [Fact]
    public void PickingFollowsAMeshsOwnTransform()
    {
        var moved = new Cube
        {
            Position = new Vector3(2f, 0, 0),
            Scale = new Vector3(2f, 2f, 2f),
            Rotation = new Rotation3D(0, 30, 0).ToRad(),
        };

        var scene = SceneWith(moved);

        Assert.Null(ScenePicker.Pick(scene, Centre, Centre));

        var ray = new Ray(new Vector3(2f, 0, -10f), Vector3.UnitZ);
        var hit = ScenePicker.Pick(scene.World, ray);

        Assert.NotNull(hit);
        Assert.Same(moved, hit.Value.Mesh);
        Assert.InRange(hit.Value.Distance, 8.5f, 9.5f);
    }

    [Fact]
    public void PickingFollowsTheSceneGraphAboveAMesh()
    {
        var node = new SceneNode("root")
        {
            Position = new Vector3(0, 4f, 0),
            Scale = new Vector3(3f, 3f, 3f),
        };
        node.UpdateWorldMatrices();

        var mesh = new Cube { Parent = node };
        var scene = SceneWith(mesh);

        Assert.Null(ScenePicker.Pick(scene.World, new Ray(new Vector3(0, 0, -10f), Vector3.UnitZ)));

        var hit = ScenePicker.Pick(scene.World, new Ray(new Vector3(0, 4f, -10f), Vector3.UnitZ));

        Assert.NotNull(hit);
        Assert.Same(mesh, hit.Value.Mesh);

        Assert.Equal(8.5f, hit.Value.Distance, 3);
    }

    [Fact]
    public void TheReportedTriangleIsOneTheRayReallyPassesThrough()
    {
        var cube = new Cube();
        var scene = SceneWith(cube);

        var hit = ScenePicker.Pick(scene, Centre, Centre);

        Assert.NotNull(hit);

        var triangle = cube.Triangles[hit.Value.TriangleIndex];
        var ray = ScenePicker.RayThrough(scene, Centre + 0.5f, Centre + 0.5f);

        Assert.True(ScenePicker.IntersectsTriangle(
            ray,
            cube.Vertices[triangle.I0],
            cube.Vertices[triangle.I1],
            cube.Vertices[triangle.I2],
            out var distance));

        Assert.Equal(hit.Value.Distance, distance, 3);
    }

    private static Mesh Quad(float x, float halfSize, ColorRGB color)
    {
        Vector3[] vertices =
        [
            new(x - halfSize, -halfSize, 0f),
            new(x + halfSize, -halfSize, 0f),
            new(x + halfSize, halfSize, 0f),
            new(x - halfSize, halfSize, 0f),
        ];

        Triangle[] triangles = [new(0, 1, 2), new(0, 2, 3)];
        Vector3[] normals = [-Vector3.UnitZ, -Vector3.UnitZ, -Vector3.UnitZ, -Vector3.UnitZ];

        return new Mesh(vertices, triangles, normals, [color, color]);
    }

    [Fact]
    public void PickingAgreesWithWhatTheRendererDrew()
    {
        var left = Quad(-1.5f, 1f, ColorRGB.Red);
        var right = Quad(1.5f, 1f, ColorRGB.Blue);

        var scene = SceneWith(left, right);

        new Renderer().Render(scene, new ClassicPainter());

        for (var x = 4; x < Size - 4; x += 4)
        {
            var packed = scene.Surface.GetColor(x, Centre);
            var hit = ScenePicker.Pick(scene, x, Centre);

            var red = (packed >> 16) & 0xFF;
            var blue = packed & 0xFF;

            if (red > 200 && blue < 50)
            {
                Assert.Same(left, hit?.Mesh);
            }
            else if (blue > 200 && red < 50)
            {
                Assert.Same(right, hit?.Mesh);
            }
            else
            {
                Assert.Null(hit);
            }
        }
    }

    #endregion

    #region The highlight

    private static int[] RenderWithHighlight(Scene scene, int highlighted)
    {
        var renderer = new Renderer
        {
            Settings = new RendererSettings { HighlightedMesh = highlighted },
        };

        renderer.Render(scene, new ClassicPainter());

        return [.. scene.Surface.Screen];
    }

    private static List<int> PixelsThatDiffer(int[] before, int[] after)
    {
        var changed = new List<int>();

        for (var i = 0; i < before.Length; i++)
        {
            if (before[i] != after[i])
            {
                changed.Add(i);
            }
        }

        return changed;
    }

    private static bool IsRed(int packed) => ((packed >> 16) & 0xFF) > 200 && (packed & 0xFF) < 50;

    private static bool IsBlue(int packed) => (packed & 0xFF) > 200 && ((packed >> 16) & 0xFF) < 50;

    [Fact]
    public void TheHighlightOutlinesThePickedMeshAndLeavesTheOthersAlone()
    {
        var left = Quad(-1.5f, 1f, ColorRGB.Red);
        var right = Quad(1.5f, 1f, ColorRGB.Blue);

        var scene = SceneWith(left, right);

        var plain = RenderWithHighlight(scene, -1);
        var outlined = RenderWithHighlight(scene, 1);

        var changed = PixelsThatDiffer(plain, outlined);

        Assert.NotEmpty(changed);

        var onTheHighlightedMesh = 0;

        foreach (var i in changed)
        {
            Assert.False(IsRed(plain[i]), "the un-highlighted mesh should not have been drawn over");

            if (IsBlue(plain[i]))
            {
                onTheHighlightedMesh++;
            }

            var (r, g, b) = ((outlined[i] >> 16) & 0xFF, (outlined[i] >> 8) & 0xFF, outlined[i] & 0xFF);

            Assert.True(r > 200 && r > g && g > b, $"expected an amber outline, got ({r}, {g}, {b})");
        }

        Assert.True(onTheHighlightedMesh > 0, "the outline should lie on the mesh it highlights");
    }

    [Fact]
    public void NoHighlightLeavesTheFrameExactlyAsItWas()
    {
        var scene = SceneWith(Quad(0f, 1f, ColorRGB.Red));

        Assert.Equal(RenderWithHighlight(scene, -1), RenderWithHighlight(scene, -1));
    }

    [Fact]
    public void HighlightingAMeshTheFrameNeverDrewOutlinesNothing()
    {
        var drawn = Quad(0f, 1f, ColorRGB.Red);
        var off = Quad(0f, 1f, ColorRGB.Blue);
        off.Visible = false;

        var scene = SceneWith(drawn, off);

        Assert.Equal(RenderWithHighlight(scene, -1), RenderWithHighlight(scene, 1));
    }

    [Fact]
    public void AHighlightIndexOutsideTheWorldIsIgnoredRatherThanThrowing()
    {
        var scene = SceneWith(Quad(0f, 1f, ColorRGB.Red));

        var plain = RenderWithHighlight(scene, -1);

        Assert.Equal(plain, RenderWithHighlight(scene, 7));
        Assert.Equal(plain, RenderWithHighlight(scene, int.MaxValue));
    }

    #endregion
}
