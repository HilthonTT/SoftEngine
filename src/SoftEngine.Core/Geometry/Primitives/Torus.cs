using System.Numerics;

namespace SoftEngine.Core.Geometry.Primitives;

/// <summary>
/// A torus lying in the XZ plane. Closed but not convex, and the only bundled primitive that is
/// concave anywhere — which makes it the shape worth reaching for when the thing being checked
/// is self-shadowing, ambient occlusion or a bounce of indirect light.
/// </summary>
public sealed class Torus : Mesh
{
    /// <param name="majorRadius">Distance from the origin to the centre of the tube.</param>
    /// <param name="minorRadius">Radius of the tube itself.</param>
    /// <param name="segments">Divisions around the ring. Clamped to at least three.</param>
    /// <param name="sides">Divisions around the tube. Clamped to at least three.</param>
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

        // Both ways round close on themselves, so both need a repeated row to cut a UV seam.
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
