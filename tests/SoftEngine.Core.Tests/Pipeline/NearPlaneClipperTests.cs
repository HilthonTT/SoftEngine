using SoftEngine.Core.Buffers;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Pipeline.Clipping;
using System.Numerics;

namespace SoftEngine.Core.Tests;

public class NearPlaneClipperTests
{
    private static VertexBuffer MakeBuffer(Vector4 proj0, Vector4 proj1, Vector4 proj2, Vector2[]? texCoords = null)
    {
        var mesh = new Mesh(
            [new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0)],
            [new Triangle(0, 1, 2)])
        {
            TexCoords = texCoords
        };

        var vbx = new VertexBuffer(3) { Mesh = mesh };
        vbx.Vertices[0] = new Vertices { Proj = proj0, World = new Vector3(0, 0, 0), Norm = Vector3.UnitZ };
        vbx.Vertices[1] = new Vertices { Proj = proj1, World = new Vector3(1, 0, 0), Norm = Vector3.UnitZ };
        vbx.Vertices[2] = new Vertices { Proj = proj2, World = new Vector3(0, 1, 0), Norm = Vector3.UnitZ };
        return vbx;
    }

    [Fact]
    public void Clip_OneVertexInFront_ProducesOneTriangle()
    {
        using var vbx = MakeBuffer(
            new Vector4(0, 0, 1, 2),
            new Vector4(0, 0, -1, 1),
            new Vector4(0, 0, -1, 1));
        var visible = new List<(int, int)>();

        var added = NearPlaneClipper.Clip(vbx, vbx.Mesh!.Triangles[0], 0, 0, visible);

        Assert.Equal(1, added);
        Assert.Single(visible);
    }

    [Fact]
    public void Clip_TwoVerticesInFront_ProducesTwoTriangles()
    {
        using var vbx = MakeBuffer(
            new Vector4(0, 0, 1, 2),
            new Vector4(0.1f, 0, 1, 2),
            new Vector4(0, 0.1f, -1, 1));
        var visible = new List<(int, int)>();

        var added = NearPlaneClipper.Clip(vbx, vbx.Mesh!.Triangles[0], 0, 0, visible);

        Assert.Equal(2, added);
        Assert.Equal(2, visible.Count);
    }

    [Fact]
    public void Clip_NewVerticesLieOnNearPlane()
    {
        using var vbx = MakeBuffer(
            new Vector4(0, 0, 1, 2),
            new Vector4(0, 0, -1, 1),
            new Vector4(0, 0, -1, 1));
        var visible = new List<(int, int)>();

        NearPlaneClipper.Clip(vbx, vbx.Mesh!.Triangles[0], 0, 0, visible);

        var t = vbx.GetTriangle(visible[0].Item2);
        foreach (var index in new[] { t.I0, t.I1, t.I2 })
        {
            Assert.True(vbx.GetVertex(index).Proj.Z >= 0);
        }
    }

    [Fact]
    public void Clip_InterpolatesTexCoords()
    {
        using var vbx = MakeBuffer(
            new Vector4(0, 0, 1, 2),
            new Vector4(0, 0, -1, 1),
            new Vector4(0, 0, -1, 1),
            [new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1)]);
        var visible = new List<(int, int)>();

        NearPlaneClipper.Clip(vbx, vbx.Mesh!.Triangles[0], 0, 0, visible);

        var uv = vbx.GetTexCoord(3);
        Assert.Equal(0.5f, uv.X, 3);
        Assert.Equal(0f, uv.Y, 3);
    }

    [Fact]
    public void Clip_RecordsSourceTriangleIndex()
    {
        using var vbx = MakeBuffer(
            new Vector4(0, 0, 1, 2),
            new Vector4(0, 0, -1, 1),
            new Vector4(0, 0, -1, 1));
        var visible = new List<(int, int)>();

        NearPlaneClipper.Clip(vbx, vbx.Mesh!.Triangles[0], 0, 7, visible);

        Assert.Equal(7, visible[0].Item1);
        Assert.Equal(0, vbx.SourceTriangleIndex(visible[0].Item2));
    }

    [Fact]
    public void Clip_SubTrianglesOutsideSidePlanes_AreRejected()
    {
        using var vbx = MakeBuffer(
            new Vector4(-10, 0, 1, 2),
            new Vector4(-10, 0, -1, 1),
            new Vector4(-10, 1, -1, 1));
        var visible = new List<(int, int)>();

        var added = NearPlaneClipper.Clip(vbx, vbx.Mesh!.Triangles[0], 0, 0, visible);

        Assert.Equal(0, added);
        Assert.Empty(visible);
    }
}
