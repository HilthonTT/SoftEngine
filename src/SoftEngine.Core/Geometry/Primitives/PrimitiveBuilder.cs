using System.Numerics;

namespace SoftEngine.Core.Geometry.Primitives;

internal sealed class PrimitiveBuilder
{
    private readonly List<Vector3> _vertices = [];
    private readonly List<Vector3> _normals = [];
    private readonly List<Vector2> _texCoords = [];
    private readonly List<Triangle> _triangles = [];

    public int Add(Vector3 position, Vector3 normal, Vector2 texCoord)
    {
        _vertices.Add(position);
        _normals.Add(normal);
        _texCoords.Add(texCoord);

        return _vertices.Count - 1;
    }

    public void AddTriangle(int a, int b, int c) => _triangles.Add(new Triangle(a, b, c));

    public void AddQuad(int a, int b, int c, int d)
    {
        AddTriangle(a, b, c);
        AddTriangle(a, c, d);
    }

    public void AddDisc(float y, float radius, int segments, bool facingUp)
    {
        var normal = facingUp ? Vector3.UnitY : -Vector3.UnitY;
        var centre = Add(new Vector3(0f, y, 0f), normal, new Vector2(0.5f, 0.5f));
        var rim = VertexCount;

        for (var i = 0; i < segments; i++)
        {
            var angle = MathF.Tau * i / segments;
            var (sin, cos) = MathF.SinCos(angle);

            Add(
                new Vector3(radius * cos, y, radius * sin),
                normal,
                new Vector2(0.5f + (0.5f * cos), 0.5f + (facingUp ? 0.5f * sin : -0.5f * sin)));
        }

        for (var i = 0; i < segments; i++)
        {
            var current = rim + i;
            var next = rim + ((i + 1) % segments);

            if (facingUp)
            {
                AddTriangle(centre, next, current);
            }
            else
            {
                AddTriangle(centre, current, next);
            }
        }
    }

    private int VertexCount => _vertices.Count;

    public PrimitiveGeometry Build() => new([.. _vertices], [.. _triangles], [.. _normals], [.. _texCoords]);
}
