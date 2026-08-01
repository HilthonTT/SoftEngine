using System.Numerics;

namespace SoftEngine.Core.Geometry.Primitives;

/// <summary>
/// A cylinder about the Y axis, centred on the origin. Uncapped it is a tube open at both ends —
/// useful as a shell to stand inside, and the one case where back-face culling has to come off.
/// </summary>
public sealed class Cylinder : Mesh
{
    /// <param name="radius">Radius of both ends.</param>
    /// <param name="height">Extent along Y; the body spans ±height/2.</param>
    /// <param name="segments">Divisions around the axis. Clamped to at least three.</param>
    /// <param name="capped">Whether to close the two ends.</param>
    public Cylinder(float radius = 1f, float height = 2f, int segments = 24, bool capped = true)
        : this(Build(radius, height, int.Max(3, segments), capped))
    {
    }

    private Cylinder(PrimitiveGeometry geometry)
        : base(geometry.Vertices, geometry.Triangles, geometry.Normals)
    {
        TexCoords = geometry.TexCoords;
    }

    private static PrimitiveGeometry Build(float radius, float height, int segments, bool capped)
    {
        var builder = new PrimitiveBuilder();
        var halfHeight = height / 2f;

        // Two vertices per column, the last column repeating the first to cut the UV seam.
        for (var i = 0; i <= segments; i++)
        {
            var u = (float)i / segments;
            var (sin, cos) = MathF.SinCos(MathF.Tau * i / segments);
            var normal = new Vector3(cos, 0f, sin);

            builder.Add(new Vector3(radius * cos, -halfHeight, radius * sin), normal, new Vector2(u, 0f));
            builder.Add(new Vector3(radius * cos, halfHeight, radius * sin), normal, new Vector2(u, 1f));
        }

        for (var i = 0; i < segments; i++)
        {
            var bottom = i * 2;
            builder.AddQuad(bottom, bottom + 1, bottom + 3, bottom + 2);
        }

        if (capped)
        {
            builder.AddDisc(halfHeight, radius, segments, facingUp: true);
            builder.AddDisc(-halfHeight, radius, segments, facingUp: false);
        }

        return builder.Build();
    }
}
