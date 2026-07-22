using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Pipeline.Clipping;
using System.Numerics;

namespace SoftEngine.Core.Rasterization.Painters;

public sealed class WireFramePainter : IPainter
{
    private static readonly LiangBarskyClippingHomogeneous _liangBarskyClipping = new();

    // Lines cross arbitrary rows, so the slice is ignored — the renderer only calls
    // this painter from sequential code.
    public void DrawTriangle(FrameBuffer surface, ColorRGB color, VertexBuffer vertexBuffer, int triangleIndice, in RowSlice slice)
    {
        ArgumentNullException.ThrowIfNull(vertexBuffer.Mesh, nameof(vertexBuffer));

        var t = vertexBuffer.Mesh.Triangles[triangleIndice];

        var (t0, t1, t2) = (vertexBuffer.Vertices[t.I0], vertexBuffer.Vertices[t.I1], vertexBuffer.Vertices[t.I2]);

        Vector4 l0p0 = t0.Proj; 
        Vector4 l0p1 = t1.Proj;

        Vector4 l1p0 = t1.Proj; 
        Vector4 l1p1 = t2.Proj;

        Vector4 l2p0 = t0.Proj; 
        Vector4 l2p1 = t2.Proj;

        bool l0 = _liangBarskyClipping.Clip(ref l0p0, ref l0p1);
        bool l1 = _liangBarskyClipping.Clip(ref l1p0, ref l1p1);
        bool l2 = _liangBarskyClipping.Clip(ref l2p0, ref l2p1);

        if (l0)
        {
            surface.DrawLine(surface.ToScreen3(l0p0), surface.ToScreen3(l0p1), color);
        }
        if (l1)
        {
            surface.DrawLine(surface.ToScreen3(l1p0), surface.ToScreen3(l1p1), color);
        }
        if (l2)
        {
            surface.DrawLine(surface.ToScreen3(l2p0), surface.ToScreen3(l2p1), color);
        }
    }

    public static void DrawLine(FrameBuffer surface, ColorRGB color, Vector4 projectionP0, Vector4 projectionP1)
    {
        if (!_liangBarskyClipping.Clip(ref projectionP0, ref projectionP1))
        {
            return;
        }

        var p0 = surface.ToScreen3(projectionP0);
        var p1 = surface.ToScreen3(projectionP1);

        surface.DrawLine(p0, p1, color);
    }
}
