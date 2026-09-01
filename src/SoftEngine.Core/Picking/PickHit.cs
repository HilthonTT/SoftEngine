using SoftEngine.Core.Geometry;
using System.Numerics;

namespace SoftEngine.Core.Picking;

public readonly record struct PickHit(
    IMesh Mesh,
    int MeshIndex,
    int TriangleIndex,
    float Distance,
    Vector3 Point,
    Vector3 Normal);
