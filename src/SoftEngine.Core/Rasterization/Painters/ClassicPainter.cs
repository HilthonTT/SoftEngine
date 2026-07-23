using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;

namespace SoftEngine.Core.Rasterization.Painters;

public sealed class ClassicPainter : IPainter
{
    public void DrawTriangle(FrameBuffer surface, ColorRGB color, VertexBuffer vertexBuffer, int triangleIndice, in RowSlice slice)
    {
        ArgumentNullException.ThrowIfNull(vertexBuffer.Mesh, nameof(vertexBuffer));

        Triangle t = vertexBuffer.GetTriangle(triangleIndice);
        (Vertices a, Vertices b, Vertices c) = (vertexBuffer.GetVertex(t.I0), vertexBuffer.GetVertex(t.I1), vertexBuffer.GetVertex(t.I2));

        ScanlineRasterizer.Fill(
           surface,
           surface.ToScreen3(a.Proj), surface.ToScreen3(b.Proj), surface.ToScreen3(c.Proj),
           1f / a.Proj.W, 1f / b.Proj.W, 1f / c.Proj.W,
           default(EmptyVarying), default, default,
           new SolidColorShader(color),
           slice);
    }
}
