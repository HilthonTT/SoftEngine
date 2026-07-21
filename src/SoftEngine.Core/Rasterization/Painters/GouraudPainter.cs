using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Shading;
using System.Numerics;

namespace SoftEngine.Core.Rasterization.Painters;

public sealed class GouraudPainter(Vector3 lightPosition) : IPainter
{
    private readonly Vector3 _lightPosition = lightPosition;

    public GouraudPainter() : this(new Vector3(0, 10, 10))
    {
    }

    public void DrawTriangle(FrameBuffer surface, ColorRGB color, VertexBuffer vertexBuffer, int triangleIndice)
    {
        ArgumentNullException.ThrowIfNull(vertexBuffer.Mesh, nameof(vertexBuffer));

        var t = vertexBuffer.Mesh.Triangles[triangleIndice];
        t.TransformWorld(vertexBuffer);

        var (a, b, c) = (vertexBuffer.Vertices[t.I0], vertexBuffer.Vertices[t.I1], vertexBuffer.Vertices[t.I2]);

        ScanlineRasterizer.Fill(
            surface,
            surface.ToScreen3(a.Proj), surface.ToScreen3(b.Proj), surface.ToScreen3(c.Proj),
            new IntensityVarying(LambertLighting.ComputeNDotL(a.World, a.Norm, _lightPosition)),
            new IntensityVarying(LambertLighting.ComputeNDotL(b.World, b.Norm, _lightPosition)),
            new IntensityVarying(LambertLighting.ComputeNDotL(c.World, c.Norm, _lightPosition)),
            new LambertShader(color));
    }
}
