using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Rasterization.Shaders;
using SoftEngine.Core.Rasterization.Varyings;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Textures;
using System.Numerics;

namespace SoftEngine.Core.Rasterization.Painters;

public sealed class MaterialPainter(ILight? light = null, float ambient = 0.12f) : LitPainter(light, ambient)
{
    private Vector3 _eye;

    public TextureFiltering Filtering { get; set; } = TextureFiltering.Bilinear;

    public bool UseMipMaps { get; set; } = true;

    public float DefaultSpecularStrength { get; set; } = 0.35f;

    public float DefaultShininess { get; set; } = 32f;

    protected override void PrepareCore(Scene scene)
    {
        _eye = Matrix4x4.Invert(scene.Camera.ViewMatrix, out var inverseView)
            ? inverseView.Translation
            : scene.Camera.Position;

        foreach (var mesh in scene.World.Meshes)
        {
            if (mesh.Material is not { } material)
            {
                continue;
            }

            if (material.NeedsTangents)
            {
                mesh.EnsureTangents();
            }

            if (!UseMipMaps)
            {
                continue;
            }

            material.DiffuseMap?.EnsureMipMaps();
            material.NormalMap?.EnsureMipMaps();
            material.SpecularMap?.EnsureMipMaps();
        }
    }

    public override void DrawTriangle(FrameBuffer surface, ColorRGB color, VertexBuffer vertexBuffer, int triangleIndice, in ScreenTile tile)
    {
        ArgumentNullException.ThrowIfNull(vertexBuffer.Mesh, nameof(vertexBuffer));

        var mesh = vertexBuffer.Mesh;
        var t = vertexBuffer.GetTriangle(triangleIndice);

        var (a, b, c) = (vertexBuffer.GetVertex(t.I0), vertexBuffer.GetVertex(t.I1), vertexBuffer.GetVertex(t.I2));

        var p0 = surface.ToScreen3(a.Proj);
        var p1 = surface.ToScreen3(b.Proj);
        var p2 = surface.ToScreen3(c.Proj);

        var textured = mesh.TexCoords is not null;

        var uv0 = textured ? vertexBuffer.GetTexCoord(t.I0) : Vector2.Zero;
        var uv1 = textured ? vertexBuffer.GetTexCoord(t.I1) : Vector2.Zero;
        var uv2 = textured ? vertexBuffer.GetTexCoord(t.I2) : Vector2.Zero;

        var material = mesh.Material;

        var albedo = Bind(textured ? material?.DiffuseMap : null, p0, p1, p2, uv0, uv1, uv2, out var albedoMip);
        var normalMap = Bind(textured ? material?.NormalMap : null, p0, p1, p2, uv0, uv1, uv2);
        var specularMap = Bind(textured ? material?.SpecularMap : null, p0, p1, p2, uv0, uv1, uv2);

        var hasTangents = normalMap.HasTexture && mesh.Tangents is not null;

        var tangent0 = hasTangents ? vertexBuffer.GetTangent(t.I0) : Vector4.Zero;
        var tangent1 = hasTangents ? vertexBuffer.GetTangent(t.I1) : Vector4.Zero;
        var tangent2 = hasTangents ? vertexBuffer.GetTangent(t.I2) : Vector4.Zero;

        var shader = new MaterialShader(
            material?.Diffuse ?? color,
            albedo,
            normalMap,
            specularMap,
            Lights,
            _eye,
            AmbientLight,
            material?.SpecularStrength ?? DefaultSpecularStrength,
            material?.Shininess ?? DefaultShininess,
            material?.NormalStrength ?? 1f,
            GammaCorrect,
            Shadows);

        var v0 = new MaterialVarying(a.World, a.Norm, tangent0, uv0);
        var v1 = new MaterialVarying(b.World, b.Norm, tangent1, uv1);
        var v2 = new MaterialVarying(c.World, c.Norm, tangent2, uv2);

        var invW0 = 1f / a.Proj.W;
        var invW1 = 1f / b.Proj.W;
        var invW2 = 1f / c.Proj.W;

        var state = StateFor(mesh).WithMipLevel(albedoMip);

        if (textured && material is { IsCutout: true })
        {
            ScanlineRasterizer.Fill(
                surface, p0, p1, p2, invW0, invW1, invW2, v0, v1, v2,
                new CutoutShader<MaterialVarying, MaterialShader>(shader, albedo, material.AlphaCutoff),
                state,
                tile);
            return;
        }

        ScanlineRasterizer.Fill(
            surface, p0, p1, p2, invW0, invW1, invW2, v0, v1, v2,
            shader,
            state,
            tile);
    }

    private TextureSampler Bind(
        Texture? texture,
        in Vector3 p0, in Vector3 p1, in Vector3 p2,
        in Vector2 uv0, in Vector2 uv1, in Vector2 uv2) =>
        Bind(texture, p0, p1, p2, uv0, uv1, uv2, out _);

    private TextureSampler Bind(
        Texture? texture,
        in Vector3 p0, in Vector3 p1, in Vector3 p2,
        in Vector2 uv0, in Vector2 uv1, in Vector2 uv2,
        out int mipLevel)
    {
        if (texture is null)
        {
            mipLevel = -1;
            return default;
        }

        var mip = UseMipMaps
            ? MipSelector.SelectBlended(texture, Filtering, p0, p1, p2, uv0, uv1, uv2)
            : default;

        mipLevel = mip.Level;

        return new TextureSampler(texture, mip, Filtering);
    }
}
