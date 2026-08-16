using SoftEngine.Core.Pipeline.Clipping;
using System.Numerics;

namespace SoftEngine.Core.Tests.Pipeline;

/// <summary>
/// The line clipper the wireframe painter and the pick highlight draw through. It works in
/// homogeneous clip space, so a point is inside when |x|, |y| and |z| are all within w.
/// </summary>
public class LiangBarskyClippingTests
{
    private static readonly LiangBarskyClippingHomogeneous _clipper = new();

    /// <summary>A point at the given clip-space position, at distance <paramref name="w"/>.</summary>
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
        // Both endpoints past the right-hand plane: x > w everywhere along it.
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

        // Still starts where it did, and now ends exactly on x = w.
        Assert.Equal(0f, p0.X, 4);
        Assert.Equal(10f, p1.X, 4);
    }

    /// <summary>
    /// The edges of a surface square-on to the camera: both endpoints at the same distance,
    /// so w does not change along the edge, and the edge runs along one axis so that
    /// coordinate does not change either.
    ///
    /// Two zero deltas make the clipper's divisor a <em>negative</em> zero, which compares as
    /// positive but divides to negative infinity. The two readings disagreed, and the line —
    /// entirely inside the frustum — was thrown away. It is worth a test of its own because
    /// nothing about the symptom pointed at clipping: a wall facing the camera simply had no
    /// wireframe and no pick outline, while everything at an angle to it did.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void AnEdgeExactlyParallelToAClipPlaneSurvives(int axis)
    {
        const float w = 10f;

        // A short edge near the middle of the frame, running along one axis only.
        var p0 = At(1f, 2f, 3f, w);
        var p1 = axis switch
        {
            0 => At(4f, 2f, 3f, w),
            1 => At(1f, 5f, 3f, w),
            _ => At(1f, 2f, 6f, w),
        };

        var (a, b) = (p0, p1);

        Assert.True(_clipper.Clip(ref a, ref b), "an edge inside the frustum should survive clipping");

        // Inside from end to end, so neither endpoint moves.
        Assert.Equal(p0, a);
        Assert.Equal(p1, b);
    }

    [Fact]
    public void AParallelEdgeOutsideThePlaneIsStillRejected()
    {
        // The other half of the same case: parallel to the plane, but on the wrong side of
        // it. Nothing about the fix should make an outside line inside.
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
