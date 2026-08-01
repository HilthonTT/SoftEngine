using System.Numerics;

namespace SoftEngine.Core.Geometry.Primitives;

/// <summary>
/// A sphere built as rings of latitude, which is what makes it the textured one:
/// <see cref="IcoSphere"/> subdivides an icosahedron and so has no seam to cut a UV map along,
/// and carries no <see cref="Mesh.TexCoords"/> at all. Triangles here are uneven — the ones at
/// the poles are slivers — so an untextured sphere is still better off as an IcoSphere.
/// </summary>
public sealed class UvSphere : Mesh
{
    /// <param name="radius">Radius, matching <see cref="IcoSphere"/>'s unit sphere by default.</param>
    /// <param name="segments">Divisions around the equator. Clamped to at least three.</param>
    /// <param name="rings">Divisions from pole to pole. Clamped to at least two.</param>
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

        // The column at u = 1 repeats the one at u = 0 in position and differs in UV: a vertex
        // carries one texture coordinate, so the seam has to be cut somewhere, and a shared
        // column would run the whole texture backwards across the last segment.
        for (var i = 0; i <= segments; i++)
        {
            var (sinTheta, cosTheta) = MathF.SinCos(MathF.Tau * i / segments);

            for (var j = 0; j <= rings; j++)
            {
                // Phi runs from the south pole, so v = 0 is the bottom of the texture.
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

                // A quad against a pole is a triangle: two of its corners are the same point.
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
