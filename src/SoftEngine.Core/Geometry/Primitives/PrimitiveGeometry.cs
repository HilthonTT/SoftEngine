using System.Numerics;

namespace SoftEngine.Core.Geometry.Primitives;

/// <summary>The four parallel arrays a generated primitive hands to <see cref="Mesh"/>'s constructor.</summary>
internal readonly record struct PrimitiveGeometry(
    Vector3[] Vertices,
    Triangle[] Triangles,
    Vector3[] Normals,
    Vector2[] TexCoords);
