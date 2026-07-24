using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Lights;

namespace SoftEngine.Core.Rasterization.Painters;

/// <summary>
/// Perspective-correct textured fill with Gouraud lighting. Meshes without a texture
/// or UVs fall back to plain Gouraud shading, so mixed scenes still render sensibly.
/// Samples bilinearly from a mip level chosen per triangle by default; both can be
/// turned off to get the raw nearest-neighbour look back.
/// </summary>
public sealed class TexturedPainter(ILight? light = null, float ambient = 0.12f) : LitPainter(light, ambient)
{
    public TextureFiltering Filtering { get; set; } = TextureFiltering.Bilinear;

    public bool UseMipMaps { get; set; } = true;

    protected override void PrepareCore(Scene scene)
    {
        if (!UseMipMaps)
        {
            return;
        }

        // Mip chains are built here, before the parallel paint phase, so DrawTriangle
        // never mutates a texture from multiple threads.
        foreach (var mesh in scene.World.Meshes)
        {
            mesh.Texture?.EnsureMipMaps();
        }
    }

    public override void DrawTriangle(FrameBuffer surface, ColorRGB color, VertexBuffer vertexBuffer, int triangleIndice, in RowSlice slice)
    {
        ArgumentNullException.ThrowIfNull(vertexBuffer.Mesh, nameof(vertexBuffer));

        var mesh = vertexBuffer.Mesh;
        var t = vertexBuffer.GetTriangle(triangleIndice);
        t.TransformWorld(vertexBuffer);

        var (a, b, c) = (vertexBuffer.GetVertex(t.I0), vertexBuffer.GetVertex(t.I1), vertexBuffer.GetVertex(t.I2));

        var ia = LitIntensity(a.World, a.Norm);
        var ib = LitIntensity(b.World, b.Norm);
        var ic = LitIntensity(c.World, c.Norm);

        var p0 = surface.ToScreen3(a.Proj);
        var p1 = surface.ToScreen3(b.Proj);
        var p2 = surface.ToScreen3(c.Proj);

        var uvs = mesh.TexCoords;
        var texture = mesh.Texture;

        if (uvs is null || texture is null)
        {
            ScanlineRasterizer.Fill(
                surface,
                p0, p1, p2,
                1f / a.Proj.W, 1f / b.Proj.W, 1f / c.Proj.W,
                new IntensityVarying(ia), new IntensityVarying(ib), new IntensityVarying(ic),
                new LambertShader(color, GammaCorrect),
                StateFor(mesh),
                slice);
            return;
        }

        var uv0 = vertexBuffer.GetTexCoord(t.I0);
        var uv1 = vertexBuffer.GetTexCoord(t.I1);
        var uv2 = vertexBuffer.GetTexCoord(t.I2);

        var mipLevel = UseMipMaps
            ? MipSelector.Select(texture, p0, p1, p2, uv0, uv1, uv2)
            : 0;

        ScanlineRasterizer.Fill(
            surface,
            p0, p1, p2,
            1f / a.Proj.W, 1f / b.Proj.W, 1f / c.Proj.W,
            new TextureVarying(uv0, ia),
            new TextureVarying(uv1, ib),
            new TextureVarying(uv2, ic),
            new TexturedShader(texture, mipLevel, Filtering, GammaCorrect),
            StateFor(mesh),
            slice);
    }
}
