using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Shading;
using System.Numerics;

namespace SoftEngine.Core.Rasterization.Painters;

public sealed class FlatPainter(Vector3 lightPosition) : IPainter
{
    private readonly Vector3 _lightPosition = lightPosition;

    public FlatPainter() : this(new Vector3(0, 10, 10))
    {
    }

    public void DrawTriangle(FrameBuffer surface, ColorRGB color, VertexBuffer vertexBuffer, int triangleIndice)
    {
        ArgumentNullException.ThrowIfNull(vertexBuffer.Mesh, nameof(vertexBuffer));

        var t = vertexBuffer.Mesh.Triangles[triangleIndice];

        var (a, b, c) = (vertexBuffer.Vertices[t.I0], vertexBuffer.Vertices[t.I1], vertexBuffer.Vertices[t.I2]);

        var normal = (a.Norm + b.Norm + c.Norm) / 3f;
        var centroid = (a.World + b.World + c.World) / 3f;

        var lit = LambertLighting.ComputeNDotL(centroid, normal, _lightPosition) * color;

        ScanlineRasterizer.Fill(
            surface,
            surface.ToScreen3(a.Proj), surface.ToScreen3(b.Proj), surface.ToScreen3(c.Proj),
            default(EmptyVarying), default, default,
            new SolidColorShader(lit));
    }
}
