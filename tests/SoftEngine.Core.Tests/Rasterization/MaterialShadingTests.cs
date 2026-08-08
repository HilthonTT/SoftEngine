using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Geometry.Primitives;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Rasterization;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Rasterization.Shaders;
using SoftEngine.Core.Rasterization.Varyings;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Cameras;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Scenes.Projections;
using SoftEngine.Core.Shading;
using SoftEngine.Core.Textures;
using System.Numerics;

namespace SoftEngine.Core.Tests;

public class MaterialShadingTests
{
    private sealed class FixedCamera(Vector3 position) : ICamera
    {
        public Vector3 Position { get; set; } = position;

        public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Vector3.Zero, Vector3.UnitY);
    }

    private static Texture Fill(byte r, byte g, byte b) =>
        new(2, 2, [.. Enumerable.Repeat(new ColorRGB(r, g, b).Color, 4)]);

    /// <summary>A surface facing +Z at the origin, lit from straight ahead.</summary>
    private static MaterialShader Shader(
        Texture? normalMap,
        Texture? specularMap = null,
        float specularStrength = 0f,
        ColorRGB? baseColor = null) =>
        new(
            baseColor ?? ColorRGB.White,
            default,
            new TextureSampler(normalMap, 0, TextureFiltering.Nearest),
            new TextureSampler(specularMap, 0, TextureFiltering.Nearest),
            LightSet.Of(new DirectionalLight { Direction = -Vector3.UnitZ }),
            eye: new Vector3(0, 0, 5f),
            ambient: new AmbientCube(0.1f),
            specularStrength: specularStrength,
            shininess: 32f,
            normalStrength: 1f,
            gammaCorrect: false,
            shadows: null);

    private static MaterialVarying Varying() =>
        new(Vector3.Zero, Vector3.UnitZ, new Vector4(1, 0, 0, 1), new Vector2(0.5f, 0.5f));

    [Fact]
    public void Shade_FlatNormalMap_MatchesShadingWithoutOne()
    {
        // (128, 128, 255) decodes to very nearly +Z — a normal map that changes nothing.
        var withMap = Shader(Fill(128, 128, 255)).Shade(Varying());
        var withoutMap = Shader(null).Shade(Varying());

        Assert.InRange(withMap.R, withoutMap.R - 2, withoutMap.R + 2);
    }

    [Fact]
    public void Shade_TiltedNormalMap_TurnsTheSurfaceAwayFromTheLight()
    {
        // Full red tilts the normal all the way along the tangent, so it no longer faces
        // the light and the Lambert term collapses to the ambient floor.
        var tilted = Shader(Fill(255, 128, 128)).Shade(Varying());
        var flat = Shader(Fill(128, 128, 255)).Shade(Varying());

        Assert.True(tilted.R < flat.R);
    }

    [Fact]
    public void Shade_WithoutATangent_IgnoresTheNormalMap()
    {
        var shader = Shader(Fill(255, 128, 128));

        var untangented = new MaterialVarying(Vector3.Zero, Vector3.UnitZ, Vector4.Zero, new Vector2(0.5f, 0.5f));

        Assert.Equal(Shader(null).Shade(Varying()).ToColorRGB().Color, shader.Shade(untangented).ToColorRGB().Color);
    }

    [Fact]
    public void Shade_SpecularMap_MasksTheHighlight()
    {
        // A dark base colour, or the diffuse term alone already saturates the channel and
        // the highlight has nowhere to show.
        var dark = new ColorRGB(20, 20, 20);

        var lit = Shader(null, Fill(255, 255, 255), specularStrength: 1f, baseColor: dark).Shade(Varying());
        var masked = Shader(null, Fill(0, 0, 0), specularStrength: 1f, baseColor: dark).Shade(Varying());

        Assert.True(lit.R > masked.R);
    }

    [Fact]
    public void Mesh_TextureAndMaterialDiffuseMap_AreTheSameThing()
    {
        var texture = Fill(10, 20, 30);
        var mesh = new Cube { Texture = texture };

        Assert.Same(texture, mesh.Material.DiffuseMap);

        mesh.Material.DiffuseMap = null;

        Assert.Null(mesh.Texture);
    }

    [Fact]
    public void Render_MaterialPainter_ShadesAMeshWithNoMapsAtAll()
    {
        var renderer = new Renderer();
        var surface = new FrameBuffer(128, 128) { Stats = renderer.Stats };

        var scene = new Scene
        {
            Surface = surface,
            Camera = new FixedCamera(new Vector3(0, 0, 5f)),
            Projection = new PerspectiveProjection(MathF.PI / 4f, 0.1f, 100f),
            World = new SimpleWorld
            {
                Meshes = [new Cube()],
                Lights = [new DirectionalLight { Direction = new Vector3(-0.3f, -0.4f, -1f) }],
            },
        };

        renderer.Render(scene, new MaterialPainter());

        Assert.True(renderer.Stats.DrawnPixelCount > 0);
        Assert.NotEqual(0, surface.GetColor(64, 64));
    }

    [Fact]
    public void Render_MaterialPainter_BuildsTangentsForMeshesWithANormalMap()
    {
        var renderer = new Renderer();
        var surface = new FrameBuffer(128, 128) { Stats = renderer.Stats };

        var cube = new TexturedCube { Scale = new Vector3(2, 2, 2) };
        cube.Material.DiffuseMap = Texture.Checkerboard(16, 4, ColorRGB.White, ColorRGB.Gray);
        cube.Material.NormalMap = NormalMapBuilder.FromHeight(Texture.Bumps(16, 2));

        var scene = new Scene
        {
            Surface = surface,
            Camera = new FixedCamera(new Vector3(0, 0, 6f)),
            Projection = new PerspectiveProjection(MathF.PI / 4f, 0.1f, 100f),
            World = new SimpleWorld
            {
                Meshes = [cube],
                Lights = [new DirectionalLight { Direction = new Vector3(-0.3f, -0.4f, -1f) }],
            },
        };

        Assert.Null(cube.Tangents);

        renderer.Render(scene, new MaterialPainter());

        Assert.NotNull(cube.Tangents);
        Assert.Equal(cube.Vertices.Length, cube.Tangents.Length);
        Assert.True(renderer.Stats.DrawnPixelCount > 0);
    }

    [Fact]
    public void FromHeight_FlatHeightMap_EncodesAFlatNormal()
    {
        var flat = new Texture(8, 8, [.. Enumerable.Repeat(new ColorRGB(128, 128, 128).Color, 64)]);

        var normals = NormalMapBuilder.FromHeight(flat);

        var texel = normals.Sample(0.5f, 0.5f);

        Assert.InRange(texel.R, 127, 129);
        Assert.InRange(texel.G, 127, 129);
        Assert.Equal(255, texel.B);
    }

    [Fact]
    public void FromHeight_ASlope_TiltsTheNormalAcrossIt()
    {
        // A horizontal ramp: height grows with x, so the normal leans along x and nowhere else.
        var pixels = new int[8 * 8];
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                var level = (byte)(x * 32);
                pixels[x + y * 8] = new ColorRGB(level, level, level).Color;
            }
        }

        var normals = NormalMapBuilder.FromHeight(new Texture(8, 8, pixels), 2f);

        // Sample in the middle of the ramp, away from the wrap-around at the edges.
        var texel = normals.Sample(4.5f / 8f, 0.5f);

        Assert.NotEqual(128, texel.R);
        Assert.InRange(texel.G, 127, 129);
    }
}
