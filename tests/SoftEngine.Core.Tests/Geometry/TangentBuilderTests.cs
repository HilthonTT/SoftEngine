using SoftEngine.Core.Geometry;
using System.Numerics;

namespace SoftEngine.Core.Tests;

public class TangentBuilderTests
{
    // A unit quad in the XY plane facing +Z, as two triangles.
    private static readonly Vector3[] _quad =
    [
        new(0, 0, 0),
        new(1, 0, 0),
        new(1, 1, 0),
        new(0, 1, 0),
    ];

    private static readonly Vector3[] _normals =
    [
        Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ,
    ];

    private static readonly Triangle[] _triangles =
    [
        new(0, 1, 2),
        new(0, 2, 3),
    ];

    [Fact]
    public void Build_StandardUvs_PointsTheTangentAlongU()
    {
        Vector2[] uvs = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];

        var tangents = TangentBuilder.Build(_quad, _normals, uvs, _triangles);

        Assert.Equal(4, tangents.Length);

        foreach (var tangent in tangents)
        {
            Assert.Equal(1f, tangent.X, 4);
            Assert.Equal(0f, tangent.Y, 4);
            Assert.Equal(0f, tangent.Z, 4);
            Assert.Equal(1f, tangent.W);
        }
    }

    [Fact]
    public void Build_MirroredUvs_FlipsTheHandedness()
    {
        // U runs the other way, which mirrors the UV island.
        Vector2[] uvs = [new(1, 0), new(0, 0), new(0, 1), new(1, 1)];

        var tangents = TangentBuilder.Build(_quad, _normals, uvs, _triangles);

        foreach (var tangent in tangents)
        {
            Assert.Equal(-1f, tangent.X, 4);
            Assert.Equal(-1f, tangent.W);
        }
    }

    [Fact]
    public void Build_AlwaysProducesUnitTangentsPerpendicularToTheNormal()
    {
        Vector2[] uvs = [new(0, 0), new(1, 0.3f), new(0.8f, 1), new(0.1f, 0.9f)];

        var tangents = TangentBuilder.Build(_quad, _normals, uvs, _triangles);

        for (var i = 0; i < tangents.Length; i++)
        {
            var tangent = new Vector3(tangents[i].X, tangents[i].Y, tangents[i].Z);

            Assert.Equal(1f, tangent.Length(), 3);
            Assert.Equal(0f, Vector3.Dot(tangent, _normals[i]), 4);
        }
    }

    [Fact]
    public void Build_DegenerateUvs_FallsBackToAUsableFrame()
    {
        // Every corner on the same UV: there is no gradient to solve for.
        Vector2[] uvs = [Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.Zero];

        var tangents = TangentBuilder.Build(_quad, _normals, uvs, _triangles);

        foreach (var value in tangents)
        {
            var tangent = new Vector3(value.X, value.Y, value.Z);

            Assert.Equal(1f, tangent.Length(), 3);
            Assert.Equal(0f, Vector3.Dot(tangent, Vector3.UnitZ), 4);
        }
    }

    [Fact]
    public void EnsureTangents_IsIdempotentAndSkipsMeshesWithoutUvs()
    {
        var untextured = new Mesh(_quad, _triangles, _normals);
        untextured.EnsureTangents();

        Assert.Null(untextured.Tangents);

        var textured = new Mesh(_quad, _triangles, _normals)
        {
            TexCoords = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)],
        };

        textured.EnsureTangents();
        var first = textured.Tangents;

        textured.EnsureTangents();

        Assert.NotNull(first);
        Assert.Same(first, textured.Tangents);
    }
}
