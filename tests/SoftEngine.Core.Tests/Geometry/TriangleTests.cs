using SoftEngine.Core.Buffers;
using SoftEngine.Core.Geometry;
using System.Numerics;

namespace SoftEngine.Core.Tests;

public class TriangleTests
{
    private static VertexBuffer MakeBuffer(params Vertices[] vertices)
    {
        var vbx = new VertexBuffer(vertices.Length);
        for (var i = 0; i < vertices.Length; i++)
        {
            vbx.Vertices[i] = vertices[i];
        }
        return vbx;
    }

    [Fact]
    public void IsFacingBack_FrontFacingTriangle_ReturnsFalse()
    {
        using var vbx = MakeBuffer(
            new Vertices { View = new Vector3(0, 0, -5) },
            new Vertices { View = new Vector3(1, 0, -5) },
            new Vertices { View = new Vector3(0, 1, -5) });

        Assert.False(new Triangle(0, 1, 2).IsFacingBack(vbx));
    }

    [Fact]
    public void IsFacingBack_ReversedWinding_ReturnsTrue()
    {
        using var vbx = MakeBuffer(
            new Vertices { View = new Vector3(0, 0, -5) },
            new Vertices { View = new Vector3(1, 0, -5) },
            new Vertices { View = new Vector3(0, 1, -5) });

        Assert.True(new Triangle(0, 2, 1).IsFacingBack(vbx));
    }

    [Fact]
    public void IsBehindFarPlane_AllVerticesBehindCamera_ReturnsTrue()
    {
        using var vbx = MakeBuffer(
            new Vertices { View = new Vector3(0, 0, 1) },
            new Vertices { View = new Vector3(1, 0, 2) },
            new Vertices { View = new Vector3(0, 1, 3) });

        Assert.True(new Triangle(0, 1, 2).IsBehindFarPlane(vbx));
    }

    [Fact]
    public void IsBehindFarPlane_OneVertexInFront_ReturnsFalse()
    {
        using var vbx = MakeBuffer(
            new Vertices { View = new Vector3(0, 0, -1) },
            new Vertices { View = new Vector3(1, 0, 2) },
            new Vertices { View = new Vector3(0, 1, 3) });

        Assert.False(new Triangle(0, 1, 2).IsBehindFarPlane(vbx));
    }

    [Fact]
    public void IsOutsideFrustum_TriangleInsideFrustum_ReturnsFalse()
    {
        using var vbx = MakeBuffer(
            new Vertices { Proj = new Vector4(0, 0, 0.5f, 1) },
            new Vertices { Proj = new Vector4(0.5f, 0, 0.5f, 1) },
            new Vertices { Proj = new Vector4(0, 0.5f, 0.5f, 1) });

        Assert.False(new Triangle(0, 1, 2).IsOutsideFrustum(vbx));
    }

    [Fact]
    public void IsOutsideFrustum_AllVerticesLeftOfFrustum_ReturnsTrue()
    {
        using var vbx = MakeBuffer(
            new Vertices { Proj = new Vector4(-2, 0, 0.5f, 1) },
            new Vertices { Proj = new Vector4(-3, 0, 0.5f, 1) },
            new Vertices { Proj = new Vector4(-2, 1, 0.5f, 1) });

        Assert.True(new Triangle(0, 1, 2).IsOutsideFrustum(vbx));
    }

    [Fact]
    public void IsOutsideFrustum_NegativeW_ReturnsTrue()
    {
        using var vbx = MakeBuffer(
            new Vertices { Proj = new Vector4(0, 0, 0.5f, -1) },
            new Vertices { Proj = new Vector4(0.5f, 0, 0.5f, 1) },
            new Vertices { Proj = new Vector4(0, 0.5f, 0.5f, 1) });

        Assert.True(new Triangle(0, 1, 2).IsOutsideFrustum(vbx));
    }

    [Fact]
    public void IsOutsideFrustum_StraddlingOneEdge_ReturnsFalse()
    {
        using var vbx = MakeBuffer(
            new Vertices { Proj = new Vector4(-2, 0, 0.5f, 1) },
            new Vertices { Proj = new Vector4(0.5f, 0, 0.5f, 1) },
            new Vertices { Proj = new Vector4(0, 0.5f, 0.5f, 1) });

        Assert.False(new Triangle(0, 1, 2).IsOutsideFrustum(vbx));
    }

    [Fact]
    public void TransformWorld_ClippedVertexIndex_IsIgnored()
    {
        var mesh = new Mesh(
            [new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0)],
            [new Triangle(0, 1, 2)]);
        using var vbx = new VertexBuffer(3) { Mesh = mesh, WorldMatrix = Matrix4x4.Identity };
        var clippedVertex = new Vertices { World = new Vector3(5, 5, 5), Norm = new Vector3(0, 0, 1) };
        var index = vbx.AddClippedVertex(clippedVertex, Vector2.Zero);

        var t = new Triangle(0, 1, index);
        t.TransformWorld(vbx);

        Assert.Equal(new Vector3(5, 5, 5), vbx.GetVertex(index).World);
        Assert.Equal(mesh.Vertices[1], vbx.GetVertex(1).World);
    }
}
