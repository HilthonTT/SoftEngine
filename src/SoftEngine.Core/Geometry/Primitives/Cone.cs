using System.Numerics;

namespace SoftEngine.Core.Geometry.Primitives;

/// <summary>
/// A cone about the Y axis with its apex at +height/2, centred on the origin like the other
/// primitives so that scaling and rotation behave the same way across all of them.
/// </summary>
public sealed class Cone : Mesh
{
    /// <param name="radius">Radius of the base.</param>
    /// <param name="height">Extent along Y; the base sits at -height/2 and the apex at +height/2.</param>
    /// <param name="segments">Divisions around the axis. Clamped to at least three.</param>
    /// <param name="capped">Whether to close the base.</param>
    public Cone(float radius = 1f, float height = 2f, int segments = 24, bool capped = true)
        : this(Build(radius, height, int.Max(3, segments), capped))
    {
    }

    private Cone(PrimitiveGeometry geometry)
        : base(geometry.Vertices, geometry.Triangles, geometry.Normals)
    {
        TexCoords = geometry.TexCoords;
    }

    private static PrimitiveGeometry Build(float radius, float height, int segments, bool capped)
    {
        var builder = new PrimitiveBuilder();
        var halfHeight = height / 2f;

        for (var i = 0; i <= segments; i++)
        {
            var (sin, cos) = MathF.SinCos(MathF.Tau * i / segments);

            builder.Add(
                new Vector3(radius * cos, -halfHeight, radius * sin),
                SideNormal(cos, sin, radius, height),
                new Vector2((float)i / segments, 0f));
        }

        // The apex is one point of the surface but many of its normals — the slope leans a
        // different way all the way round it — so each triangle gets its own, aimed down the
        // middle of that triangle rather than at either of its edges.
        for (var i = 0; i < segments; i++)
        {
            var (sin, cos) = MathF.SinCos(MathF.Tau * (i + 0.5f) / segments);
            var apex = builder.Add(
                new Vector3(0f, halfHeight, 0f),
                SideNormal(cos, sin, radius, height),
                new Vector2((i + 0.5f) / segments, 1f));

            builder.AddTriangle(i, apex, i + 1);
        }

        if (capped)
        {
            builder.AddDisc(-halfHeight, radius, segments, facingUp: false);
        }

        return builder.Build();
    }

    /// <summary>
    /// The outward normal on the slanted surface. It is not the radial direction — that is
    /// perpendicular to the axis, while the surface leans inward by the cone's slope — but the
    /// radial direction tilted up by radius/height, which is the only vector at right angles to
    /// both the rim and the line running up to the apex.
    /// </summary>
    private static Vector3 SideNormal(float cos, float sin, float radius, float height) =>
        Vector3.Normalize(new Vector3(height * cos, radius, height * sin));
}
