using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Cameras;
using SoftEngine.Core.Scenes.Projections;
using System.Numerics;

namespace SoftEngine.Core.Tests;

public class TransparencyTests
{
    private sealed class FixedCamera(Vector3 position) : ICamera
    {
        public Vector3 Position { get; set; } = position;

        public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Vector3.Zero, Vector3.UnitY);
    }

    private static (Renderer Renderer, Scene Scene) MakeScene(params Mesh[] meshes)
    {
        var renderer = new Renderer();
        var surface = new FrameBuffer(128, 128) { Stats = renderer.Stats };

        // The test quads are single-sided; culling would silently drop the ones
        // wound the "wrong" way and every expected colour with them.
        renderer.Settings.BackFaceCulling = false;

        var scene = new Scene
        {
            Surface = surface,
            Camera = new FixedCamera(new Vector3(0, 0, 5)),
            Projection = new PerspectiveProjection(MathF.PI / 4f, 1f, 100f),
            World = new SimpleWorld { Meshes = [.. meshes], Lights = [] },
        };

        return (renderer, scene);
    }

    /// <summary>
    /// A camera-facing 2x2 quad with its own colour array — unlike <c>Cube</c>, whose
    /// instances share one static <c>TriangleColors</c> array.
    /// </summary>
    private static Mesh MakeQuad(Vector3 position, ColorRGB color, float opacity = 1f)
    {
        Vector3[] vertices = [new(-1, -1, 0), new(1, -1, 0), new(1, 1, 0), new(-1, 1, 0)];
        Triangle[] triangles = [new(0, 1, 2), new(2, 3, 0)];

        return new Mesh(vertices, triangles, null, [color, color])
        {
            Position = position,
            Opacity = opacity,
        };
    }

    [Fact]
    public void PutPixelBlend_BlendsWithoutWritingDepth()
    {
        var surface = new FrameBuffer(4, 4);
        surface.SetDepthRange(1f, 100f);
        surface.Clear();

        surface.PutPixel(1, 1, 100, ColorRGB.Red);

        Assert.True(surface.PutPixelBlend(1, 1, 50, ColorRGB.Blue, 0.5f));

        var blended = ColorRGB.FromPacked(surface.GetColor(1, 1));
        Assert.InRange(blended.R, 126, 128);
        Assert.InRange(blended.B, 126, 128);

        // The opaque write's depth must survive the blend.
        Assert.Equal(100, surface.GetDepth(1, 1));
    }

    [Fact]
    public void PutPixelBlend_BehindTheDepthBuffer_IsRejected()
    {
        var surface = new FrameBuffer(4, 4);
        surface.SetDepthRange(1f, 100f);
        surface.Clear();

        surface.PutPixel(1, 1, 100, ColorRGB.Red);

        Assert.False(surface.PutPixelBlend(1, 1, 200, ColorRGB.Blue, 0.5f));
        Assert.Equal(ColorRGB.Red.Color, surface.GetColor(1, 1));
    }

    [Fact]
    public void Render_TransparentCubes_BlendBackToFront()
    {
        // The near cube is listed first, so a correct result requires the renderer
        // to sort the transparent triangles farthest-first before blending.
        var near = MakeQuad(new Vector3(0, 0, 2), ColorRGB.Blue, 0.5f);
        var far = MakeQuad(Vector3.Zero, ColorRGB.Red, 0.5f);
        var (renderer, scene) = MakeScene(near, far);

        renderer.Render(scene, new ClassicPainter());

        // Black background, then 50% red, then 50% blue over that: (63, 0, 127).
        var center = ColorRGB.FromPacked(scene.Surface.GetColor(64, 64));
        Assert.InRange(center.R, 62, 65);
        Assert.Equal(0, center.G);
        Assert.InRange(center.B, 126, 128);
    }

    [Fact]
    public void Render_TransparentMesh_LeavesDepthBufferUntouched()
    {
        var (renderer, scene) = MakeScene(MakeQuad(Vector3.Zero, ColorRGB.Blue, 0.5f));

        renderer.Render(scene, new ClassicPainter());

        Assert.True(renderer.Stats.DrawnPixelCount > 0);
        Assert.Equal(FrameBuffer.DepthResolution, scene.Surface.GetDepth(64, 64));
    }

    [Fact]
    public void Render_TransparentBehindOpaque_IsHiddenByTheDepthTest()
    {
        var opaque = MakeQuad(new Vector3(0, 0, 2), ColorRGB.Red);
        var hidden = MakeQuad(Vector3.Zero, ColorRGB.Blue, 0.5f);
        var (renderer, scene) = MakeScene(opaque, hidden);

        renderer.Render(scene, new ClassicPainter());

        Assert.Equal(ColorRGB.Red.Color, scene.Surface.GetColor(64, 64));
    }

    [Fact]
    public void Render_FullyTransparentMesh_IsSkipped()
    {
        var (renderer, scene) = MakeScene(MakeQuad(Vector3.Zero, ColorRGB.Blue, 0f));

        renderer.Render(scene, new ClassicPainter());

        Assert.Equal(0, renderer.Stats.DrawnPixelCount);
        Assert.Equal(0, renderer.Stats.DrawnTriangleCount);
    }

    [Fact]
    public void Render_TransparentCubes_WorkWithEveryPainter()
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
            var near = MakeQuad(new Vector3(0, 0, 2), ColorRGB.Blue, 0.5f);
            var far = MakeQuad(Vector3.Zero, ColorRGB.Red);
            var (renderer, scene) = MakeScene(near, far);

            renderer.Render(scene, painter());

            Assert.True(renderer.Stats.DrawnPixelCount > 0);
        }
    }
}

