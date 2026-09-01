using System.Numerics;

namespace SoftEngine.Core.Geometry.Primitives;

public sealed class Torus : Mesh
{
    public Torus(float majorRadius = 1f, float minorRadius = 0.25f, int segments = 32, int sides = 16)
        : this(Build(majorRadius, minorRadius, int.Max(3, segments), int.Max(3, sides)))
    {
    }

    private Torus(PrimitiveGeometry geometry)
        : base(geometry.Vertices, geometry.Triangles, geometry.Normals)
    {
        TexCoords = geometry.TexCoords;
    }

    private static PrimitiveGeometry Build(float majorRadius, float minorRadius, int segments, int sides)
    {
        var builder = new PrimitiveBuilder();

        for (var i = 0; i <= segments; i++)
        {
            var (sinTheta, cosTheta) = MathF.SinCos(MathF.Tau * i / segments);

            for (var j = 0; j <= sides; j++)
            {
                var (sinPhi, cosPhi) = MathF.SinCos(MathF.Tau * j / sides);
                var normal = new Vector3(cosPhi * cosTheta, sinPhi, cosPhi * sinTheta);
                var distance = majorRadius + (minorRadius * cosPhi);

                builder.Add(
                    new Vector3(distance * cosTheta, minorRadius * sinPhi, distance * sinTheta),
                    normal,
                    new Vector2((float)i / segments, (float)j / sides));
            }
        }

        for (var i = 0; i < segments; i++)
        {
            for (var j = 0; j < sides; j++)
            {
                var corner = (i * (sides + 1)) + j;
                builder.AddQuad(corner, corner + 1, corner + sides + 2, corner + sides + 1);
            }
        }

        return builder.Build();
    }
}
