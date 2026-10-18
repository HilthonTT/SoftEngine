using SoftEngine.Core.Pipeline.Clipping;
using System.Numerics;

namespace SoftEngine.Core.Tests.Pipeline;

public class LiangBarskyClippingTests
{
    private static readonly LiangBarskyClippingHomogeneous _clipper = new();

    private static Vector4 At(float x, float y, float z, float w) => new(x, y, z, w);

    [Fact]
    public void ALineWhollyInsideIsKeptAsItWas()
    {
        var p0 = At(-2f, 1f, 3f, 10f);
        var p1 = At(4f, -1f, 5f, 10f);

        var (a, b) = (p0, p1);

        Assert.True(_clipper.Clip(ref a, ref b));
        Assert.Equal(p0, a);
        Assert.Equal(p1, b);
    }

    [Fact]
    public void ALineWhollyOutsideIsRejected()
    {
        var p0 = At(12f, 0f, 3f, 10f);
        var p1 = At(20f, 0f, 3f, 10f);

        Assert.False(_clipper.Clip(ref p0, ref p1));
    }

    [Fact]
    public void ALineCrossingAPlaneIsShortenedToTheCrossing()
    {
        var p0 = At(0f, 0f, 3f, 10f);
        var p1 = At(20f, 0f, 3f, 10f);

        Assert.True(_clipper.Clip(ref p0, ref p1));

        Assert.Equal(0f, p0.X, 4);
        Assert.Equal(10f, p1.X, 4);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void AnEdgeExactlyParallelToAClipPlaneSurvives(int axis)
    {
        const float w = 10f;

        var p0 = At(1f, 2f, 3f, w);
        var p1 = axis switch
        {
            0 => At(4f, 2f, 3f, w),
            1 => At(1f, 5f, 3f, w),
            _ => At(1f, 2f, 6f, w),
        };

        var (a, b) = (p0, p1);

        Assert.True(_clipper.Clip(ref a, ref b), "an edge inside the frustum should survive clipping");

        Assert.Equal(p0, a);
        Assert.Equal(p1, b);
    }

    [Fact]
    public void AParallelEdgeOutsideThePlaneIsStillRejected()
    {
        var p0 = At(14f, 2f, 3f, 10f);
        var p1 = At(14f, 5f, 3f, 10f);

        Assert.False(_clipper.Clip(ref p0, ref p1));
    }

    [Fact]
    public void ALineBehindTheEyeIsRejected()
    {
        var p0 = At(1f, 1f, 1f, -5f);
        var p1 = At(2f, 2f, 2f, -8f);

        Assert.False(_clipper.Clip(ref p0, ref p1));
    }
}
