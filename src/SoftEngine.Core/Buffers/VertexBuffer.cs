using SoftEngine.Core.Geometry;
using System.Buffers;
using System.Numerics;

namespace SoftEngine.Core.Buffers;

public sealed class VertexBuffer : IDisposable
{
    private static readonly ArrayPool<Vertices> _verticeBag = ArrayPool<Vertices>.Create();

    // Geometry produced by near-plane clipping this frame. Clipped vertex indices start
    // at Size and clipped triangle indices at Mesh.Triangles.Length, so painters address
    // both kinds through the Get* accessors without knowing which is which.
    private readonly List<Vertices> _clippedVertices = [];
    private readonly List<Vector2> _clippedTexCoords = [];
    private readonly List<(Triangle Triangle, int Source)> _clippedTriangles = [];

    public VertexBuffer(int vertexCount)
    {
        Size = vertexCount;
        Vertices = _verticeBag.Rent(vertexCount);
    }

    public IMesh? Mesh { get; set; }

    public Vertices[] Vertices { get; }

    public int Size { get; }

    public Matrix4x4 WorldMatrix { get; set; }

    public void ResetClipped()
    {
        _clippedVertices.Clear();
        _clippedTexCoords.Clear();
        _clippedTriangles.Clear();
    }

    /// <summary>Adds a vertex produced by clipping; returns its index (≥ <see cref="Size"/>).</summary>
    public int AddClippedVertex(in Vertices vertex, Vector2 texCoord)
    {
        _clippedVertices.Add(vertex);
        _clippedTexCoords.Add(texCoord);
        return Size + _clippedVertices.Count - 1;
    }

    /// <summary>
    /// Adds a triangle produced by clipping <paramref name="sourceTriangle"/>;
    /// returns its index (≥ <c>Mesh.Triangles.Length</c>).
    /// </summary>
    public int AddClippedTriangle(in Triangle triangle, int sourceTriangle)
    {
        _clippedTriangles.Add((triangle, sourceTriangle));
        return Mesh!.Triangles.Length + _clippedTriangles.Count - 1;
    }

    public Triangle GetTriangle(int triangleIndex)
    {
        var baseCount = Mesh!.Triangles.Length;
        return triangleIndex < baseCount
            ? Mesh.Triangles[triangleIndex]
            : _clippedTriangles[triangleIndex - baseCount].Triangle;
    }

    /// <summary>The mesh triangle a triangle index originates from — itself unless clipped.</summary>
    public int SourceTriangleIndex(int triangleIndex)
    {
        var baseCount = Mesh!.Triangles.Length;
        return triangleIndex < baseCount
            ? triangleIndex
            : _clippedTriangles[triangleIndex - baseCount].Source;
    }

    public Vertices GetVertex(int vertexIndex) =>
        vertexIndex < Size ? Vertices[vertexIndex] : _clippedVertices[vertexIndex - Size];

    public Vector2 GetTexCoord(int vertexIndex) =>
        vertexIndex < Size
            ? Mesh?.TexCoords?[vertexIndex] ?? Vector2.Zero
            : _clippedTexCoords[vertexIndex - Size];

    public void Dispose()
    {
        _verticeBag.Return(Vertices, true);
    }
}
