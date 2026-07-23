using SoftEngine.Core.Buffers;
using SoftEngine.Core.Geometry.Primitives;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Cameras;
using SoftEngine.Core.Scenes.Projections;
using System.Numerics;

namespace SoftEngine.Core.Tests;

public class RendererTests
{
    private sealed class FixedCamera(Vector3 position) : ICamera
    {
        public Vector3 Position { get; set; } = position;

        public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Vector3.Zero, Vector3.UnitY);
    }

    private static (Renderer Renderer, Scene Scene) MakeCubeScene(Vector3 eye, bool backFaceCulling = true)
    {
        var renderer = new Renderer();
        var surface = new FrameBuffer(128, 128) { Stats = renderer.Stats };
        renderer.Settings.BackFaceCulling = backFaceCulling;

        var scene = new Scene
        {
            Surface = surface,
            Camera = new FixedCamera(eye),
            Projection = new PerspectiveProjection(MathF.PI / 4f, 1f, 100f),
            World = new SimpleWorld { Meshes = [new Cube()], Lights = [] },
        };

        return (renderer, scene);
    }

    [Fact]
    public void Render_VisibleCube_DrawsPixels()
    {
        var (renderer, scene) = MakeCubeScene(new Vector3(0, 0, 5));

        renderer.Render(scene, new ClassicPainter());

        Assert.True(renderer.Stats.DrawnPixelCount > 0);
        Assert.True(renderer.Stats.DrawnTriangleCount > 0);
        Assert.NotEqual(0, scene.Surface.GetColor(64, 64));
    }

    [Fact]
    public void Render_BackFaceCulling_RejectsRoughlyHalfTheCube()
    {
        var (renderer, scene) = MakeCubeScene(new Vector3(0, 0, 5));

        renderer.Render(scene, new ClassicPainter());

        Assert.True(renderer.Stats.FacingBackTriangleCount >= 6);
    }

    [Fact]
    public void Render_CubeBehindCamera_DrawsNothing()
    {
        var (renderer, scene) = MakeCubeScene(new Vector3(0, 0, 5));
        scene.World.Meshes[0].Position = new Vector3(0, 0, 20);

        renderer.Render(scene, new ClassicPainter());

        Assert.Equal(0, renderer.Stats.DrawnPixelCount);
        Assert.Equal(0, renderer.Stats.DrawnTriangleCount);
    }

    [Fact]
    public void Render_InvisibleMesh_IsSkipped()
    {
        var (renderer, scene) = MakeCubeScene(new Vector3(0, 0, 5));
        ((Cube)scene.World.Meshes[0]).Visible = false;

        renderer.Render(scene, new ClassicPainter());

        Assert.Equal(0, renderer.Stats.DrawnPixelCount);
        Assert.Equal(0, renderer.Stats.DrawnTriangleCount);
    }

    [Fact]
    public void Render_CubeStraddlingNearPlane_IsClippedNotDiscarded()
    {
        var (renderer, scene) = MakeCubeScene(new Vector3(0.9f, 0.7f, 1.0f));

        renderer.Render(scene, new ClassicPainter());

        Assert.True(renderer.Stats.NearClippedTriangleCount > 0);
        Assert.True(renderer.Stats.DrawnPixelCount > 0);
    }

    [Fact]
    public void Render_CubeFullyInFrontOfNearPlane_ClipsNothing()
    {
        var (renderer, scene) = MakeCubeScene(new Vector3(0, 0, 5));

        renderer.Render(scene, new ClassicPainter());

        Assert.Equal(0, renderer.Stats.NearClippedTriangleCount);
    }

    [Fact]
    public void Render_StraddlingCube_WorksWithEveryPainter()
    {
        foreach (var painter in new Func<SoftEngine.Core.Rasterization.IPainter>[]
        {
            () => new ClassicPainter(),
            () => new FlatPainter(),
            () => new GouraudPainter(),
            () => new PhongPainter(),
            () => new TexturedPainter(),
        })
        {
            var (renderer, scene) = MakeCubeScene(new Vector3(0.9f, 0.7f, 1.0f));

            renderer.Render(scene, painter());

            Assert.True(renderer.Stats.DrawnPixelCount > 0);
            Assert.True(renderer.Stats.NearClippedTriangleCount > 0);
        }
    }

    [Fact]
    public void Render_SecondFrame_ReusesBuffersAndMatchesFirstFrame()
    {
        var (renderer, scene) = MakeCubeScene(new Vector3(0.9f, 0.7f, 1.0f));

        renderer.Render(scene, new ClassicPainter());
        var firstDrawn = renderer.Stats.DrawnPixelCount;
        var firstClipped = renderer.Stats.NearClippedTriangleCount;

        renderer.Render(scene, new ClassicPainter());

        Assert.Equal(firstDrawn, renderer.Stats.DrawnPixelCount);
        Assert.Equal(firstClipped, renderer.Stats.NearClippedTriangleCount);
    }

    [Fact]
    public void Render_WireframeOverlay_DrawsOnStraddlingCube()
    {
        var (renderer, scene) = MakeCubeScene(new Vector3(0.9f, 0.7f, 1.0f));
        renderer.Settings.ShowTriangles = true;

        renderer.Render(scene, new ClassicPainter());

        Assert.True(renderer.Stats.DrawnPixelCount > 0);
    }
}
