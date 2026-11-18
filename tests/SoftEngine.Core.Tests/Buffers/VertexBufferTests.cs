using SoftEngine.Core.Buffers;
using SoftEngine.Core.Geometry;
using System.Numerics;

namespace SoftEngine.Core.Tests.Buffers;

public class VertexBufferTests
{
    private static Mesh MakeMesh()
    {
        var mesh = new Mesh(
            [new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0)],
            [new Triangle(0, 1, 2)])
        {
            TexCoords = [new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1)]
        };
        return mesh;
    }

    [Fact]
    public void AddClippedVertex_ReturnsIndicesStartingAtSize()
    {
        using var vbx = new VertexBuffer(3) { Mesh = MakeMesh() };

        var first = vbx.AddClippedVertex(new Vertices(), Vector2.Zero);
        var second = vbx.AddClippedVertex(new Vertices(), Vector2.Zero);

        Assert.Equal(3, first);
        Assert.Equal(4, second);
    }

    [Fact]
    public void GetVertex_RoutesBaseAndClippedIndices()
    {
        using var vbx = new VertexBuffer(3) { Mesh = MakeMesh() };
        vbx.Vertices[0] = new Vertices { World = new Vector3(1, 2, 3) };
        var index = vbx.AddClippedVertex(new Vertices { World = new Vector3(7, 8, 9) }, Vector2.Zero);

        Assert.Equal(new Vector3(1, 2, 3), vbx.GetVertex(0).World);
        Assert.Equal(new Vector3(7, 8, 9), vbx.GetVertex(index).World);
    }

    [Fact]
    public void GetTexCoord_RoutesBaseAndClippedIndices()
    {
        using var vbx = new VertexBuffer(3) { Mesh = MakeMesh() };
        var index = vbx.AddClippedVertex(new Vertices(), new Vector2(0.25f, 0.75f));

        Assert.Equal(new Vector2(1, 0), vbx.GetTexCoord(1));
        Assert.Equal(new Vector2(0.25f, 0.75f), vbx.GetTexCoord(index));
    }

    [Fact]
    public void GetTexCoord_WithoutMeshTexCoords_ReturnsZero()
    {
        var mesh = new Mesh(
            [new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0)],
            [new Triangle(0, 1, 2)]);
        using var vbx = new VertexBuffer(3) { Mesh = mesh };

        Assert.Equal(Vector2.Zero, vbx.GetTexCoord(0));
    }

    [Fact]
    public void AddClippedTriangle_ReturnsIndicesAfterMeshTriangles()
    {
        using var vbx = new VertexBuffer(3) { Mesh = MakeMesh() };

        var index = vbx.AddClippedTriangle(new Triangle(0, 1, 2), 0);

        Assert.Equal(1, index);
        Assert.Equal(0, vbx.GetTriangle(index).I0);
    }

    [Fact]
    public void SourceTriangleIndex_MapsClippedToSourceAndBaseToItself()
    {
        using var vbx = new VertexBuffer(3) { Mesh = MakeMesh() };
        var clipped = vbx.AddClippedTriangle(new Triangle(0, 1, 2), 0);

        Assert.Equal(0, vbx.SourceTriangleIndex(0));
        Assert.Equal(0, vbx.SourceTriangleIndex(clipped));
    }

    [Fact]
    public void ResetClipped_DiscardsClippedGeometry()
    {
        using var vbx = new VertexBuffer(3) { Mesh = MakeMesh() };
        vbx.AddClippedVertex(new Vertices(), Vector2.Zero);
        vbx.AddClippedTriangle(new Triangle(0, 1, 2), 0);

        vbx.ResetClipped();

        var index = vbx.AddClippedVertex(new Vertices(), Vector2.Zero);
        Assert.Equal(3, index);
        Assert.Equal(1, vbx.AddClippedTriangle(new Triangle(0, 1, 2), 0));
    }

    [Fact]
    public void GetTangent_IsInWorldSpace_LikeTheNormalItIsBuiltAgainst()
    {
        // A quarter turn about Y takes +X to -Z. Left in model space, the tangent of a mesh
        // turned to face +X would lie along its world normal and the normal map would vanish.
        var mesh = MakeMesh();
        mesh.Tangents = [new Vector4(1, 0, 0, -1), new Vector4(1, 0, 0, -1), new Vector4(1, 0, 0, -1)];

        using var vbx = new VertexBuffer(3)
        {
            Mesh = mesh,
            WorldMatrix = Matrix4x4.CreateRotationY(MathF.PI / 2) * Matrix4x4.CreateTranslation(5, 0, 0),
        };
        var clipped = vbx.AddClippedVertex(new Vertices(), Vector2.Zero, new Vector4(1, 0, 0, 1));

        var expected = new Vector3(0, 0, -1);
        AssertDirection(expected, -1f, vbx.GetTangent(0));
        AssertDirection(expected, 1f, vbx.GetTangent(clipped));

        static void AssertDirection(Vector3 expected, float handedness, Vector4 actual)
        {
            Assert.True(Vector3.Distance(expected, new Vector3(actual.X, actual.Y, actual.Z)) < 1e-5f, $"{actual}");
            Assert.Equal(handedness, actual.W);
        }
    }

    [Fact]
    public void GetTangent_BeforeAnyPassSetsTheWorld_IsUnchanged()
    {
        var mesh = MakeMesh();
        mesh.Tangents = [new Vector4(0, 1, 0, 1), Vector4.Zero, Vector4.Zero];

        using var vbx = new VertexBuffer(3) { Mesh = mesh };

        Assert.Equal(new Vector4(0, 1, 0, 1), vbx.GetTangent(0));
    }
}
