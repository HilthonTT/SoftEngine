using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Math;
using System.Numerics;

namespace SoftEngine.Core.Geometry;

public class Mesh : IMesh
{
    public Mesh(
        Vector3[] vertices,
        Triangle[] triangleIndices,
        Vector3[]? vertexNormals = null,
        ColorRGB[]? triangleColors = null)
    {
        Vertices = vertices;
        Triangles = triangleIndices;

        NormVertices = vertexNormals is null ? [.. this.CalculateVertexNormals()] : vertexNormals;
        TriangleColors = triangleColors ?? [.. Enumerable.Repeat(ColorRGB.Gray, Triangles.Length)];

        Scale = Vector3.One;

        Rotation = new Rotation3D(0, 0, 0);
    }

    public Rotation3D Rotation { get; set; }

    public Vector3 Position { get; set; }

    public Vector3 Scale { get; }

    public ColorRGB[] TriangleColors { get; }

    public Triangle[] Triangles { get; }

    public Vector3[] Vertices { get; }

    public Vector3[] NormVertices { get; }
}
