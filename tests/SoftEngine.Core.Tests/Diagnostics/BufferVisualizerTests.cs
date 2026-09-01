using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry.Primitives;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Pipeline.Debugging;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Cameras;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Scenes.Projections;
using SoftEngine.Core.Shading;
using System.Numerics;

namespace SoftEngine.Core.Tests.Diagnostics;

public class BufferVisualizerTests
{
    private sealed class FixedCamera(Vector3 position) : ICamera
    {
        public Vector3 Position { get; set; } = position;

        public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Vector3.Zero, Vector3.UnitY);
    }

    private const int Size = 64;
    private const int Centre = Size / 2;

    private static Scene Wall(bool shadows = false)
    {
        var world = new SimpleWorld();
        world.Meshes.Add(new Cube { Scale = new Vector3(4f, 4f, 0.5f) });
        world.Lights.Add(new DirectionalLight { Direction = new Vector3(-0.2f, -0.4f, 1f) });

        return new Scene
        {
            World = world,
            Camera = new FixedCamera(new Vector3(0, 0, -12f)),
            Projection = new PerspectiveProjection(MathF.PI / 4f, 0.5f, 100f),
            Surface = new FrameBuffer(Size, Size) { Stats = new RenderStats() },
            GammaCorrect = true,
            Shadows = new ShadowSettings { Enabled = shadows },
        };
    }

    private static (byte R, byte G, byte B) Pixel(Scene scene, int x, int y)
    {
        var packed = scene.Surface.GetColor(x, y);

        return ((byte)((packed >> 16) & 0xFF), (byte)((packed >> 8) & 0xFF), (byte)(packed & 0xFF));
    }

    private static Renderer RendererFor(DebugView view) =>
        new() { Settings = new RendererSettings { BackFaceCulling = true, DebugView = view } };

    [Fact]
    public void DepthView_IsBrightOnGeometryAndBlackOnBackground()
    {
        var scene = Wall();

        RendererFor(DebugView.Depth).Render(scene, new GouraudPainter());

        var (r, g, b) = Pixel(scene, Centre, Centre);

        Assert.True(r > 200, $"expected a bright depth, got {r}");
        Assert.Equal(r, g);
        Assert.Equal(g, b);

        Assert.Equal((byte)0, Pixel(scene, 0, 0).R);
    }

    [Fact]
    public void DepthView_RampsAwayFromTheCamera()
    {
        var world = new SimpleWorld();
        world.Meshes.Add(new Cube { Position = new Vector3(0, -2f, 0), Scale = new Vector3(6f, 0.2f, 40f) });
        world.Lights.Add(new DirectionalLight { Direction = new Vector3(0, -1f, 0.2f) });

        var scene = new Scene
        {
            World = world,
            Camera = new FixedCamera(new Vector3(0, 2f, -10f)),
            Projection = new PerspectiveProjection(MathF.PI / 4f, 0.5f, 200f),
            Surface = new FrameBuffer(Size, Size) { Stats = new RenderStats() },
        };

        RendererFor(DebugView.Depth).Render(scene, new GouraudPainter());

        var near = Pixel(scene, Centre, Size - 4).R;
        var far = Pixel(scene, Centre, Centre + 2).R;

        Assert.True(near > far, $"near {near} should be brighter than far {far}");
    }

    [Fact]
    public void NormalsView_ShowsASurfaceFacingTheCameraAsFacingTheCamera()
    {
        var scene = Wall();

        RendererFor(DebugView.Normals).Render(scene, new GouraudPainter());

        var (r, g, b) = Pixel(scene, Centre, Centre);

        Assert.InRange(r, 100, 155);
        Assert.InRange(g, 100, 155);
        Assert.True(b > 230, $"expected the normal to point at the camera, got blue {b}");
    }

    [Fact]
    public void NormalsView_UnderAParallelProjection_LeavesTheImageAlone()
    {
        var surface = new FrameBuffer(8, 8);
        surface.SetLinearDepthRange();
        surface.Clear();
        surface.Screen[0] = unchecked((int)0xFF123456);

        var drawn = new BufferVisualizer().Render(
            surface,
            new OrthographicProjection(10f, 0.1f, 100f),
            shadowMap: null,
            DebugView.Normals);

        Assert.False(drawn);
        Assert.Equal(unchecked((int)0xFF123456), surface.Screen[0]);
    }

    [Fact]
    public void OverdrawCounting_IsOffUntilTheViewAsksForIt()
    {
        var scene = Wall();

        new Renderer().Render(scene, new GouraudPainter());

        Assert.False(scene.Surface.IsCountingOverdraw);
        Assert.True(scene.Surface.Overdraw.IsEmpty);
    }

    [Fact]
    public void OverdrawView_CountsTheWritesEachPixelTook()
    {
        var world = new SimpleWorld();
        world.Meshes.Add(new Cube { Position = new Vector3(0, 0, 2f), Scale = new Vector3(4f, 4f, 0.5f) });
        world.Meshes.Add(new Cube { Position = new Vector3(0, 0, 0f), Scale = new Vector3(4f, 4f, 0.5f) });
        world.Lights.Add(new DirectionalLight { Direction = new Vector3(0, 0, 1f) });

        var scene = new Scene
        {
            World = world,
            Camera = new FixedCamera(new Vector3(0, 0, -12f)),
            Projection = new PerspectiveProjection(MathF.PI / 4f, 0.5f, 100f),
            Surface = new FrameBuffer(Size, Size) { Stats = new RenderStats() },
        };

        var renderer = RendererFor(DebugView.Overdraw);

        renderer.Settings.NearestMeshesFirst = false;

        renderer.Render(scene, new GouraudPainter());

        Assert.True(scene.Surface.IsCountingOverdraw);

        var counts = scene.Surface.Overdraw;

        Assert.True(counts[Centre + Centre * Size] >= 2,
            $"the centre pixel should have been written more than once, got {counts[Centre + Centre * Size]}");

        Assert.Equal(0, counts[0]);

        Assert.Equal((0, 0, 0), Pixel(scene, 0, 0));
        Assert.NotEqual((byte)0, Pixel(scene, Centre, Centre).G);
    }

    [Fact]
    public void ShadowMapView_NeedsAShadowMap()
    {
        var scene = Wall(shadows: false);

        RendererFor(DebugView.ShadowMap).Render(scene, new GouraudPainter());

        Assert.Null(scene.ShadowMap);
        Assert.NotEqual(0, scene.Surface.GetColor(Centre, Centre) & 0x00FFFFFF);
    }

    [Fact]
    public void ShadowMapView_DrawsTheMapTheLightRendered()
    {
        var scene = Wall(shadows: true);

        RendererFor(DebugView.ShadowMap).Render(scene, new PhongPainter());

        Assert.NotNull(scene.ShadowMap);

        var (r, g, b) = Pixel(scene, Centre, Centre);

        Assert.True(r > 0, "the centre of the shadow map should hold a depth");
        Assert.Equal(r, g);
        Assert.Equal(g, b);
    }

    [Fact]
    public void ShadowMapView_DoesNotSpillTheMapIntoTheLetterboxingBesideIt()
    {
        var surface = new FrameBuffer(64, 32);

        var map = new ShadowMap(8);

        Assert.True(new BufferVisualizer().Render(surface, projection: null, map, DebugView.ShadowMap));

        const int origin = (64 - 32) / 2;
        const int surround = 0x18181C;

        Assert.Equal(surround, surface.GetColor(origin - 1, 16) & 0x00FFFFFF);
        Assert.Equal(surround, surface.GetColor(origin + 32, 16) & 0x00FFFFFF);

        Assert.NotEqual(surround, surface.GetColor(origin, 16) & 0x00FFFFFF);
        Assert.NotEqual(surround, surface.GetColor(origin + 31, 16) & 0x00FFFFFF);
    }

    [Fact]
    public void OverdrawCounters_AreReadableAsSoonAsCountingIsSwitchedOn()
    {
        var surface = new FrameBuffer(16, 16);

        surface.SetOverdrawCounting(true);

        Assert.True(surface.IsCountingOverdraw);
        Assert.Equal(16 * 16, surface.Overdraw.Length);

        surface.SetOverdrawCounting(false);

        Assert.True(surface.Overdraw.IsEmpty);
    }

    [Fact]
    public void DebugViewOff_LeavesTheShadedFrameUntouched()
    {
        var shaded = Wall();
        var same = Wall();

        new Renderer().Render(shaded, new GouraudPainter());
        RendererFor(DebugView.Off).Render(same, new GouraudPainter());

        Assert.Equal(shaded.Surface.GetColor(Centre, Centre), same.Surface.GetColor(Centre, Centre));
    }
}
