using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Geometry.Primitives;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Pipeline.Debugging;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Cameras;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Scenes.Projections;
using System.Numerics;

namespace SoftEngine.Core.Tests.Diagnostics;

public class OcclusionViewTests
{
    private const int Width = 200;
    private const int Height = 150;

    private sealed class FixedCamera(Vector3 position) : ICamera
    {
        public Vector3 Position { get; set; } = position;

        public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Vector3.Zero, Vector3.UnitY);
    }

    private static Mesh Wall(float half, float z)
    {
        Vector3[] vertices =
        [
            new(-half, -half, z),
            new(half, -half, z),
            new(half, half, z),
            new(-half, half, z),
        ];

        var normal = Vector3.UnitZ;
        var color = ColorRGB.Gray;

        return new Mesh(vertices, [new(0, 1, 2), new(0, 2, 3)], [normal, normal, normal, normal], [color, color]);
    }

    private static Mesh SmallCube(Vector3 position)
    {
        var source = new Cube();
        var colors = new ColorRGB[source.Triangles.Length];
        Array.Fill(colors, new ColorRGB(220, 90, 70));

        return new Mesh(
            (Vector3[])source.Vertices.Clone(),
            source.Triangles,
            (Vector3[])source.NormVertices.Clone(),
            colors)
        {
            Position = position,
            Scale = new Vector3(0.2f),
        };
    }

    private static (Renderer Renderer, Scene Scene) Build(params IMesh[] meshes)
    {
        var renderer = new Renderer();
        renderer.Settings.BackFaceCulling = true;
        renderer.Diagnostics.Events.IsEnabled = false;

        renderer.Occlusion.MinimumTestableMeshes = 1;

        var scene = new Scene
        {
            Surface = new FrameBuffer(Width, Height) { Stats = renderer.Stats },
            Camera = new FixedCamera(new Vector3(0f, 0f, 5f)),
            Projection = new PerspectiveProjection(MathF.PI / 4f, 0.1f, 100f),
            World = new SimpleWorld
            {
                Meshes = [.. meshes],
                Lights = [new DirectionalLight { Direction = Vector3.Normalize(new Vector3(-0.3f, -0.6f, -0.7f)) }],
            },
        };

        return (renderer, scene);
    }

    private static int CoveredPixels(FrameBuffer surface)
    {
        var empty = unchecked((int)0xFF1C222E);
        var covered = 0;

        foreach (var pixel in surface.Screen)
        {
            if (pixel != empty)
            {
                covered++;
            }
        }

        return covered;
    }

    [Fact]
    public void OcclusionView_WithAWallOnScreen_DrawsTheCoverageItProduced()
    {
        var (renderer, scene) = Build(Wall(3f, 0f), SmallCube(new Vector3(0f, 0f, -2f)));

        renderer.Settings.DebugView = DebugView.OcclusionBuffer;
        renderer.Render(scene, new GouraudPainter());

        Assert.Equal(1, renderer.Stats.OccluderMeshCount);

        var covered = CoveredPixels(scene.Surface);

        Assert.True(covered > Width * Height / 4,
            $"expected the wall's coverage to fill much of the view, got {covered} of {Width * Height} pixels");
    }

    [Fact]
    public void OcclusionView_WithThePassSwitchedOff_LeavesTheShadedImageAlone()
    {
        var (renderer, scene) = Build(Wall(3f, 0f), SmallCube(new Vector3(0f, 0f, -2f)));

        renderer.Settings.OcclusionCulling = false;
        renderer.Settings.DebugView = DebugView.OcclusionBuffer;
        renderer.Render(scene, new GouraudPainter());

        var shaded = (int[])scene.Surface.Screen.Clone();

        renderer.Settings.DebugView = DebugView.Off;
        renderer.Render(scene, new GouraudPainter());

        Assert.Equal(shaded, scene.Surface.Screen);
    }

    [Fact]
    public void OcclusionView_OfAProbedFrame_HasNothingToShow()
    {
        var (renderer, scene) = Build(Wall(3f, 0f), SmallCube(new Vector3(0f, 0f, -2f)));

        renderer.Settings.DebugView = DebugView.OcclusionBuffer;

        renderer.Render(scene, new GouraudPainter());

        renderer.Diagnostics.SetProbe(Width / 2, Height / 2);
        renderer.Render(scene, new GouraudPainter());

        Assert.Equal(0, renderer.Stats.OccluderMeshCount);

        var presented = (int[])scene.Surface.Screen.Clone();

        renderer.Settings.DebugView = DebugView.Off;
        renderer.Render(scene, new GouraudPainter());

        Assert.Equal(scene.Surface.Screen, presented);
    }

    [Fact]
    public void OcclusionView_DoesNotChangeWhatThePassRejects()
    {
        var (plain, plainScene) = Build(Wall(3f, 0f), SmallCube(new Vector3(0f, 0f, -2f)));
        plain.Render(plainScene, new GouraudPainter());

        var (viewed, viewedScene) = Build(Wall(3f, 0f), SmallCube(new Vector3(0f, 0f, -2f)));
        viewed.Settings.DebugView = DebugView.OcclusionBuffer;
        viewed.Render(viewedScene, new GouraudPainter());

        Assert.Equal(plain.Stats.OccludedMeshCount, viewed.Stats.OccludedMeshCount);
        Assert.Equal(plain.Stats.OccluderMeshCount, viewed.Stats.OccluderMeshCount);
        Assert.Equal(plain.Stats.DrawnTriangleCount, viewed.Stats.DrawnTriangleCount);
    }
}
