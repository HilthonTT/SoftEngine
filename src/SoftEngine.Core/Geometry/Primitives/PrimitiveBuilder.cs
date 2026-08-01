using System.Numerics;

namespace SoftEngine.Core.Geometry.Primitives;

/// <summary>
/// Accumulates a generated surface vertex by vertex. Every primitive in this folder is some
/// loop over a parametric surface, and the part worth sharing is not the loop but the winding:
/// the renderer takes <c>Cross(v1 - v0, v2 - v0)</c> as the outward normal
/// (<see cref="Triangle.IsFacingBack"/>), so a quad wound the wrong way round is invisible under
/// back-face culling and lit from behind without it. <see cref="AddQuad"/> is the one place that
/// convention is written down, and every primitive here goes through it.
/// </summary>
internal sealed class PrimitiveBuilder
{
    private readonly List<Vector3> _vertices = [];
    private readonly List<Vector3> _normals = [];
    private readonly List<Vector2> _texCoords = [];
    private readonly List<Triangle> _triangles = [];

    /// <summary>Adds a vertex and returns its index, for the triangles that will reference it.</summary>
    public int Add(Vector3 position, Vector3 normal, Vector2 texCoord)
    {
        _vertices.Add(position);
        _normals.Add(normal);
        _texCoords.Add(texCoord);

        return _vertices.Count - 1;
    }

    /// <summary>Adds one triangle, whose corners are wound counter-clockwise seen from outside.</summary>
    public void AddTriangle(int a, int b, int c) => _triangles.Add(new Triangle(a, b, c));

    /// <summary>
    /// Adds the two triangles of a quad whose four corners are given in order around its rim,
    /// counter-clockwise seen from outside the surface.
    /// </summary>
    public void AddQuad(int a, int b, int c, int d)
    {
        AddTriangle(a, b, c);
        AddTriangle(a, c, d);
    }

    /// <summary>
    /// Adds a flat disc of radius <paramref name="radius"/> at height <paramref name="y"/>, as a
    /// fan around its own centre — the end cap a cylinder needs twice and a cone once. Its rim
    /// vertices are its own rather than the side wall's, because a cap meets the wall at a hard
    /// edge: one shared vertex cannot hold both surfaces' normals, and averaging them rounds the
    /// rim of every cylinder in the scene.
    /// </summary>
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

            // Seen from outside the solid, an upward cap's rim runs the other way round.
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
