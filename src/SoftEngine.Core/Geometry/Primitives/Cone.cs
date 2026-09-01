using System.Numerics;

namespace SoftEngine.Core.Geometry.Primitives;

public sealed class Cone : Mesh
{
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

    private static Vector3 SideNormal(float cos, float sin, float radius, float height) =>
        Vector3.Normalize(new Vector3(height * cos, radius, height * sin));
}
