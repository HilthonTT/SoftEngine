using System.Numerics;

namespace SoftEngine.Core.Geometry;

public static class TangentBuilder
{
    public static Vector4[] Build(Vector3[] vertices, Vector3[] normals, Vector2[] texCoords, Triangle[] triangles)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(normals);
        ArgumentNullException.ThrowIfNull(texCoords);
        ArgumentNullException.ThrowIfNull(triangles);

        var count = vertices.Length;
        var tangents = new Vector3[count];
        var bitangents = new Vector3[count];

        foreach (var triangle in triangles)
        {
            var (i0, i1, i2) = (triangle.I0, triangle.I1, triangle.I2);

            if ((uint)i0 >= (uint)count || (uint)i1 >= (uint)count || (uint)i2 >= (uint)count ||
                i0 >= texCoords.Length || i1 >= texCoords.Length || i2 >= texCoords.Length)
            {
                continue;
            }

            var edge1 = vertices[i1] - vertices[i0];
            var edge2 = vertices[i2] - vertices[i0];

            var deltaUv1 = texCoords[i1] - texCoords[i0];
            var deltaUv2 = texCoords[i2] - texCoords[i0];

            var determinant = deltaUv1.X * deltaUv2.Y - deltaUv2.X * deltaUv1.Y;
            if (MathF.Abs(determinant) < 1e-12f)
            {
                continue;
            }

            var inverse = 1f / determinant;

            var tangent = (edge1 * deltaUv2.Y - edge2 * deltaUv1.Y) * inverse;
            var bitangent = (edge2 * deltaUv1.X - edge1 * deltaUv2.X) * inverse;

            tangents[i0] += tangent;
            tangents[i1] += tangent;
            tangents[i2] += tangent;

            bitangents[i0] += bitangent;
            bitangents[i1] += bitangent;
            bitangents[i2] += bitangent;
        }

        var result = new Vector4[count];

        for (var i = 0; i < count; i++)
        {
            var normal = i < normals.Length ? normals[i] : Vector3.UnitY;
            normal = normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : Vector3.UnitY;

            var tangent = tangents[i];

            tangent -= normal * Vector3.Dot(normal, tangent);

            if (tangent.LengthSquared() < 1e-12f)
            {
                tangent = MathF.Abs(normal.X) < 0.9f
                    ? Vector3.Cross(normal, Vector3.UnitX)
                    : Vector3.Cross(normal, Vector3.UnitY);
            }

            tangent = Vector3.Normalize(tangent);

            var handedness = Vector3.Dot(Vector3.Cross(normal, tangent), bitangents[i]) < 0f ? -1f : 1f;

            result[i] = new Vector4(tangent, handedness);
        }

        return result;
    }
}
