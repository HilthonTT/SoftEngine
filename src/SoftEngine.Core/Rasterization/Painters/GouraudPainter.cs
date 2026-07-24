using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Scenes.Lights;

namespace SoftEngine.Core.Rasterization.Painters;

/// <summary>Per-vertex Lambert intensity, interpolated across the triangle.</summary>
public sealed class GouraudPainter(ILight? light = null, float ambient = 0.12f) : LitPainter(light, ambient)
{
    public override void DrawTriangle(FrameBuffer surface, ColorRGB color, VertexBuffer vertexBuffer, int triangleIndice, in RowSlice slice)
    {
        ArgumentNullException.ThrowIfNull(vertexBuffer.Mesh, nameof(vertexBuffer));

        var t = vertexBuffer.GetTriangle(triangleIndice);
        t.TransformWorld(vertexBuffer);

        var (a, b, c) = (vertexBuffer.GetVertex(t.I0), vertexBuffer.GetVertex(t.I1), vertexBuffer.GetVertex(t.I2));

        ScanlineRasterizer.Fill(
            surface,
            surface.ToScreen3(a.Proj), surface.ToScreen3(b.Proj), surface.ToScreen3(c.Proj),
            1f / a.Proj.W, 1f / b.Proj.W, 1f / c.Proj.W,
            new IntensityVarying(LitIntensity(a.World, a.Norm)),
            new IntensityVarying(LitIntensity(b.World, b.Norm)),
            new IntensityVarying(LitIntensity(c.World, c.Norm)),
            new LambertShader(color, GammaCorrect),
            StateFor(vertexBuffer.Mesh),
            slice);
    }
}
