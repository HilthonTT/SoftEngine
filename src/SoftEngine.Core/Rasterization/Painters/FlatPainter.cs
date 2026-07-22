using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Scenes.Lights;

namespace SoftEngine.Core.Rasterization.Painters;

/// <summary>One Lambert intensity per triangle, from its centroid and averaged normal.</summary>
public sealed class FlatPainter(ILight? light = null, float ambient = 0.05f) : LitPainter(light, ambient)
{
    public override void DrawTriangle(FrameBuffer surface, ColorRGB color, VertexBuffer vertexBuffer, int triangleIndice)
    {
        ArgumentNullException.ThrowIfNull(vertexBuffer.Mesh, nameof(vertexBuffer));

        var t = vertexBuffer.Mesh.Triangles[triangleIndice];
        t.TransformWorld(vertexBuffer);

        var (a, b, c) = (vertexBuffer.Vertices[t.I0], vertexBuffer.Vertices[t.I1], vertexBuffer.Vertices[t.I2]);

        var normal = (a.Norm + b.Norm + c.Norm) / 3f;
        var centroid = (a.World + b.World + c.World) / 3f;

        var lit = LitIntensity(centroid, normal) * color;

        ScanlineRasterizer.Fill(
            surface,
            surface.ToScreen3(a.Proj), surface.ToScreen3(b.Proj), surface.ToScreen3(c.Proj),
            1f / a.Proj.W, 1f / b.Proj.W, 1f / c.Proj.W,
            default(EmptyVarying), default, default,
            new SolidColorShader(lit));
    }
}
