using SoftEngine.Core.Buffers;
using System.Numerics;

namespace SoftEngine.Core.Geometry;

public readonly struct Triangle(int p0, int p1, int p2)
{
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

    public readonly bool IsOutsideFrustum(VertexBuffer vertexBuffer)
    {
        Vector4 p0 = vertexBuffer.Vertices[I0].Proj;
        Vector4 p1 = vertexBuffer.Vertices[I1].Proj;
        Vector4 p2 = vertexBuffer.Vertices[I2].Proj;

        if (p0.W < 0 || p1.W < 0 || p2.W < 0)
        {
            return true;
        }

        if (p0.X < -p0.W && p1.X < -p1.W && p2.X < -p2.W)
        {
            return true;
        }

        if (p0.X > p0.W && p1.X > p1.W && p2.X > p2.W)
        {
            return true;
        }

        if (p0.Y < -p0.W && p1.Y < -p1.W && p2.Y < -p2.W)
        {
            return true;
        }

        if (p0.Y > p0.W && p1.Y > p1.W && p2.Y > p2.W)
        {
            return true;
        }

        if (p0.Z > p0.W && p1.Z > p1.W && p2.Z > p2.W)
        {
            return true;
        }

        // This last one is normally not necessary when a IsTriangleBehind check is done
        if (p0.Z < 0 && p1.Z < 0 && p2.Z < 0)
        {
            return true;
        }

        return false;
    }

    public readonly void TransformProjection(VertexBuffer vbx, Matrix4x4 projectionMatrix)
    {
        TransformProjectionVertex(vbx, I0, projectionMatrix);
        TransformProjectionVertex(vbx, I1, projectionMatrix);
        TransformProjectionVertex(vbx, I2, projectionMatrix);
    }

    private static void TransformProjectionVertex(VertexBuffer vbx, int v, Matrix4x4 projectionMatrix)
    {
        if (vbx.Vertices[v].Proj == Vector4.Zero)
        {
            vbx.Vertices[v] = vbx.Vertices[v].SetProj(Vector4.Transform(vbx.Vertices[v].View, projectionMatrix));
        }
    }

    public readonly void TransformWorld(VertexBuffer vertexBuffer)
    {
        Matrix4x4 worldMatrix = vertexBuffer.WorldMatrix;
        Vector3[] normVertices = vertexBuffer.Mesh?.NormVertices ?? [];
        Vector3[] modelVertices = vertexBuffer.Mesh?.Vertices ?? [];

        TransformWorldVertex(vertexBuffer, I0, modelVertices, normVertices, worldMatrix);
        TransformWorldVertex(vertexBuffer, I1, modelVertices, normVertices, worldMatrix);
        TransformWorldVertex(vertexBuffer, I2, modelVertices, normVertices, worldMatrix);
    }

    private static void TransformWorldVertex(VertexBuffer vertexBuffer, int v, Vector3[] modelVertices, Vector3[] normVertices, Matrix4x4 worldMatrix)
    {
        if (vertexBuffer.Vertices[v].Norm == Vector3.Zero)
        {
            vertexBuffer.Vertices[v] = vertexBuffer.Vertices[v].SetNorm(Vector3.TransformNormal(normVertices[v], worldMatrix));
        }

        if (vertexBuffer.Vertices[v].World == Vector3.Zero)
        {
            vertexBuffer.Vertices[v] = vertexBuffer.Vertices[v].SetWorld(Vector3.Transform(modelVertices[v], worldMatrix));
        }
    }

    public readonly bool IsFacingBack(VertexBuffer vertexBuffer)
    {
        var (v0, v1, v2) = (vertexBuffer.Vertices[I0].View, vertexBuffer.Vertices[I1].View, vertexBuffer.Vertices[I2].View);

        // Calculate the centroid without division, since dividing by 3 doesn't affect the sign .
        var centroid = v0 + v1 + v2;
        // Compute the triangle's unnormalized normal
        var normal = Vector3.Cross(v1 - v0, v2 - v0);

        // The sign of the dot product is unchanged by normalization,
        // so no need to normalize centroid or normal.
        return Vector3.Dot(centroid, normal) >= 0;
    }
}
