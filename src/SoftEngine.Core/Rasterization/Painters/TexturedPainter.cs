using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Rasterization.Shaders;
using SoftEngine.Core.Rasterization.Varyings;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Textures;

namespace SoftEngine.Core.Rasterization.Painters;

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

        foreach (var mesh in scene.World.Meshes)
        {
            mesh.Texture?.EnsureMipMaps();
        }
    }

    public override void DrawTriangle(FrameBuffer surface, ColorRGB color, VertexBuffer vertexBuffer, int triangleIndice, in ScreenTile tile)
    {
        ArgumentNullException.ThrowIfNull(vertexBuffer.Mesh, nameof(vertexBuffer));

        var mesh = vertexBuffer.Mesh;
        var t = vertexBuffer.GetTriangle(triangleIndice);

        var (a, b, c) = (vertexBuffer.GetVertex(t.I0), vertexBuffer.GetVertex(t.I1), vertexBuffer.GetVertex(t.I2));

        var ia = LitColor(a.World, a.Norm);
        var ib = LitColor(b.World, b.Norm);
        var ic = LitColor(c.World, c.Norm);

        var p0 = surface.ToScreen3(a.Proj);
        var p1 = surface.ToScreen3(b.Proj);
        var p2 = surface.ToScreen3(c.Proj);

        var uvs = mesh.TexCoords;
        var texture = mesh.Texture;

        if (uvs is null || texture is null)
        {
            Rasterizer.Fill(
                surface,
                p0, p1, p2,
                1f / a.Proj.W, 1f / b.Proj.W, 1f / c.Proj.W,
                new IntensityVarying(ia), new IntensityVarying(ib), new IntensityVarying(ic),
                new LambertShader(color, GammaCorrect),
                StateFor(mesh),
                tile);
            return;
        }

        var uv0 = vertexBuffer.GetTexCoord(t.I0);
        var uv1 = vertexBuffer.GetTexCoord(t.I1);
        var uv2 = vertexBuffer.GetTexCoord(t.I2);

        var mip = UseMipMaps
            ? MipSelector.SelectBlended(texture, Filtering, p0, p1, p2, uv0, uv1, uv2)
            : default;

        var albedo = new TextureSampler(texture, mip, Filtering);
        var shader = new TexturedShader(albedo, GammaCorrect);

        var state = StateFor(mesh).WithMipLevel(mip.Level);

        var v0 = new TextureVarying(uv0, ia);
        var v1 = new TextureVarying(uv1, ib);
        var v2 = new TextureVarying(uv2, ic);

        var invW0 = 1f / a.Proj.W;
        var invW1 = 1f / b.Proj.W;
        var invW2 = 1f / c.Proj.W;

        if (mesh.Material is { IsCutout: true } cutout)
        {
            Rasterizer.Fill(
                surface, p0, p1, p2, invW0, invW1, invW2, v0, v1, v2,
                new CutoutShader<TextureVarying, TexturedShader>(shader, albedo, cutout.AlphaCutoff),
                state,
                tile);
            return;
        }

        Rasterizer.Fill(
            surface, p0, p1, p2, invW0, invW1, invW2, v0, v1, v2,
            shader,
            state,
            tile);
    }
}
