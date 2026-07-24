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

        var mipLevel = 0;
        if (UseMipMaps && texture.MipCount > 1)
        {
            mipLevel = SelectMipLevel(texture, p0, p1, p2, uv0, uv1, uv2);
        }

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

    /// <summary>
    /// One mip level per triangle, from the ratio of its texel footprint to its screen
    /// area: level 0 when a texel maps to a pixel or more, one level up for every 4×
    /// more texels than pixels. Cruder than per-pixel derivatives, but a triangle is a
    /// small enough unit in practice — and it keeps the per-pixel path branch-free.
    /// </summary>
    private static int SelectMipLevel(
        Texture texture,
        in System.Numerics.Vector3 p0, in System.Numerics.Vector3 p1, in System.Numerics.Vector3 p2,
        in System.Numerics.Vector2 uv0, in System.Numerics.Vector2 uv1, in System.Numerics.Vector2 uv2)
    {
        var screenArea = MathF.Abs(ScanlineRasterizer.Cross2D(p0, p1, p2)) * 0.5f;
        if (screenArea <= 0f)
        {
            return 0;
        }

        var texelArea = MathF.Abs(
            (uv1.X - uv0.X) * (uv2.Y - uv0.Y) - (uv1.Y - uv0.Y) * (uv2.X - uv0.X))
            * 0.5f * texture.Width * texture.Height;
        if (texelArea <= 0f)
        {
            return 0;
        }

        var level = (int)(0.5f * MathF.Log2(texelArea / screenArea) + 0.5f);
        return System.Math.Clamp(level, 0, texture.MipCount - 1);
    }
}
