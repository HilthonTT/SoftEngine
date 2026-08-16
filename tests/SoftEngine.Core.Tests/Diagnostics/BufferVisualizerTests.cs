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

namespace SoftEngine.Core.Tests;

public class BufferVisualizerTests
{
    private sealed class FixedCamera(Vector3 position) : ICamera
    {
        public Vector3 Position { get; set; } = position;

        public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Vector3.Zero, Vector3.UnitY);
    }

    private const int Size = 64;
    private const int Centre = Size / 2;

    /// <summary>
    /// A wall filling the middle of the frame and nothing else, so the centre pixel is
    /// always geometry and the corners are always background.
    /// </summary>
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

        // Auto-ranged over what is on screen: the wall is the nearest thing there is, so it
        // sits at the bright end of the ramp — and it is grey, not coloured.
        Assert.True(r > 200, $"expected a bright depth, got {r}");
        Assert.Equal(r, g);
        Assert.Equal(g, b);

        Assert.Equal((byte)0, Pixel(scene, 0, 0).R);
    }

    [Fact]
    public void DepthView_RampsAwayFromTheCamera()
    {
        // A floor stretching away from the camera: the far end of it must come out darker
        // than the near end, which is the only property the ramp actually promises.
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

        // Lower rows of the frame are nearer the camera on a floor seen from above.
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

        // The wall faces the eye: the view-space normal is roughly (0, 0, 1), which encodes
        // to a mid-grey red and green with a saturated blue.
        Assert.InRange(r, 100, 155);
        Assert.InRange(g, 100, 155);
        Assert.True(b > 230, $"expected the normal to point at the camera, got blue {b}");
    }

    [Fact]
    public void NormalsView_UnderAParallelProjection_LeavesTheImageAlone()
    {
        // There is no distance to recover from a parallel projection's depth, so there are no
        // positions to difference and no normals to show.
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
        // Two walls, one behind the other, both covering the centre of the frame: the pixel
        // there is written by whichever the fill reaches first and attempted by both.
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

        // In list order, so the far wall is drawn first and the near one is attempted over it.
        // Sorted nearest-first the second write never happens, which is the optimization
        // working and not what this test is about.
        renderer.Settings.NearestMeshesFirst = false;

        renderer.Render(scene, new GouraudPainter());

        Assert.True(scene.Surface.IsCountingOverdraw);

        var counts = scene.Surface.Overdraw;

        Assert.True(counts[Centre + Centre * Size] >= 2,
            $"the centre pixel should have been written more than once, got {counts[Centre + Centre * Size]}");

        Assert.Equal(0, counts[0]);

        // Background stays black; a written pixel gets a colour off the ramp.
        Assert.Equal((0, 0, 0), Pixel(scene, 0, 0));
        Assert.NotEqual((byte)0, Pixel(scene, Centre, Centre).G);
    }

    [Fact]
    public void ShadowMapView_NeedsAShadowMap()
    {
        var scene = Wall(shadows: false);

        RendererFor(DebugView.ShadowMap).Render(scene, new GouraudPainter());

        // No map was rendered, so the shaded image is left exactly as it was — the wall is
        // still lit rather than replaced by an empty buffer.
        Assert.Null(scene.ShadowMap);
        Assert.NotEqual(0, scene.Surface.GetColor(Centre, Centre) & 0x00FFFFFF);
    }

    [Fact]
    public void ShadowMapView_DrawsTheMapTheLightRendered()
    {
        var scene = Wall(shadows: true);

        RendererFor(DebugView.ShadowMap).Render(scene, new PhongPainter());

        Assert.NotNull(scene.ShadowMap);

        // The wall fills the light's view, so the middle of the map holds a real depth and
        // comes out grey rather than black.
        var (r, g, b) = Pixel(scene, Centre, Centre);

        Assert.True(r > 0, "the centre of the shadow map should hold a depth");
        Assert.Equal(r, g);
        Assert.Equal(g, b);
    }

    [Fact]
    public void ShadowMapView_DoesNotSpillTheMapIntoTheLetterboxingBesideIt()
    {
        // A map coarser than the square it is drawn into, in a frame wider than it is tall:
        // one texel covers several pixels, so the step from one pixel to the next is a
        // fraction of a texel, and truncating that fraction toward zero puts the pixel just
        // outside the square onto texel 0 instead of outside the map.
        var surface = new FrameBuffer(64, 32);

        // Every texel zero — nearer to the light than anything — so the map draws white and
        // is unmistakable against the surround.
        var map = new ShadowMap(8);

        Assert.True(new BufferVisualizer().Render(surface, projection: null, map, DebugView.ShadowMap));

        // 64 × 32: the square is the middle 32 columns, and fills the height.
        const int origin = (64 - 32) / 2;
        const int surround = 0x18181C;

        Assert.Equal(surround, surface.GetColor(origin - 1, 16) & 0x00FFFFFF);
        Assert.Equal(surround, surface.GetColor(origin + 32, 16) & 0x00FFFFFF);

        // …and the square itself is the map, right up to both of its edges.
        Assert.NotEqual(surround, surface.GetColor(origin, 16) & 0x00FFFFFF);
        Assert.NotEqual(surround, surface.GetColor(origin + 31, 16) & 0x00FFFFFF);
    }

    [Fact]
    public void OverdrawCounters_AreReadableAsSoonAsCountingIsSwitchedOn()
    {
        // Allocated when it is asked for rather than at the next clear, so a caller that
        // turns counting on outside the renderer's own order cannot walk off an empty array.
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
