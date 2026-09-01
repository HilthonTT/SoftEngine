using System.Numerics;

namespace SoftEngine.Core.Geometry.Primitives;

internal readonly record struct PrimitiveGeometry(
    Vector3[] Vertices,
    Triangle[] Triangles,
    Vector3[] Normals,
    Vector2[] TexCoords);
