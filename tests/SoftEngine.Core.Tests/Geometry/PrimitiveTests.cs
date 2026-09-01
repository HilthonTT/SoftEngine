using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Geometry.Primitives;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Cameras;
using SoftEngine.Core.Scenes.Projections;
using System.Numerics;

namespace SoftEngine.Core.Tests.Geometry;

public class PrimitiveTests
{
    private const float Radius = 1.5f;

    private static Mesh Make(string primitive) => primitive switch
    {
        "plane" => new PlaneMesh(2f, 3f, 4, 5),
        "box" => new Box(2f, 3f, 4f),
        "sphere" => new UvSphere(Radius, 24, 16),
        "cylinder" => new Cylinder(1f, 2f, 24),
        "cone" => new Cone(1f, 2f, 24),
        "torus" => new Torus(1f, 0.25f, 32, 16),
        _ => throw new ArgumentOutOfRangeException(nameof(primitive), primitive, "Unknown primitive."),
    };

    private static double ExactVolume(string primitive) => primitive switch
    {
        "box" => 2d * 3d * 4d,
        "sphere" => 4d / 3d * System.Math.PI * System.Math.Pow(Radius, 3),
        "cylinder" => System.Math.PI * 1d * 1d * 2d,
        "cone" => System.Math.PI * 1d * 1d * 2d / 3d,
        "torus" => 2d * System.Math.PI * System.Math.PI * 1d * 0.25d * 0.25d,
        _ => throw new ArgumentOutOfRangeException(nameof(primitive), primitive, "Not a closed primitive."),
    };

    [Theory]
    [InlineData("plane")]
    [InlineData("box")]
    [InlineData("sphere")]
    [InlineData("cylinder")]
    [InlineData("cone")]
    [InlineData("torus")]
    public void Normals_AreUnitLength(string primitive)
    {
        var mesh = Make(primitive);

        Assert.Equal(mesh.Vertices.Length, mesh.NormVertices.Length);
        Assert.All(mesh.NormVertices, normal => Assert.Equal(1f, normal.Length(), 3));
    }

    [Theory]
    [InlineData("plane")]
    [InlineData("box")]
    [InlineData("sphere")]
    [InlineData("cylinder")]
    [InlineData("cone")]
    [InlineData("torus")]
    public void TexCoords_CoverEveryVertexAndStayInTheUnitSquare(string primitive)
    {
        var mesh = Make(primitive);

        Assert.NotNull(mesh.TexCoords);
        Assert.Equal(mesh.Vertices.Length, mesh.TexCoords.Length);
        Assert.All(mesh.TexCoords, uv =>
        {
            Assert.InRange(uv.X, 0f, 1f);
            Assert.InRange(uv.Y, 0f, 1f);
        });
    }

    [Theory]
    [InlineData("plane")]
    [InlineData("box")]
    [InlineData("sphere")]
    [InlineData("cylinder")]
    [InlineData("cone")]
    [InlineData("torus")]
    public void Triangles_AreInRangeAndHaveArea(string primitive)
    {
        var mesh = Make(primitive);

        Assert.NotEmpty(mesh.Triangles);
        Assert.All(mesh.Triangles, triangle =>
        {
            foreach (var index in new[] { triangle.I0, triangle.I1, triangle.I2 })
            {
                Assert.InRange(index, 0, mesh.Vertices.Length - 1);
            }

            Assert.True(FaceNormal(mesh, triangle).Length() > 1e-6f, "Degenerate triangle.");
        });
    }

    [Theory]
    [InlineData("box")]
    [InlineData("sphere")]
    [InlineData("cylinder")]
    [InlineData("cone")]
    [InlineData("torus")]
    public void ClosedPrimitives_HaveNoBoundaryOrReversedEdges(string primitive)
    {
        var mesh = Make(primitive);
        var welded = WeldByPosition(mesh.Vertices);
        var edges = new HashSet<(int From, int To)>();

        foreach (var triangle in mesh.Triangles)
        {
            var (a, b, c) = (welded[triangle.I0], welded[triangle.I1], welded[triangle.I2]);

            foreach (var edge in new[] { (a, b), (b, c), (c, a) })
            {
                Assert.True(edges.Add(edge), $"Edge {edge} is traversed twice the same way round.");
            }
        }

        foreach (var (from, to) in edges)
        {
            Assert.Contains((to, from), edges);
        }
    }

    [Theory]
    [InlineData("box")]
    [InlineData("sphere")]
    [InlineData("cylinder")]
    [InlineData("cone")]
    [InlineData("torus")]
    public void ClosedPrimitives_EncloseTheirAnalyticVolume(string primitive)
    {
        var mesh = Make(primitive);
        var volume = 0d;

        foreach (var triangle in mesh.Triangles)
        {
            var (a, b, c) = (mesh.Vertices[triangle.I0], mesh.Vertices[triangle.I1], mesh.Vertices[triangle.I2]);
            volume += Vector3.Dot(a, Vector3.Cross(b, c)) / 6d;
        }

        var exact = ExactVolume(primitive);
        Assert.InRange(volume, exact * 0.9d, exact * 1.02d);
    }

    [Theory]
    [InlineData("plane")]
    [InlineData("box")]
    [InlineData("sphere")]
    [InlineData("cylinder")]
    [InlineData("cone")]
    [InlineData("torus")]
    public void VertexNormals_PointTheSameWayAsTheTrianglesTheyBelongTo(string primitive)
    {
        var mesh = Make(primitive);

        Assert.All(mesh.Triangles, triangle =>
        {
            var face = Vector3.Normalize(FaceNormal(mesh, triangle));

            foreach (var index in new[] { triangle.I0, triangle.I1, triangle.I2 })
            {
                Assert.True(
                    Vector3.Dot(face, mesh.NormVertices[index]) > 0f,
                    $"Vertex {index}'s normal faces away from its own triangle.");
            }
        });
    }

    [Fact]
    public void PlaneMesh_IsAGridOfUpwardFacingQuads()
    {
        var plane = new PlaneMesh(2f, 3f, 4, 5);

        Assert.Equal(4 * 5 * 2, plane.Triangles.Length);
        Assert.All(plane.NormVertices, normal => Approx.Equal(Vector3.UnitY, normal));
        Assert.All(plane.Triangles, triangle => Approx.Equal(Vector3.UnitY, Vector3.Normalize(FaceNormal(plane, triangle))));
        Assert.All(plane.Vertices, vertex =>
        {
            Assert.Equal(0f, vertex.Y);
            Assert.InRange(vertex.X, -1f, 1f);
            Assert.InRange(vertex.Z, -1.5f, 1.5f);
        });
    }

    [Fact]
    public void PlaneMesh_SubdivisionsBelowOne_StillProduceAQuad() =>
        Assert.Equal(2, new PlaneMesh(1f, 1f, 0, -3).Triangles.Length);

    [Fact]
    public void PlaneMesh_UvScale_TilesTheTextureWithoutMovingAVertex()
    {
        var plain = new PlaneMesh(4f, 4f, 2, 2);
        var tiled = new PlaneMesh(4f, 4f, 2, 2, uvScale: 8f);

        Assert.Equal(plain.Vertices, tiled.Vertices);
        Assert.Equal(1f, plain.TexCoords!.Max(uv => uv.X));
        Assert.Equal(8f, tiled.TexCoords!.Max(uv => uv.X));
    }

    [Fact]
    public void Cylinder_Uncapped_IsOpenAtBothEnds()
    {
        var tube = new Cylinder(1f, 2f, 24, capped: false);
        var welded = WeldByPosition(tube.Vertices);
        var edges = new HashSet<(int, int)>();

        foreach (var triangle in tube.Triangles)
        {
            var (a, b, c) = (welded[triangle.I0], welded[triangle.I1], welded[triangle.I2]);
            edges.Add((a, b));
            edges.Add((b, c));
            edges.Add((c, a));
        }

        Assert.Equal(2 * 24, edges.Count(edge => !edges.Contains((edge.Item2, edge.Item1))));
    }

    [Fact]
    public void UvSphere_CarriesTheTexCoordsAnIcoSphereCannot()
    {
        Assert.Null(new IcoSphere(2).TexCoords);
        Assert.NotNull(new UvSphere().TexCoords);
    }

    [Fact]
    public void Box_GivesEveryFaceItsOwnCornersAndOneFlatNormal()
    {
        var box = new Box(2f, 3f, 4f);

        Assert.Equal(24, box.Vertices.Length);
        Assert.Equal(12, box.Triangles.Length);

        Assert.Equal(6, box.NormVertices.Distinct().Count());
        Assert.All(box.NormVertices, normal => Assert.Equal(1f, MathF.Abs(normal.X) + MathF.Abs(normal.Y) + MathF.Abs(normal.Z), 4));
    }

    [Fact]
    public void Box_OwnsItsTriangleColours()
    {
        var first = new Box();
        var second = new Box();

        Array.Fill(first.TriangleColors, ColorRGB.Red);

        Assert.All(second.TriangleColors, color => Assert.Equal(ColorRGB.Gray, color));
    }

    [Theory]
    [InlineData(PrimitiveShape.Plane)]
    [InlineData(PrimitiveShape.Box)]
    [InlineData(PrimitiveShape.UvSphere)]
    [InlineData(PrimitiveShape.IcoSphere)]
    [InlineData(PrimitiveShape.Cylinder)]
    [InlineData(PrimitiveShape.Cone)]
    [InlineData(PrimitiveShape.Torus)]
    public void PrimitiveFactory_BuildsEveryShapeToTheSameHalfExtent(PrimitiveShape shape)
    {
        const float Size = 2.5f;

        var mesh = PrimitiveFactory.Create(shape, Size);
        var widest = mesh.Vertices.Max(vertex => MathF.Max(MathF.Abs(vertex.X), MathF.Max(MathF.Abs(vertex.Y), MathF.Abs(vertex.Z))));

        Assert.InRange(widest, Size * 0.99f, Size * 1.0001f);
    }

    [Fact]
    public void PrimitiveFactory_SizeOfZero_StillBuildsSomething() =>
        Assert.NotEmpty(PrimitiveFactory.Create(PrimitiveShape.Cone, 0f).Triangles);

    [Fact]
    public void IcoSphere_DefaultRadius_LeavesTheUnitSphereBitIdentical()
    {
        Assert.Equal(new IcoSphere(3).Vertices, new IcoSphere(3, 1f).Vertices);
        Assert.All(new IcoSphere(3).Vertices, vertex => Assert.Equal(1f, vertex.Length(), 6));
    }

    [Fact]
    public void IcoSphere_Radius_ScalesTheSphereWithoutMovingItsCentre()
    {
        var sphere = new IcoSphere(2, 4f);

        Assert.All(sphere.Vertices, vertex => Assert.Equal(4f, vertex.Length(), 4));
        Assert.Equal(4f, sphere.BoundingRadius, 4);
    }

    [Theory]
    [InlineData("box", 2.6926f)]
    [InlineData("sphere", Radius)]
    [InlineData("cylinder", 1.4142f)]
    [InlineData("cone", 1.4142f)]
    [InlineData("torus", 1.25f)]
    public void ClosedPrimitives_ReportTheirBoundingRadius(string primitive, float expected) =>
        Assert.Equal(expected, Make(primitive).BoundingRadius, 3);

    [Theory]
    [InlineData("box")]
    [InlineData("sphere")]
    [InlineData("cylinder")]
    [InlineData("cone")]
    [InlineData("torus")]
    public void ClosedPrimitives_PresentTheirNearSideToTheCamera(string primitive)
    {
        var outward = Render(Make(primitive));
        var inverted = Render(Reversed(Make(primitive)));

        Assert.True(outward.Pixels > 0, "Nothing was drawn at all.");
        Assert.True(
            outward.Depth < inverted.Depth,
            $"The visible surface is the far one: {outward.Depth} is not nearer than {inverted.Depth}.");
    }

    private sealed class FixedCamera(Vector3 position) : ICamera
    {
        public Vector3 Position { get; set; } = position;

        public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Vector3.Zero, Vector3.UnitY);
    }

    private static (int Pixels, int Depth) Render(Mesh mesh)
    {
        var renderer = new Renderer();
        var surface = new FrameBuffer(128, 128) { Stats = renderer.Stats };
        renderer.Settings.BackFaceCulling = true;

        renderer.Render(
            new Scene
            {
                Surface = surface,
                Camera = new FixedCamera(new Vector3(0f, 0f, 6f)),
                Projection = new PerspectiveProjection(MathF.PI / 4f, 1f, 100f),
                World = new SimpleWorld { Meshes = [mesh], Lights = [] },
            },
            new ClassicPainter());

        return (renderer.Stats.DrawnPixelCount, surface.GetDepth(64, 64));
    }

    private static Mesh Reversed(Mesh mesh) => new(
        mesh.Vertices,
        [.. mesh.Triangles.Select(t => new Triangle(t.I0, t.I2, t.I1))],
        [.. mesh.NormVertices.Select(n => -n)]);

    private static Vector3 FaceNormal(Mesh mesh, Triangle triangle) => Vector3.Cross(
        mesh.Vertices[triangle.I1] - mesh.Vertices[triangle.I0],
        mesh.Vertices[triangle.I2] - mesh.Vertices[triangle.I0]);

    private static int[] WeldByPosition(Vector3[] vertices)
    {
        var welded = new int[vertices.Length];
        var unique = new List<Vector3>();

        for (var i = 0; i < vertices.Length; i++)
        {
            welded[i] = -1;

            for (var u = 0; u < unique.Count; u++)
            {
                if ((unique[u] - vertices[i]).Length() < 1e-5f)
                {
                    welded[i] = u;
                    break;
                }
            }

            if (welded[i] < 0)
            {
                welded[i] = unique.Count;
                unique.Add(vertices[i]);
            }
        }

        return welded;
    }
}
