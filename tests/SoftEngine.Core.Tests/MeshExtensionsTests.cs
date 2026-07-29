using SoftEngine.Core.Geometry;
using System.Numerics;

namespace SoftEngine.Core.Tests;

/// <summary>
/// The two flat-array readers every importer funnels its geometry through. Both used to run
/// off the end of their input whenever its length did not divide evenly — which is what a
/// truncated <c>float_array</c>, a clipped <c>&lt;p&gt;</c> stream or a face list ending
/// mid-triangle produces — and an importer that throws there fails to open the whole model
/// over its last corner.
/// </summary>
public class MeshExtensionsTests
{
    [Fact]
    public void BuildTriangleIndices_GroupsIndicesInThrees()
    {
        var triangles = new[] { 0, 1, 2, 3, 4, 5 }.BuildTriangleIndices();

        Assert.Equal(2, triangles.Length);
        Assert.Equal((0, 1, 2), (triangles[0].I0, triangles[0].I1, triangles[0].I2));
        Assert.Equal((3, 4, 5), (triangles[1].I0, triangles[1].I1, triangles[1].I2));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void BuildTriangleIndices_TrailingPartialTriangle_IsDropped(int extra)
    {
        var indices = Enumerable.Range(0, 6 + extra).ToArray();

        var triangles = indices.BuildTriangleIndices();

        Assert.Equal(2, triangles.Length);
        Assert.Equal((3, 4, 5), (triangles[1].I0, triangles[1].I1, triangles[1].I2));
    }

    [Fact]
    public void BuildTriangleIndices_FewerThanThreeIndices_IsEmpty() =>
        Assert.Empty(new[] { 0, 1 }.BuildTriangleIndices());

    [Fact]
    public void BuildVector3s_GroupsFloatsInThrees() =>
        Assert.Equal(
            [new Vector3(1, 2, 3), new Vector3(4, 5, 6)],
            new[] { 1f, 2f, 3f, 4f, 5f, 6f }.BuildVector3s());

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void BuildVector3s_TrailingPartialVector_IsDropped(int extra)
    {
        var floats = Enumerable.Range(0, 6 + extra).Select(i => (float)i).ToArray();

        Assert.Equal(
            [new Vector3(0, 1, 2), new Vector3(3, 4, 5)],
            floats.BuildVector3s());
    }
}
