using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Cameras;
using SoftEngine.Core.Scenes.Projections;
using System.Numerics;

namespace SoftEngine.Core.Tests.Pipeline;

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
        Assert.InRange(blended.R, 186, 190);
        Assert.InRange(blended.B, 186, 190);

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
        var near = MakeQuad(new Vector3(0, 0, 2), ColorRGB.Blue, 0.5f);
        var far = MakeQuad(Vector3.Zero, ColorRGB.Red, 0.5f);
        var (renderer, scene) = MakeScene(near, far);

        renderer.Render(scene, new ClassicPainter());

        var center = ColorRGB.FromPacked(scene.Surface.GetColor(64, 64));
        Assert.InRange(center.R, 135, 139);
        Assert.Equal(0, center.G);
        Assert.InRange(center.B, 186, 190);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Render_TransparentQuads_BlendBackToFront_EitherWay(bool orderIndependent)
    {
        var near = MakeQuad(new Vector3(0, 0, 2), ColorRGB.Blue, 0.5f);
        var far = MakeQuad(Vector3.Zero, ColorRGB.Red, 0.5f);
        var (renderer, scene) = MakeScene(near, far);

        renderer.Settings.OrderIndependentTransparency = orderIndependent;
        renderer.Render(scene, new ClassicPainter());

        var center = ColorRGB.FromPacked(scene.Surface.GetColor(64, 64));
        Assert.InRange(center.R, 135, 139);
        Assert.Equal(0, center.G);
        Assert.InRange(center.B, 186, 190);
    }

    [Fact]
    public void Render_IntersectingTransparentQuads_AreOnlyCorrectPerPixel()
    {
        static Mesh Slanted(ColorRGB color, float nearLeft, float nearRight)
        {
            Vector3[] vertices =
            [
                new(-1, -1, nearLeft), new(1, -1, nearRight), new(1, 1, nearRight), new(-1, 1, nearLeft),
            ];
            Triangle[] triangles = [new(0, 1, 2), new(2, 3, 0)];

            return new Mesh(vertices, triangles, null, [color, color]) { Opacity = 0.5f };
        }

        const int left = 45;
        const int right = 83;
        const int row = 64;

        var (sortedRenderer, sortedScene) = MakeScene(
            Slanted(ColorRGB.Red, 1f, -1f), Slanted(ColorRGB.Blue, -1f, 1f));

        sortedRenderer.Settings.OrderIndependentTransparency = false;
        sortedRenderer.Render(sortedScene, new ClassicPainter());

        var (oitRenderer, oitScene) = MakeScene(
            Slanted(ColorRGB.Red, 1f, -1f), Slanted(ColorRGB.Blue, -1f, 1f));

        oitRenderer.Settings.OrderIndependentTransparency = true;
        oitRenderer.Render(oitScene, new ClassicPainter());

        var oitLeft = ColorRGB.FromPacked(oitScene.Surface.GetColor(left, row));
        var oitRight = ColorRGB.FromPacked(oitScene.Surface.GetColor(right, row));

        Assert.NotEqual(oitLeft.Color, oitRight.Color);
        Assert.True(oitLeft.R > oitLeft.B, $"left should lead with red, got {oitLeft.R}/{oitLeft.B}");
        Assert.True(oitRight.B > oitRight.R, $"right should lead with blue, got {oitRight.R}/{oitRight.B}");

        Assert.NotEqual(sortedScene.Surface.Screen, oitScene.Surface.Screen);
    }

    [Fact]
    public void Render_OrderIndependent_IsUnchangedByDrawOrder()
    {
        static int[] Render(bool nearFirst)
        {
            var near = MakeQuad(new Vector3(0, 0, 2), ColorRGB.Blue, 0.5f);
            var far = MakeQuad(Vector3.Zero, ColorRGB.Red, 0.5f);

            var (renderer, scene) = MakeScene(nearFirst ? [near, far] : [far, near]);
            renderer.Settings.OrderIndependentTransparency = true;
            renderer.Render(scene, new ClassicPainter());

            return (int[])scene.Surface.Screen.Clone();
        }

        Assert.Equal(Render(nearFirst: true), Render(nearFirst: false));
    }

    [Fact]
    public void Render_OrderIndependent_TransparentBehindOpaque_IsStillHiddenByTheDepthTest()
    {
        var opaque = MakeQuad(new Vector3(0, 0, 2), ColorRGB.Red);
        var hidden = MakeQuad(Vector3.Zero, ColorRGB.Blue, 0.5f);
        var (renderer, scene) = MakeScene(opaque, hidden);

        renderer.Settings.OrderIndependentTransparency = true;
        renderer.Render(scene, new ClassicPainter());

        Assert.Equal(ColorRGB.Red.Color, scene.Surface.GetColor(64, 64));
        Assert.Equal(0, renderer.Stats.TransparentFragmentCount);
    }

    [Fact]
    public void Render_OrderIndependent_ReportsWhatItStored()
    {
        var (renderer, scene) = MakeScene(
            MakeQuad(new Vector3(0, 0, 2), ColorRGB.Blue, 0.5f),
            MakeQuad(Vector3.Zero, ColorRGB.Red, 0.5f));

        renderer.Settings.OrderIndependentTransparency = true;
        renderer.Render(scene, new ClassicPainter());

        var pixels = renderer.Stats.TransparentPixelCount;
        var fragments = renderer.Stats.TransparentFragmentCount;

        Assert.True(pixels > 0);
        Assert.InRange(fragments, pixels + 1, pixels * 2);
        Assert.Equal(0, renderer.Stats.TransparentOverflowCount);
    }

    [Fact]
    public void Render_OrderIndependent_TooLittleToParallelize_StillResolves()
    {
        var near = MakeQuad(Vector3.Zero, ColorRGB.Blue, 0.5f);
        var far = MakeQuad(new Vector3(0f, 0f, -0.5f), ColorRGB.Red, 0.5f);

        near.Scale = new Vector3(0.15f);
        far.Scale = new Vector3(0.15f);

        var (renderer, scene) = MakeScene(near, far);
        renderer.Settings.OrderIndependentTransparency = true;
        renderer.Render(scene, new ClassicPainter());

        Assert.True(renderer.Stats.TransparentFragmentCount > 0);

        var center = ColorRGB.FromPacked(scene.Surface.GetColor(64, 64));
        Assert.InRange(center.R, 135, 139);
        Assert.InRange(center.B, 186, 190);
    }

    [Fact]
    public void Render_MoreFragmentsThanCapacity_MergesTheFarthestAndSaysSo()
    {
        static (Renderer Renderer, Scene Scene) Panes(int capacity)
        {
            var meshes = new Mesh[6];
            for (var i = 0; i < meshes.Length; i++)
            {
                meshes[i] = MakeQuad(new Vector3(0, 0, -i * 0.25f), ColorRGB.Blue, 0.4f);
            }

            var (renderer, scene) = MakeScene(meshes);
            renderer.Settings.OrderIndependentTransparency = true;
            renderer.Fragments.Capacity = capacity;
            renderer.Render(scene, new ClassicPainter());

            return (renderer, scene);
        }

        var (exact, exactScene) = Panes(capacity: 8);
        var (capped, cappedScene) = Panes(capacity: 3);

        Assert.Equal(0, exact.Stats.TransparentOverflowCount);
        Assert.True(capped.Stats.TransparentOverflowCount > 0);

        var exactCenter = ColorRGB.FromPacked(exactScene.Surface.GetColor(64, 64));
        var cappedCenter = ColorRGB.FromPacked(cappedScene.Surface.GetColor(64, 64));

        Assert.InRange(cappedCenter.B, exactCenter.B - 2, exactCenter.B + 2);
    }

    [Fact]
    public void Probe_OrderIndependent_RecordsEachFragmentFarthestFirst()
    {
        var near = MakeQuad(new Vector3(0, 0, 2), ColorRGB.Blue, 0.5f);
        var far = MakeQuad(Vector3.Zero, ColorRGB.Red, 0.5f);
        var (renderer, scene) = MakeScene(near, far);

        renderer.Settings.OrderIndependentTransparency = true;
        renderer.Diagnostics.SetProbe(64, 64);
        renderer.Render(scene, new ClassicPainter());

        var history = renderer.Diagnostics.PixelHistory;
        Assert.NotNull(history);

        var fragments = history.Writes
            .Where(w => w.Source == PixelWriteSource.TransparentFragment)
            .ToList();

        Assert.Equal(2, fragments.Count);

        Assert.True(fragments[0].Depth > fragments[1].Depth);
        Assert.NotEqual(fragments[0].ObjectId, fragments[1].ObjectId);
        Assert.All(fragments, f => Assert.True(f.Passed));

        var center = ColorRGB.FromPacked(scene.Surface.GetColor(64, 64));
        Assert.InRange(center.R, 135, 139);
        Assert.InRange(center.B, 186, 190);
    }

    [Fact]
    public void Probe_OrderIndependent_RecordsFragmentsTheDepthTestRejected()
    {
        var opaque = MakeQuad(new Vector3(0, 0, 2), ColorRGB.Red);
        var hidden = MakeQuad(Vector3.Zero, ColorRGB.Blue, 0.5f);
        var (renderer, scene) = MakeScene(opaque, hidden);

        renderer.Settings.OrderIndependentTransparency = true;
        renderer.Diagnostics.SetProbe(64, 64);
        renderer.Render(scene, new ClassicPainter());

        var history = renderer.Diagnostics.PixelHistory;
        Assert.NotNull(history);

        Assert.Contains(history.Writes, w => w.Source == PixelWriteSource.Triangle && !w.Passed);
        Assert.DoesNotContain(history.Writes, w => w.Source == PixelWriteSource.TransparentFragment);
        Assert.Equal(ColorRGB.Red.Color, scene.Surface.GetColor(64, 64));
    }

    [Fact]
    public void Render_OneFragmentPerPixel_MergesRatherThanDropping()
    {
        var (renderer, scene) = MakeScene(
            MakeQuad(new Vector3(0, 0, 2), ColorRGB.Blue, 0.5f),
            MakeQuad(Vector3.Zero, ColorRGB.Red, 0.5f));

        renderer.Settings.OrderIndependentTransparency = true;
        renderer.Fragments.Capacity = 1;
        renderer.Render(scene, new ClassicPainter());

        Assert.True(renderer.Stats.TransparentOverflowCount > 0);

        var center = ColorRGB.FromPacked(scene.Surface.GetColor(64, 64));
        Assert.InRange(center.R, 135, 139);
        Assert.InRange(center.B, 186, 190);
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
