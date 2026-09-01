using System.Numerics;

namespace SoftEngine.Core.Geometry.Primitives;

public sealed class UvSphere : Mesh
{
    public UvSphere(float radius = 1f, int segments = 24, int rings = 16)
        : this(Build(radius, int.Max(3, segments), int.Max(2, rings)))
    {
    }

    private UvSphere(PrimitiveGeometry geometry)
        : base(geometry.Vertices, geometry.Triangles, geometry.Normals)
    {
        TexCoords = geometry.TexCoords;
    }

    private static PrimitiveGeometry Build(float radius, int segments, int rings)
    {
        var builder = new PrimitiveBuilder();

        for (var i = 0; i <= segments; i++)
        {
            var (sinTheta, cosTheta) = MathF.SinCos(MathF.Tau * i / segments);

            for (var j = 0; j <= rings; j++)
            {
                var (sinPhi, cosPhi) = MathF.SinCos(MathF.PI * j / rings);
                var normal = new Vector3(sinPhi * cosTheta, -cosPhi, sinPhi * sinTheta);

                builder.Add(normal * radius, normal, new Vector2((float)i / segments, (float)j / rings));
            }
        }

        for (var i = 0; i < segments; i++)
        {
            for (var j = 0; j < rings; j++)
            {
                var corner = (i * (rings + 1)) + j;
                var (a, b, c, d) = (corner, corner + 1, corner + rings + 2, corner + rings + 1);

                if (j == 0)
                {
                    builder.AddTriangle(a, b, c);
                }
                else if (j == rings - 1)
                {
                    builder.AddTriangle(a, c, d);
                }
                else
                {
                    builder.AddQuad(a, b, c, d);
                }
            }
        }

        return builder.Build();
    }
}
