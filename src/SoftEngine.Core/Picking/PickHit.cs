using SoftEngine.Core.Geometry;
using System.Numerics;

namespace SoftEngine.Core.Picking;

/// <summary>
/// What a ray ran into: which mesh, which of its triangles, and where.
///
/// <see cref="MeshIndex"/> is the mesh's position in the world's list, which is also what
/// <see cref="Diagnostics.SceneObjectIds.Mesh"/> turns into the <c>obj:N</c> the graphics
/// debugger labels everything with — so a click can select the same row an event or a pixel
/// write would.
/// </summary>
public readonly record struct PickHit(
    IMesh Mesh,
    int MeshIndex,
    int TriangleIndex,
    float Distance,
    Vector3 Point,
    Vector3 Normal);
