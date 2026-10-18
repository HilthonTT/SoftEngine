using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry.Primitives;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Cameras;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Scenes.Projections;
using System.Numerics;

namespace SoftEngine.Core.Tests.Buffers;

public class FrameBufferTests
{
    [Fact]
    public void PutPixel_EmptyBuffer_DrawsAndStoresColor()
    {
        var surface = new FrameBuffer(16, 16);
        surface.Clear();

        var drawn = surface.PutPixel(5, 5, 100, ColorRGB.Red);

        Assert.True(drawn);
        Assert.Equal(ColorRGB.Red.Color, surface.GetColor(5, 5));
        Assert.Equal(100, surface.GetDepth(5, 5));
    }

    [Fact]
    public void PutPixel_FartherThanExisting_IsRejected()
    {
        var surface = new FrameBuffer(16, 16);
        surface.Clear();
        surface.PutPixel(5, 5, 100, ColorRGB.Red);

        var drawn = surface.PutPixel(5, 5, 200, ColorRGB.Blue);

        Assert.False(drawn);
        Assert.Equal(ColorRGB.Red.Color, surface.GetColor(5, 5));
    }

    [Fact]
    public void PutPixel_NearerThanExisting_Overwrites()
    {
        var surface = new FrameBuffer(16, 16);
        surface.Clear();
        surface.PutPixel(5, 5, 200, ColorRGB.Red);

        var drawn = surface.PutPixel(5, 5, 100, ColorRGB.Blue);

        Assert.True(drawn);
        Assert.Equal(ColorRGB.Blue.Color, surface.GetColor(5, 5));
        Assert.Equal(100, surface.GetDepth(5, 5));
    }

    [Fact]
    public void Clear_ResetsColorAndDepth()
    {
        var surface = new FrameBuffer(16, 16);
        surface.Clear();
        surface.PutPixel(5, 5, 100, ColorRGB.Red);

        surface.Clear();

        Assert.Equal(0, surface.GetColor(5, 5));
        Assert.Equal(FrameBuffer.DepthResolution, surface.GetDepth(5, 5));
    }

    [Fact]
    public void ToScreen3_NdcOrigin_MapsToScreenCenter()
    {
        var surface = new FrameBuffer(101, 101);
        surface.SetDepthRange(1f, 100f);

        var screen = surface.ToScreen3(new Vector4(0, 0, 0.5f, 1f));

        Assert.Equal(50f, screen.X, 3);
        Assert.Equal(50f, screen.Y, 3);
    }

    [Fact]
    public void ToScreen3_NdcCorners_MapToScreenCorners()
    {
        var surface = new FrameBuffer(101, 101);
        surface.SetDepthRange(1f, 100f);

        var topLeft = surface.ToScreen3(new Vector4(-1, 1, 0.5f, 1f));
        var bottomRight = surface.ToScreen3(new Vector4(1, -1, 0.5f, 1f));

        Assert.Equal(0f, topLeft.X, 3);
        Assert.Equal(0f, topLeft.Y, 3);
        Assert.Equal(100f, bottomRight.X, 3);
        Assert.Equal(100f, bottomRight.Y, 3);
    }

    [Fact]
    public void ToScreen3_DepthIncreasesWithDistance()
    {
        var surface = new FrameBuffer(100, 100);
        surface.SetDepthRange(1f, 100f);

        var near = surface.ToScreen3(new Vector4(0, 0, 0, 1f));
        var far = surface.ToScreen3(new Vector4(0, 0, 50, 50f));

        Assert.True(near.Z < far.Z);
    }

    [Fact]
    public void SwitchingToHighDynamicRange_SizesTheFloatBufferImmediately()
    {
        var surface = new FrameBuffer(16, 9);

        Assert.Empty(surface.HdrColor);

        surface.SetHighDynamicRange(true);

        Assert.True(
            surface.HdrColor.Length >= 16 * 9 * 3,
            $"expected room for the frame, got {surface.HdrColor.Length} floats");
    }

    [Fact]
    public void ProbingTheFirstHdrFrameOfANewRenderTarget_DoesNotThrow()
    {
        foreach (var superSampling in new[] { 1, 2, 4 })
        {
            var world = new SimpleWorld();
            world.Meshes.Add(new Cube { Scale = new Vector3(3f, 3f, 3f) });
            world.Lights.Add(new DirectionalLight { Direction = new Vector3(0, -0.3f, 1f) });

            var scene = new Scene
            {
                World = world,
                Camera = new ProbeCamera(new Vector3(0, 0, -10f)),
                Projection = new PerspectiveProjection(MathF.PI / 4f, 0.5f, 100f),
                Surface = new FrameBuffer(37 * superSampling, 23 * superSampling) { Stats = new RenderStats() },
                HighDynamicRange = true,
            };

            var renderer = new Renderer();

            renderer.Diagnostics.SetProbe(
                36 * superSampling + superSampling / 2,
                22 * superSampling + superSampling / 2);

            renderer.Render(scene, new PhongPainter());

            Assert.NotNull(renderer.Diagnostics.PixelHistory);
        }
    }

    private sealed class ProbeCamera(Vector3 position) : ICamera
    {
        public Vector3 Position { get; set; } = position;

        public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Vector3.Zero, Vector3.UnitY);
    }
}
