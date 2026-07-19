using SoftEngine.Core.Buffers;
using System.Numerics;

namespace SoftEngine.Core.Geometry;

public readonly struct Triangle(int p0, int p1, int p2)
{
    public int[] IX { get; } = [p0, p1, p2];

    public int I0 { get; } = p0;

    public int I1 { get; } = p1;

    public int I2 { get; } = p2;

    public Vector3 CalculateNormal(Vector3[] vertices)
    {
        return Vector3.Normalize(Vector3.Cross(vertices[I1] - vertices[I0], vertices[I2] - vertices[I0]));
    }

    public bool Contains(Vector3 vertex, Vector3[] vertices)
    {
        return vertices[I0] == vertex || vertices[I1] == vertex || vertices[I2] == vertex;
    }

    public readonly bool IsBehindFarPlane(VertexBuffer vbx)
    {
        return vbx.Vertices[I0].View.Z > 0 && vbx.Vertices[I1].View.Z > 0 && vbx.Vertices[I2].View.Z > 0;
    }
}
