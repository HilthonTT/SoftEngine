using System.Numerics;

namespace SoftEngine.Core.Geometry.Primitives;

public sealed class Cylinder : Mesh
{
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
