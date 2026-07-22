using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Scenes.Lights;

namespace SoftEngine.Core.Rasterization.Painters;

/// <summary>
/// Perspective-correct textured fill with Gouraud lighting. Meshes without a texture
/// or UVs fall back to plain Gouraud shading, so mixed scenes still render sensibly.
/// </summary>
public sealed class TexturedPainter(ILight? light = null, float ambient = 0.12f) : LitPainter(light, ambient)
{
    public override void DrawTriangle(FrameBuffer surface, ColorRGB color, VertexBuffer vertexBuffer, int triangleIndice, in RowSlice slice)
    {
        ArgumentNullException.ThrowIfNull(vertexBuffer.Mesh, nameof(vertexBuffer));

        var mesh = vertexBuffer.Mesh;
        var t = mesh.Triangles[triangleIndice];
        t.TransformWorld(vertexBuffer);

        var (a, b, c) = (vertexBuffer.Vertices[t.I0], vertexBuffer.Vertices[t.I1], vertexBuffer.Vertices[t.I2]);

        var ia = LitIntensity(a.World, a.Norm);
        var ib = LitIntensity(b.World, b.Norm);
        var ic = LitIntensity(c.World, c.Norm);

        var uvs = mesh.TexCoords;
        var texture = mesh.Texture;

        if (uvs is null || texture is null)
        {
            ScanlineRasterizer.Fill(
                surface,
                surface.ToScreen3(a.Proj), surface.ToScreen3(b.Proj), surface.ToScreen3(c.Proj),
                1f / a.Proj.W, 1f / b.Proj.W, 1f / c.Proj.W,
                new IntensityVarying(ia), new IntensityVarying(ib), new IntensityVarying(ic),
                new LambertShader(color),
                slice);
            return;
        }

        ScanlineRasterizer.Fill(
            surface,
            surface.ToScreen3(a.Proj), surface.ToScreen3(b.Proj), surface.ToScreen3(c.Proj),
            1f / a.Proj.W, 1f / b.Proj.W, 1f / c.Proj.W,
            new TextureVarying(uvs[t.I0], ia),
            new TextureVarying(uvs[t.I1], ib),
            new TextureVarying(uvs[t.I2], ic),
            new TexturedShader(texture),
            slice);
    }
}
