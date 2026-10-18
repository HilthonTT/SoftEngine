using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Scenes.Projections;

namespace SoftEngine.Core.Tests.Buffers;

public class NormalizedDepthTests
{
    [Fact]
    public void WriteNormalizedDepth_QuantizesToTheBuffersOwnScale()
    {
        var surface = new FrameBuffer(2, 2);
        surface.Clear();

        surface.WriteNormalizedDepth([0f, 0.25f, 0.5f, 1f]);

        Assert.Equal(0, surface.GetDepth(0, 0));
        Assert.Equal((int)(0.25f * FrameBuffer.DepthResolution), surface.GetDepth(1, 0));
        Assert.Equal((int)(0.5f * FrameBuffer.DepthResolution), surface.GetDepth(0, 1));

        Assert.Equal(FrameBuffer.DepthResolution, surface.GetDepth(1, 1));
    }

    [Fact]
    public void WriteNormalizedDepth_FarPlane_ReadsAsBackground()
    {
        var surface = new FrameBuffer(2, 1);
        surface.Clear();

        surface.WriteNormalizedDepth([0.5f, 1f]);

        Assert.False(surface.IsBackground(0, 0));
        Assert.True(surface.IsBackground(1, 0));
    }

    [Fact]
    public void WriteNormalizedDepth_OutOfRangeValues_AreClampedRatherThanWrapped()
    {
        var surface = new FrameBuffer(4, 1);
        surface.Clear();

        surface.WriteNormalizedDepth([-0.5f, 1.5f, float.NaN, 0.75f]);

        Assert.Equal(0, surface.GetDepth(0, 0));
        Assert.Equal(FrameBuffer.DepthResolution, surface.GetDepth(1, 0));
        Assert.Equal(FrameBuffer.DepthResolution, surface.GetDepth(2, 0));
        Assert.Equal((int)(0.75f * FrameBuffer.DepthResolution), surface.GetDepth(3, 0));
    }

    [Fact]
    public void WriteNormalizedDepth_TooFewValues_Throws()
    {
        var surface = new FrameBuffer(4, 4);

        Assert.Throws<ArgumentException>(() => surface.WriteNormalizedDepth(new float[8]));
    }

    [Fact]
    public void WriteNormalizedDepth_RoundTripsThroughReadViewDepth()
    {
        const float near = 0.5f;
        const float far = 200f;

        var surface = new FrameBuffer(3, 1);
        surface.Clear();
        surface.SetDepthRange(near, far);

        float[] distances = [1f, 25f, 150f];

        var scale = far / (far - near);
        var bias = far * near / (far - near);

        var normalized = new float[distances.Length];
        for (var i = 0; i < distances.Length; i++)
        {
            normalized[i] = scale - bias / distances[i];
        }

        surface.WriteNormalizedDepth(normalized);

        var recovered = new float[3];
        surface.ReadViewDepth(recovered);

        for (var i = 0; i < distances.Length; i++)
        {
            Assert.Equal(distances[i], recovered[i], distances[i] * 0.01f);
        }
    }

    [Fact]
    public void ToScreen3_ProducesTheSameNormalizedDepthWriteNormalizedDepthTakes()
    {
        var projection = new PerspectiveProjection(1f, 0.5f, 200f);

        var surface = new FrameBuffer(64, 64);
        surface.Clear();
        surface.SetDepthRange(projection.ZNear, projection.ZFar);

        var matrix = projection.ProjectionMatrix(64, 64);

        var clip = System.Numerics.Vector4.Transform(new System.Numerics.Vector3(0f, 0f, -30f), matrix);

        var screen = surface.ToScreen3(clip);
        var expected = screen.Z / FrameBuffer.DepthResolution;

        var other = new FrameBuffer(1, 1);
        other.Clear();
        other.SetDepthRange(projection.ZNear, projection.ZFar);
        other.WriteNormalizedDepth([expected]);

        Assert.Equal((int)screen.Z, other.GetDepth(0, 0), tolerance: 1);
    }

    [Fact]
    public void Clear_HighDynamicRange_ClearsTheFloatTargetAndTheDepth()
    {
        var surface = new FrameBuffer(8, 8);
        surface.SetHighDynamicRange(true);

        surface.PutPixel(3, 3, 100, new Core.Shading.LinearColor(4f, 5f, 6f));

        surface.Clear();

        Assert.Equal(0f, surface.HdrColor[(3 + 3 * 8) * 3]);
        Assert.Equal(FrameBuffer.DepthResolution, surface.GetDepth(3, 3));

        surface.ResolveToScreen();
        Assert.Equal(unchecked((int)0xFF000000), surface.GetColor(3, 3));
    }

    [Fact]
    public void Clear_StandardDynamicRange_ClearsTheScreen()
    {
        var surface = new FrameBuffer(8, 8);
        surface.Clear();

        surface.PutPixel(2, 2, 100, ColorRGB.Red);
        Assert.NotEqual(0, surface.GetColor(2, 2));

        surface.Clear();
        Assert.Equal(0, surface.GetColor(2, 2));
    }
}
