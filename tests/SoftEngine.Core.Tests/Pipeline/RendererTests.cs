using SoftEngine.Core.Buffers;
using SoftEngine.Core.Geometry.Primitives;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Cameras;
using SoftEngine.Core.Scenes.Graph;
using SoftEngine.Core.Scenes.Projections;
using System.Numerics;

namespace SoftEngine.Core.Tests.Pipeline;

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
    public void Render_MeshScaledByItsParentNode_IsCulledAsIfItScaledItself()
    {
        var node = new SceneNode("rig")
        {
            Position = new Vector3(0, 6f, 0),
            Scale = new Vector3(8f, 8f, 8f),
        };
        node.UpdateWorldMatrices();

        var (parented, parentedScene) = MakeCubeScene(new Vector3(0, 0, 5f));
        parentedScene.World.Meshes[0] = new Cube { Parent = node };

        var (direct, directScene) = MakeCubeScene(new Vector3(0, 0, 5f));
        directScene.World.Meshes[0] = new Cube
        {
            Position = new Vector3(0, 6f, 0),
            Scale = new Vector3(8f, 8f, 8f),
        };

        parented.Render(parentedScene, new ClassicPainter());
        direct.Render(directScene, new ClassicPainter());

        Assert.True(direct.Stats.DrawnPixelCount > 0, "the reference cube should reach the frame");
        Assert.Equal(direct.Stats.DrawnTriangleCount, parented.Stats.DrawnTriangleCount);
        Assert.Equal(direct.Stats.DrawnPixelCount, parented.Stats.DrawnPixelCount);
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

    private static (Renderer Renderer, Scene Scene) MakeOccludedScene()
    {
        var renderer = new Renderer();
        renderer.Settings.BackFaceCulling = true;

        var scene = new Scene
        {
            Surface = new FrameBuffer(256, 256) { Stats = renderer.Stats },
            Camera = new FixedCamera(new Vector3(0, 0, 6)),
            Projection = new PerspectiveProjection(MathF.PI / 4f, 0.1f, 100f),
            World = new SimpleWorld
            {
                Meshes =
                [
                    new IcoSphere(3) { Scale = new Vector3(1.5f) },
                    new IcoSphere(3) { Position = new Vector3(0, 0, -1.2f), Scale = new Vector3(1.5f) },
                ],
                Lights = [],
            },
        };

        return (renderer, scene);
    }

    [Fact]
    public void Render_HierarchicalZ_RejectsOccludedTrianglesWithoutChangingTheImage()
    {
        var (rejecting, rejectingScene) = MakeOccludedScene();
        var (plain, plainScene) = MakeOccludedScene();

        rejecting.Settings.HierarchicalZ = true;
        plain.Settings.HierarchicalZ = false;

        rejecting.Render(rejectingScene, new PhongPainter());
        plain.Render(plainScene, new PhongPainter());

        Assert.True(rejecting.Stats.OccludedTriangleCount > 0);
        Assert.Equal(0, plain.Stats.OccludedTriangleCount);

        for (var y = 0; y < 256; y++)
        {
            for (var x = 0; x < 256; x++)
            {
                Assert.Equal(plainScene.Surface.GetColor(x, y), rejectingScene.Surface.GetColor(x, y));
                Assert.Equal(plainScene.Surface.GetDepth(x, y), rejectingScene.Surface.GetDepth(x, y));
            }
        }
    }

    [Fact]
    public void Render_HierarchicalZ_DrawsTheSamePixelsAsTheDepthTestWould()
    {
        var (renderer, scene) = MakeOccludedScene();
        var (reference, referenceScene) = MakeOccludedScene();

        reference.Settings.HierarchicalZ = false;

        renderer.Render(scene, new PhongPainter());
        reference.Render(referenceScene, new PhongPainter());

        Assert.Equal(reference.Stats.DrawnPixelCount, renderer.Stats.DrawnPixelCount);
        Assert.True(renderer.Stats.BehindZPixelCount < reference.Stats.BehindZPixelCount);
    }
}
