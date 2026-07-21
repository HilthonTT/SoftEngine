using SoftEngine.Core.Buffers;
using SoftEngine.Core.Geometry;

namespace SoftEngine.Core.Rasterization;

public sealed class TriangleSorter
{
    public static void SortTrianglePoints(
        VertexBuffer vertexBuffer,
        FrameBuffer frameBuffer,
        int triangleIndices,
        out PaintedVertex v0,
        out PaintedVertex v1,
        out PaintedVertex v2)
    {
        ArgumentNullException.ThrowIfNull(vertexBuffer.Mesh, nameof(vertexBuffer.Mesh));

        Triangle t = vertexBuffer.Mesh.Triangles[triangleIndices];

        var (t0, t1, t2) = (vertexBuffer.Vertices[t.I0], vertexBuffer.Vertices[t.I1], vertexBuffer.Vertices[t.I2]);

        v0 = new PaintedVertex(t0.Norm, frameBuffer.ToScreen3(t0.Proj), t0.World);
        v1 = new PaintedVertex(t1.Norm, frameBuffer.ToScreen3(t1.Proj), t1.World);
        v2 = new PaintedVertex(t2.Norm, frameBuffer.ToScreen3(t2.Proj), t2.World);

        if (v0.ScreenProj.Y > v1.ScreenProj.Y)
        {
            (v0, v1) = (v1, v0);
        }
        if (v1.ScreenProj.Y > v2.ScreenProj.Y)
        {
            (v1, v2) = (v2, v1);
        }
        if (v0.ScreenProj.Y > v1.ScreenProj.Y)
        {
            (v0, v1) = (v1, v0);
        }
    }
}
