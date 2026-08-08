using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Pipeline.Debugging;
using SoftEngine.Core.Rasterization;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Cameras;
using SoftEngine.Core.Scenes.Projections;
using SoftEngine.Core.Textures;
using System.Numerics;

namespace SoftEngine.Core.Tests;

/// <summary>
/// The mip-level buffer view: which level of a texture's chain each pixel was sampled from.
///
/// It is the one view whose buffer the frame does not otherwise keep — the level is chosen per
/// triangle inside the painter, used for one fill and discarded — so most of what is asserted
/// here is that recording it is opt-in, costs nothing when nobody is looking, and reports
/// "untextured" and "level 0" as different answers.
/// </summary>
public class MipLevelViewTests
{
    private sealed class FixedCamera(Vector3 position) : ICamera
    {
        public Vector3 Position { get; set; } = position;

        public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Vector3.Zero, Vector3.UnitY);
    }

    private const int Size = 64;
    private const int Centre = Size / 2;

    /// <summary>A camera-facing quad with UVs over the whole texture.</summary>
    private static Mesh Quad(Texture? texture, float scale = 1f)
    {
        Vector3[] vertices = [new(-scale, -scale, 0), new(scale, -scale, 0), new(scale, scale, 0), new(-scale, scale, 0)];
        Triangle[] triangles = [new(0, 1, 2), new(2, 3, 0)];
        Vector3[] normals = [-Vector3.UnitZ, -Vector3.UnitZ, -Vector3.UnitZ, -Vector3.UnitZ];

        var mesh = new Mesh(vertices, triangles, normals, [ColorRGB.White, ColorRGB.White]);

        if (texture is not null)
        {
            mesh.TexCoords = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];
            mesh.Texture = texture;
        }

        return mesh;
    }

    private static Scene SceneWith(Mesh mesh, float distance = 5f) =>
        new()
        {
            World = new SimpleWorld { Meshes = [mesh], Lights = [] },
            Camera = new FixedCamera(new Vector3(0, 0, distance)),
            Projection = new PerspectiveProjection(MathF.PI / 4f, 0.5f, 1000f),
            Surface = new FrameBuffer(Size, Size) { Stats = new RenderStats() },
        };

    private static (byte R, byte G, byte B) Pixel(Scene scene, int x, int y)
    {
        var packed = scene.Surface.GetColor(x, y);

        return ((byte)((packed >> 16) & 0xFF), (byte)((packed >> 8) & 0xFF), (byte)(packed & 0xFF));
    }

    #region The buffer

    [Fact]
    public void MipLevels_WhenNotRecording_IsEmpty()
    {
        var surface = new FrameBuffer(8, 8);
        surface.Clear();

        Assert.False(surface.IsRecordingMipLevels);
        Assert.True(surface.MipLevels.IsEmpty);

        // And a write while nothing is listening is simply dropped, rather than throwing on a
        // buffer that was never allocated.
        surface.RecordMipLevel(1, 1, 3);
    }

    /// <summary>
    /// The cleared value is -1, not 0. "Nothing textured here" and "sampled the full-resolution
    /// image" are different answers, and a zeroed buffer would give them the same colour —
    /// painting every background pixel in the frame as level 0.
    /// </summary>
    [Fact]
    public void MipLevels_Cleared_AreMinusOneRatherThanZero()
    {
        var surface = new FrameBuffer(8, 8);
        surface.SetMipLevelRecording(true);
        surface.Clear();

        Assert.True(surface.IsRecordingMipLevels);

        foreach (var level in surface.MipLevels)
        {
            Assert.Equal(-1, level);
        }
    }

    [Fact]
    public void RecordMipLevel_WritesTheLevelAtThePixel()
    {
        var surface = new FrameBuffer(8, 8);
        surface.SetMipLevelRecording(true);
        surface.Clear();

        surface.RecordMipLevel(3, 2, 4);

        Assert.Equal(4, surface.MipLevels[3 + 2 * 8]);
        Assert.Equal(-1, surface.MipLevels[0]);
    }

    #endregion

    #region The per-triangle state

    [Fact]
    public void RasterState_ByDefault_ReportsNoMipLevel() =>
        Assert.Equal(-1, default(RasterState).MipLevel);

    [Fact]
    public void RasterState_WithMipLevel_RoundTrips()
    {
        Assert.Equal(0, default(RasterState).WithMipLevel(0).MipLevel);
        Assert.Equal(7, default(RasterState).WithMipLevel(7).MipLevel);
    }

    /// <summary>
    /// The two are set from opposite ends — opacity per mesh, the level per triangle — so
    /// neither may drop the other.
    /// </summary>
    [Fact]
    public void RasterState_OpacityAndMipLevel_Compose()
    {
        var state = default(RasterState).WithMipLevel(3).WithOpacity(0.5f);

        Assert.Equal(3, state.MipLevel);
        Assert.Equal(0.5f, state.Alpha, 3);
        Assert.False(state.IsOpaque);
    }

    #endregion

    #region The view

    [Fact]
    public void MipLevelView_WithTheViewClosed_RecordsNothing()
    {
        var scene = SceneWith(Quad(Texture.Checkerboard(64, 8, ColorRGB.White, ColorRGB.Black)));

        new Renderer { Settings = new RendererSettings { DebugView = DebugView.Off } }
            .Render(scene, new TexturedPainter());

        Assert.True(scene.Surface.MipLevels.IsEmpty);
    }

    [Fact]
    public void MipLevelView_OnATexturedSurface_ColoursTheLevelItSampled()
    {
        var scene = SceneWith(Quad(Texture.Checkerboard(64, 8, ColorRGB.White, ColorRGB.Black)));

        var renderer = new Renderer
        {
            Settings = new RendererSettings { DebugView = DebugView.MipLevel, BackFaceCulling = false },
        };

        renderer.Render(scene, new TexturedPainter { UseMipMaps = true });

        var level = scene.Surface.MipLevels[Centre + Centre * Size];
        Assert.True(level >= 0, "a textured pixel should carry the level it sampled");

        // Whatever the level, the pixel is a saturated tint and the background is black.
        var (r, g, b) = Pixel(scene, Centre, Centre);
        Assert.True(r + g + b > 120, $"expected a mip tint at the centre, got {r},{g},{b}");

        Assert.Equal((0, 0, 0), Pixel(scene, 1, 1));
    }

    /// <summary>
    /// Untextured geometry is neither background nor a level: the painter made no mip decision
    /// there, and colouring it as level 0 would fill most scenes in this engine with a
    /// confident red.
    /// </summary>
    [Fact]
    public void MipLevelView_OnUntexturedGeometry_IsGreyRatherThanLevelZero()
    {
        var scene = SceneWith(Quad(texture: null));

        var renderer = new Renderer
        {
            Settings = new RendererSettings { DebugView = DebugView.MipLevel, BackFaceCulling = false },
        };

        renderer.Render(scene, new TexturedPainter());

        Assert.Equal(-1, scene.Surface.MipLevels[Centre + Centre * Size]);

        var (r, g, b) = Pixel(scene, Centre, Centre);

        Assert.Equal(r, g);
        Assert.True(r is > 20 and < 90, $"expected the untextured grey, got {r},{g},{b}");

        // Still distinct from the background, which is what the grey is for.
        Assert.Equal((0, 0, 0), Pixel(scene, 1, 1));
    }

    /// <summary>
    /// The reading the view exists for: the same texture on the same quad picks a coarser level
    /// as it shrinks on screen. If it did not, the view would be a picture of a constant.
    /// </summary>
    [Fact]
    public void MipLevelView_FartherGeometry_SamplesACoarserLevel()
    {
        var texture = Texture.Checkerboard(64, 8, ColorRGB.White, ColorRGB.Black);

        Assert.True(Level(texture, distance: 60f) > Level(texture, distance: 5f));
    }

    private static int Level(Texture texture, float distance)
    {
        var scene = SceneWith(Quad(texture), distance);

        var renderer = new Renderer
        {
            Settings = new RendererSettings { DebugView = DebugView.MipLevel, BackFaceCulling = false },
        };

        renderer.Render(scene, new TexturedPainter { UseMipMaps = true });

        var level = scene.Surface.MipLevels[Centre + Centre * Size];

        Assert.True(level >= 0, $"nothing textured was drawn at the centre from {distance} units away");

        return level;
    }

    #endregion
}
