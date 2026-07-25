using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Lights;
using System.Numerics;

namespace SoftEngine.Core.Rasterization.Painters;

/// <summary>
/// Shades every pixel from the mesh's <see cref="Material"/>: albedo, normal and specular
/// maps over per-pixel Blinn-Phong, with shadows when the scene casts them.
///
/// It is the most complete painter, and degrades one map at a time rather than all at once
/// — a mesh with no normal map still gets its albedo, and a mesh with no material at all
/// still gets lit from its triangle colour. Mip chains and tangent frames are built in
/// <see cref="PrepareCore"/>, before the parallel paint phase, because both mutate the
/// mesh the first time they are needed.
/// </summary>
public sealed class MaterialPainter(ILight? light = null, float ambient = 0.12f) : LitPainter(light, ambient)
{
    private Vector3 _eye;

    public TextureFiltering Filtering { get; set; } = TextureFiltering.Bilinear;

    public bool UseMipMaps { get; set; } = true;

    /// <summary>Applied to meshes whose material sets none of its own — the same defaults Phong uses.</summary>
    public float DefaultSpecularStrength { get; set; } = 0.35f;

    public float DefaultShininess { get; set; } = 32f;

    protected override void PrepareCore(Scene scene)
    {
        // Camera.Position is the translation fed into the view matrix, not the eye's
        // world position — invert the view matrix to get the true eye point.
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
        t.TransformWorld(vertexBuffer);

        var (a, b, c) = (vertexBuffer.GetVertex(t.I0), vertexBuffer.GetVertex(t.I1), vertexBuffer.GetVertex(t.I2));

        var p0 = surface.ToScreen3(a.Proj);
        var p1 = surface.ToScreen3(b.Proj);
        var p2 = surface.ToScreen3(c.Proj);

        // Meshes without UVs shade from the flat material colour; sampling a map without
        // them would read texel (0, 0) across the whole surface.
        var textured = mesh.TexCoords is not null;

        var uv0 = textured ? vertexBuffer.GetTexCoord(t.I0) : Vector2.Zero;
        var uv1 = textured ? vertexBuffer.GetTexCoord(t.I1) : Vector2.Zero;
        var uv2 = textured ? vertexBuffer.GetTexCoord(t.I2) : Vector2.Zero;

        var material = mesh.Material;

        var albedo = Bind(textured ? material?.DiffuseMap : null, p0, p1, p2, uv0, uv1, uv2);
        var normalMap = Bind(textured ? material?.NormalMap : null, p0, p1, p2, uv0, uv1, uv2);
        var specularMap = Bind(textured ? material?.SpecularMap : null, p0, p1, p2, uv0, uv1, uv2);

        // Tangents only matter where a normal map will read them.
        var hasTangents = normalMap.HasTexture && mesh.Tangents is not null;

        var tangent0 = hasTangents ? vertexBuffer.GetTangent(t.I0) : Vector4.Zero;
        var tangent1 = hasTangents ? vertexBuffer.GetTangent(t.I1) : Vector4.Zero;
        var tangent2 = hasTangents ? vertexBuffer.GetTangent(t.I2) : Vector4.Zero;

        // Resolve the light to plain vectors so the per-pixel shader stays dispatch-free.
        var (lightVector, isDirectional) = Light switch
        {
            DirectionalLight d => (d.DirectionFrom(Vector3.Zero), true),
            PointLight p => (p.Position, false),
            _ => (Light.DirectionFrom((a.World + b.World + c.World) / 3f), true),
        };

        var shader = new MaterialShader(
            material?.Diffuse ?? color,
            albedo,
            normalMap,
            specularMap,
            lightVector,
            isDirectional,
            Light.Intensity,
            _eye,
            Ambient,
            material?.SpecularStrength ?? DefaultSpecularStrength,
            material?.Shininess ?? DefaultShininess,
            material?.NormalStrength ?? 1f,
            GammaCorrect,
            Shadows);

        ScanlineRasterizer.Fill(
            surface,
            p0, p1, p2,
            1f / a.Proj.W, 1f / b.Proj.W, 1f / c.Proj.W,
            new MaterialVarying(a.World, a.Norm, tangent0, uv0),
            new MaterialVarying(b.World, b.Norm, tangent1, uv1),
            new MaterialVarying(c.World, c.Norm, tangent2, uv2),
            shader,
            StateFor(mesh),
            tile);
    }

    /// <summary>Binds one map at the mip level this triangle's screen footprint calls for.</summary>
    private TextureSampler Bind(
        Texture? texture,
        in Vector3 p0, in Vector3 p1, in Vector3 p2,
        in Vector2 uv0, in Vector2 uv1, in Vector2 uv2)
    {
        if (texture is null)
        {
            return default;
        }

        var mipLevel = UseMipMaps ? MipSelector.Select(texture, p0, p1, p2, uv0, uv1, uv2) : 0;

        return new TextureSampler(texture, mipLevel, Filtering);
    }
}
