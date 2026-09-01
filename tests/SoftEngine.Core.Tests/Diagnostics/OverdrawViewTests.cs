using SoftEngine.Core.Buffers;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Geometry.Primitives;
using SoftEngine.Core.Gizmos;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Pipeline.Debugging;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Cameras;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Scenes.Projections;
using SoftEngine.Core.Textures;
using System.Numerics;

namespace SoftEngine.Core.Tests.Diagnostics;

public class OverdrawViewTests
{
    private const int Size = 64;
    private const int Centre = Size / 2;

    private sealed class FixedCamera(Vector3 position) : ICamera
    {
        public Vector3 Position { get; set; } = position;

        public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Vector3.Zero, Vector3.UnitY);
    }

    private static (Renderer Renderer, Scene Scene) Slabs(
        int count,
        bool nearestFirst,
        bool hierarchicalZ = false,
        bool backFaceCulling = true,
        bool sky = false)
    {
        var renderer = new Renderer();
        renderer.Settings.DebugView = DebugView.Overdraw;
        renderer.Settings.HierarchicalZ = hierarchicalZ;
        renderer.Settings.BackFaceCulling = backFaceCulling;

        renderer.Settings.NearestMeshesFirst = false;

        var world = new SimpleWorld
        {
            Lights = [new DirectionalLight { Direction = new Vector3(0, 0, -1f) }],
        };

        for (var i = 0; i < count; i++)
        {
            var slot = nearestFirst ? i : count - 1 - i;

            world.Meshes.Add(new Cube
            {
                Position = new Vector3(0, 0, slot * 1.5f),

                Scale = new Vector3(8f, 8f, 0.2f),
            });
        }

        var scene = new Scene
        {
            World = world,
            Camera = new FixedCamera(new Vector3(0, 0, -20f)),
            Projection = new PerspectiveProjection(MathF.PI / 4f, 0.5f, 200f),
            Surface = new FrameBuffer(Size, Size) { Stats = renderer.Stats },
            ShowSky = sky,
            Environment = sky ? SkyBox.Gradient(Vector3.Normalize(new Vector3(-0.3f, -1f, -0.4f)), resolution: 16) : null,
        };

        return (renderer, scene);
    }

    private static int CentreCount(Scene scene) => scene.Surface.Overdraw[Centre + Centre * Size];

    private static int CornerCount(Scene scene) => scene.Surface.Overdraw[0];

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(9)]
    public void Overdraw_SlabsDrawnFarthestFirst_CountsExactlyOneWritePerSlab(int slabs)
    {
        var (renderer, scene) = Slabs(slabs, nearestFirst: false);

        renderer.Render(scene, new GouraudPainter());

        Assert.Equal(slabs, CentreCount(scene));
    }

    [Fact]
    public void Overdraw_BackFaceCullingOff_CountsBothFacesOfEachSlab()
    {
        var (renderer, scene) = Slabs(4, nearestFirst: false, backFaceCulling: false);

        renderer.Render(scene, new GouraudPainter());

        Assert.Equal(8, CentreCount(scene));
    }

    [Fact]
    public void Overdraw_SlabsDrawnNearestFirst_CountsOnlyTheWritesTheFillAttempted()
    {
        var (renderer, scene) = Slabs(9, nearestFirst: true);
        renderer.Render(scene, new GouraudPainter());

        Assert.Equal(1, CentreCount(scene));

        var (other, otherScene) = Slabs(9, nearestFirst: false);
        other.Render(otherScene, new GouraudPainter());

        Assert.Equal(9, CentreCount(otherScene));
    }

    [Fact]
    public void Overdraw_NearestMeshesFirst_CostsWhatFrontToBackCosts()
    {
        var (renderer, scene) = Slabs(9, nearestFirst: false);
        renderer.Settings.NearestMeshesFirst = true;

        renderer.Render(scene, new GouraudPainter());

        Assert.Equal(1, CentreCount(scene));
    }

    [Fact]
    public void NearestMeshesFirst_DrawsTheSameFrame()
    {
        var (ordered, orderedScene) = Slabs(9, nearestFirst: false);
        ordered.Settings.DebugView = DebugView.Off;
        ordered.Settings.NearestMeshesFirst = true;
        ordered.Render(orderedScene, new GouraudPainter());

        var (listOrder, listOrderScene) = Slabs(9, nearestFirst: false);
        listOrder.Settings.DebugView = DebugView.Off;
        listOrder.Render(listOrderScene, new GouraudPainter());

        Assert.Equal(
            listOrderScene.Surface.Screen.ToArray(),
            orderedScene.Surface.Screen.ToArray());
    }

    [Fact]
    public void Overdraw_HierarchicalZ_NeverRaisesTheCount()
    {
        var (without, withoutScene) = Slabs(40, nearestFirst: true, hierarchicalZ: false);
        without.Render(withoutScene, new GouraudPainter());

        var (with, withScene) = Slabs(40, nearestFirst: true, hierarchicalZ: true);
        with.Render(withScene, new GouraudPainter());

        Assert.True(
            CentreCount(withScene) <= CentreCount(withoutScene),
            $"hi-z raised the count: {CentreCount(withScene)} vs {CentreCount(withoutScene)}");
    }

    [Fact]
    public void Overdraw_EmptyFrame_IsZeroEverywhere()
    {
        var (renderer, scene) = Slabs(0, nearestFirst: false);

        renderer.Render(scene, new GouraudPainter());

        Assert.All(scene.Surface.Overdraw.ToArray(), count => Assert.Equal(0, count));
    }

    [Fact]
    public void Overdraw_WithSky_AddsExactlyOneWriteToTheUncoveredPixelsOnly()
    {
        var (bare, bareScene) = Slabs(3, nearestFirst: false, sky: false);
        bare.Render(bareScene, new GouraudPainter());

        var (skied, skyScene) = Slabs(3, nearestFirst: false, sky: true);
        skied.Render(skyScene, new GouraudPainter());

        Assert.Equal(0, CornerCount(bareScene));
        Assert.Equal(1, CornerCount(skyScene));

        Assert.Equal(3, CentreCount(bareScene));
        Assert.Equal(3, CentreCount(skyScene));
    }

    [Fact]
    public void Overdraw_WithAGizmo_AddsOnlyTheHandlePixels()
    {
        var (bare, bareScene) = Slabs(3, nearestFirst: false);
        bare.Render(bareScene, new GouraudPainter());
        var before = bareScene.Surface.Overdraw.ToArray();

        var (withGizmo, gizmoScene) = Slabs(3, nearestFirst: false);
        withGizmo.Settings.Gizmo = new TransformGizmo
        {
            Mode = GizmoMode.Translate,
            Target = gizmoScene.World.Meshes[0],
        };
        withGizmo.Render(gizmoScene, new GouraudPainter());
        var after = gizmoScene.Surface.Overdraw.ToArray();

        var raised = 0;
        var lowered = 0;

        for (var i = 0; i < before.Length; i++)
        {
            if (after[i] > before[i]) { raised++; }
            if (after[i] < before[i]) { lowered++; }
        }

        Assert.Equal(0, lowered);
        Assert.True(raised > 0, "the gizmo's own writes should be counted");
    }
}
