using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Shading;
using System.Numerics;

namespace SoftEngine.Core.Rasterization.Painters;

/// <summary>
/// The physically-based path: metallic-roughness materials lit by the scene's lights and by
/// its environment, through <see cref="PbrShader"/>.
///
/// It degrades one map at a time exactly as <see cref="MaterialPainter"/> does — a mesh with
/// no metallic or roughness map takes the material's scalars, and a mesh with no material at
/// all is a mid-grey dielectric lit from its triangle colour — so it can be switched on over
/// any scene in the viewer, not only ones authored for it.
/// </summary>
public sealed class PbrPainter(ILight? light = null, float ambient = 0.12f) : LitPainter(light, ambient)
{
    // The environment convolved per roughness, and what it was built from. Building walks
    // every texel of five cube maps, so it is kept until the environment or its intensity
    // changes — which, for a scene with a fixed sky, is never.
    private PrefilteredEnvironment? _prefiltered;
    private CubeMap? _prefilteredSource;
    private float _prefilteredIntensity = float.NaN;

    private Vector3 _eye;

    public TextureFiltering Filtering { get; set; } = TextureFiltering.Bilinear;

    public bool UseMipMaps { get; set; } = true;

    /// <summary>Roughness for meshes whose material has never been given one — a satin dielectric.</summary>
    public float DefaultRoughness { get; set; } = 0.5f;

    public float DefaultMetallic { get; set; }

    /// <summary>
    /// Edge length of the first prefiltered environment level. Higher is a sharper
    /// reflection on a near-mirror surface and a longer wait the first time a scene with a
    /// sky is drawn.
    /// </summary>
    public int EnvironmentResolution { get; set; } = 64;

    /// <summary>The environment this frame reflects, or null when the scene has none.</summary>
    public PrefilteredEnvironment? Environment => _prefiltered;

    protected override void PrepareCore(Scene scene)
    {
        // Camera.Position is the translation fed into the view matrix, not the eye's world
        // position — invert the view matrix to get the true eye point.
        _eye = Matrix4x4.Invert(scene.Camera.ViewMatrix, out var inverseView)
            ? inverseView.Translation
            : scene.Camera.Position;

        ResolveEnvironment(scene);

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
            material.MetallicMap?.EnsureMipMaps();
            material.RoughnessMap?.EnsureMipMaps();
            material.EmissiveMap?.EnsureMipMaps();
        }
    }

    /// <summary>
    /// Builds — or reuses — the prefiltered environment for this frame. It is scaled by the
    /// same <see cref="Scene.AmbientIntensity"/> the diffuse ambient uses, because the two
    /// are halves of one answer: how much light the surroundings actually deliver, as
    /// opposed to how bright the sky looks.
    /// </summary>
    private void ResolveEnvironment(Scene scene)
    {
        if (scene.Environment is not { } environment || !scene.AmbientFromEnvironment)
        {
            _prefiltered = null;
            _prefilteredSource = null;
            _prefilteredIntensity = float.NaN;
            return;
        }

        if (ReferenceEquals(environment, _prefilteredSource) && scene.AmbientIntensity == _prefilteredIntensity)
        {
            return;
        }

        _prefilteredSource = environment;
        _prefilteredIntensity = scene.AmbientIntensity;
        _prefiltered = PrefilteredEnvironment.Build(
            environment,
            System.Math.Max(4, EnvironmentResolution),
            intensity: scene.AmbientIntensity);
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

        var albedo = Bind(textured ? material?.DiffuseMap : null, p0, p1, p2, uv0, uv1, uv2, out var albedoMip);
        var normalMap = Bind(textured ? material?.NormalMap : null, p0, p1, p2, uv0, uv1, uv2);
        var metallicMap = Bind(textured ? material?.MetallicMap : null, p0, p1, p2, uv0, uv1, uv2);
        var roughnessMap = Bind(textured ? material?.RoughnessMap : null, p0, p1, p2, uv0, uv1, uv2);
        var emissiveMap = Bind(textured ? material?.EmissiveMap : null, p0, p1, p2, uv0, uv1, uv2);

        // Tangents only matter where a normal map will read them.
        var hasTangents = normalMap.HasTexture && mesh.Tangents is not null;

        var tangent0 = hasTangents ? vertexBuffer.GetTangent(t.I0) : Vector4.Zero;
        var tangent1 = hasTangents ? vertexBuffer.GetTangent(t.I1) : Vector4.Zero;
        var tangent2 = hasTangents ? vertexBuffer.GetTangent(t.I2) : Vector4.Zero;

        LinearColor emissive = material?.Emissive ?? ColorRGB.Black;

        var shader = new PbrShader(
            material?.Diffuse ?? color,
            albedo,
            normalMap,
            metallicMap,
            roughnessMap,
            emissiveMap,
            emissive * (material?.EmissiveStrength ?? 1f),
            material?.Metallic ?? DefaultMetallic,
            material?.Roughness ?? DefaultRoughness,
            material?.NormalStrength ?? 1f,
            Lights,
            _eye,
            AmbientLight,
            _prefiltered,
            Shadows);

        var v0 = new MaterialVarying(a.World, a.Norm, tangent0, uv0);
        var v1 = new MaterialVarying(b.World, b.Norm, tangent1, uv1);
        var v2 = new MaterialVarying(c.World, c.Norm, tangent2, uv2);

        var invW0 = 1f / a.Proj.W;
        var invW1 = 1f / b.Proj.W;
        var invW2 = 1f / c.Proj.W;

        var state = StateFor(mesh).WithMipLevel(albedoMip);

        // A cutout needs the mask bound, which needs UVs — a mesh without them has no cutout
        // to apply, whatever its material says.
        if (textured && material is { IsCutout: true })
        {
            ScanlineRasterizer.Fill(
                surface, p0, p1, p2, invW0, invW1, invW2, v0, v1, v2,
                new CutoutShader<MaterialVarying, PbrShader>(shader, albedo, material.AlphaCutoff),
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

    /// <summary>Binds one map at the mip level this triangle's screen footprint calls for.</summary>
    private TextureSampler Bind(
        Texture? texture,
        in Vector3 p0, in Vector3 p1, in Vector3 p2,
        in Vector2 uv0, in Vector2 uv1, in Vector2 uv2) =>
        Bind(texture, p0, p1, p2, uv0, uv1, uv2, out _);

    /// <inheritdoc cref="Bind(Texture?, in Vector3, in Vector3, in Vector3, in Vector2, in Vector2, in Vector2)"/>
    /// <param name="mipLevel">The level chosen, or -1 when there was no texture to choose one for.</param>
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

        mipLevel = UseMipMaps ? MipSelector.Select(texture, p0, p1, p2, uv0, uv1, uv2) : 0;

        return new TextureSampler(texture, mipLevel, Filtering);
    }
}
