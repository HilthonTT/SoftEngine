using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Shading;

namespace SoftEngine.Core.Rasterization.Painters;

/// <summary>One Lambert intensity per triangle, from its centroid and averaged normal.</summary>
public sealed class FlatPainter(ILight? light = null, float ambient = 0.12f) : LitPainter(light, ambient)
{
    public override void DrawTriangle(FrameBuffer surface, ColorRGB color, VertexBuffer vertexBuffer, int triangleIndice, in RowSlice slice)
    {
        ArgumentNullException.ThrowIfNull(vertexBuffer.Mesh, nameof(vertexBuffer));

        var t = vertexBuffer.GetTriangle(triangleIndice);
        t.TransformWorld(vertexBuffer);

        var (a, b, c) = (vertexBuffer.GetVertex(t.I0), vertexBuffer.GetVertex(t.I1), vertexBuffer.GetVertex(t.I2));

        var normal = (a.Norm + b.Norm + c.Norm) / 3f;
        var centroid = (a.World + b.World + c.World) / 3f;

        var intensity = LitIntensity(centroid, normal);
        var lit = GammaCorrect ? ColorSpace.ScaleLinear(color, intensity) : intensity * color;

        ScanlineRasterizer.Fill(
            surface,
            surface.ToScreen3(a.Proj), surface.ToScreen3(b.Proj), surface.ToScreen3(c.Proj),
            1f / a.Proj.W, 1f / b.Proj.W, 1f / c.Proj.W,
            default(EmptyVarying), default, default,
            new SolidColorShader(lit),
            StateFor(vertexBuffer.Mesh),
            slice);
    }
}
