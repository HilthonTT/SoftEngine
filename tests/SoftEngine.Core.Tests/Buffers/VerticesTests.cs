using SoftEngine.Core.Buffers;
using System.Numerics;

namespace SoftEngine.Core.Tests;

public class VerticesTests
{
    [Fact]
    public void Lerp_AtZero_ReturnsFirstVertex()
    {
        var a = MakeVertex(1f);
        var b = MakeVertex(9f);

        var result = Vertices.Lerp(a, b, 0f);

        Assert.Equal(a.View, result.View);
        Assert.Equal(a.World, result.World);
        Assert.Equal(a.Norm, result.Norm);
        Assert.Equal(a.Proj, result.Proj);
    }

    [Fact]
    public void Lerp_AtOne_ReturnsSecondVertex()
    {
        var a = MakeVertex(1f);
        var b = MakeVertex(9f);

        var result = Vertices.Lerp(a, b, 1f);

        Assert.Equal(b.View, result.View);
        Assert.Equal(b.World, result.World);
        Assert.Equal(b.Norm, result.Norm);
        Assert.Equal(b.Proj, result.Proj);
    }

    [Fact]
    public void Lerp_AtHalf_InterpolatesEveryAttribute()
    {
        var a = MakeVertex(0f);
        var b = MakeVertex(10f);

        var result = Vertices.Lerp(a, b, 0.5f);

        Assert.Equal(new Vector3(5f, 5f, 5f), result.View);
        Assert.Equal(new Vector3(5f, 5f, 5f), result.World);
        Assert.Equal(new Vector3(5f, 5f, 5f), result.Norm);
        Assert.Equal(new Vector4(5f, 5f, 5f, 5f), result.Proj);
    }

    private static Vertices MakeVertex(float value) => new()
    {
        View = new Vector3(value),
        World = new Vector3(value),
        Norm = new Vector3(value),
        Proj = new Vector4(value),
    };
}
