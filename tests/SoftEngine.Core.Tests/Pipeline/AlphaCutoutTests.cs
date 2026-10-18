using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Pipeline.Shadows;
using SoftEngine.Core.Rasterization;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Cameras;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Scenes.Projections;
using SoftEngine.Core.Textures;
using System.Numerics;

namespace SoftEngine.Core.Tests.Pipeline;

public class AlphaCutoutTests
{
    private sealed class FixedCamera(Vector3 position) : ICamera
    {
        public Vector3 Position { get; set; } = position;

        public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Vector3.Zero, Vector3.UnitY);
    }

    private static Texture SplitMask(byte leftAlpha = 255, byte rightAlpha = 0) =>
        new(2, 1, [
            unchecked((int)((uint)leftAlpha << 24 | 0x00FFFFFF)),
            unchecked((int)((uint)rightAlpha << 24 | 0x00FFFFFF)),
        ]);

    private static Mesh MaskedQuad(Texture mask, float cutoff, float z = 0f)
    {
        Vector3[] vertices = [new(-1, -1, z), new(1, -1, z), new(1, 1, z), new(-1, 1, z)];
        Triangle[] triangles = [new(0, 1, 2), new(2, 3, 0)];
        Vector3[] normals = [-Vector3.UnitZ, -Vector3.UnitZ, -Vector3.UnitZ, -Vector3.UnitZ];

        var mesh = new Mesh(vertices, triangles, normals, [ColorRGB.White, ColorRGB.White])
        {
            TexCoords = [new(0.25f, 0f), new(0.75f, 0f), new(0.75f, 1f), new(0.25f, 1f)],
        };

        mesh.Material.DiffuseMap = mask;
        mesh.Material.AlphaCutoff = cutoff;

        return mesh;
    }

    private static (Renderer Renderer, Scene Scene, FrameBuffer Surface) MakeScene(params IMesh[] meshes)
    {
        var renderer = new Renderer();
        var surface = new FrameBuffer(64, 64) { Stats = renderer.Stats };

        renderer.Settings.BackFaceCulling = false;

        var scene = new Scene
        {
            Surface = surface,
            Camera = new FixedCamera(new Vector3(0, 0, 5)),
            Projection = new PerspectiveProjection(MathF.PI / 4f, 1f, 100f),
            World = new SimpleWorld { Meshes = [.. meshes], Lights = [] },
        };

        return (renderer, scene, surface);
    }

    #region Sampling the mask

    [Fact]
    public void SampleAlpha_WithNoTexture_CoversEverything()
    {
        var sampler = new TextureSampler(null, 0, TextureFiltering.Nearest);

        Assert.False(sampler.HasTexture);
        Assert.Equal(1f, sampler.SampleAlpha(new Vector2(0.5f, 0.5f)), 3);
    }

    [Fact]
    public void SampleAlpha_Nearest_ReadsTheTexelsOwnAlpha()
    {
        var sampler = new TextureSampler(SplitMask(), 0, TextureFiltering.Nearest);

        Assert.Equal(1f, sampler.SampleAlpha(0.25f, 0.5f), 3);
        Assert.Equal(0f, sampler.SampleAlpha(0.75f, 0.5f), 3);
    }

    [Fact]
    public void SampleAlpha_Bilinear_BlendsBetweenTexels()
    {
        var sampler = new TextureSampler(SplitMask(), 0, TextureFiltering.Bilinear);

        Assert.Equal(0.5f, sampler.SampleAlpha(0.5f, 0.5f), 2);
    }

    #endregion

    #region The fill

    [Fact]
    public void Render_CutoutQuad_DrawsOnlyWhereTheMaskIsOpaque()
    {
        var (renderer, scene, surface) = MakeScene(MaskedQuad(SplitMask(), cutoff: 0.5f));

        renderer.Render(scene, new TexturedPainter());

        Assert.False(surface.IsBackground(20, 32));
        Assert.True(surface.IsBackground(44, 32));
    }

    [Fact]
    public void Render_CutoutQuad_LeavesNoDepthWhereItRejected()
    {
        var behind = MaskedQuad(SplitMask(leftAlpha: 255, rightAlpha: 255), cutoff: 0f, z: 2f);
        behind.TriangleColors[0] = ColorRGB.Red;
        behind.TriangleColors[1] = ColorRGB.Red;
        behind.Material.DiffuseMap = null;

        var (renderer, scene, surface) = MakeScene(MaskedQuad(SplitMask(), cutoff: 0.5f), behind);

        renderer.Render(scene, new TexturedPainter());

        Assert.False(surface.IsBackground(44, 32));

        var throughTheHole = ColorRGB.FromPacked(surface.GetColor(44, 32));
        Assert.True(throughTheHole.R > throughTheHole.B,
            $"expected the red quad behind the cutout, got {throughTheHole.R},{throughTheHole.G},{throughTheHole.B}");
    }

    [Fact]
    public void Render_CutoffOfZero_IsNoCutoutAtAll()
    {
        var (renderer, scene, surface) = MakeScene(MaskedQuad(SplitMask(), cutoff: 0f));

        renderer.Render(scene, new TexturedPainter());

        Assert.False(surface.IsBackground(20, 32));
        Assert.False(surface.IsBackground(44, 32));
    }

    [Fact]
    public void Render_CutoutQuad_VectorAndScalarSpansAgreeExactly()
    {
        var original = ScanlineRasterizer.VectorizedSpans;

        try
        {
            ScanlineRasterizer.VectorizedSpans = true;
            var (rendererA, sceneA, surfaceA) = MakeScene(MaskedQuad(SplitMask(), cutoff: 0.5f));
            rendererA.Render(sceneA, new TexturedPainter());

            ScanlineRasterizer.VectorizedSpans = false;
            var (rendererB, sceneB, surfaceB) = MakeScene(MaskedQuad(SplitMask(), cutoff: 0.5f));
            rendererB.Render(sceneB, new TexturedPainter());

            Assert.Equal(surfaceB.Screen, surfaceA.Screen);
        }
        finally
        {
            ScanlineRasterizer.VectorizedSpans = original;
        }
    }

    [Theory]
    [InlineData("textured")]
    [InlineData("material")]
    [InlineData("pbr")]
    public void Render_CutoutQuad_IsHonouredByEveryTexturedPainter(string painter)
    {
        var (renderer, scene, surface) = MakeScene(MaskedQuad(SplitMask(), cutoff: 0.5f));

        IPainter chosen = painter switch
        {
            "textured" => new TexturedPainter(),
            "material" => new MaterialPainter(),
            _ => new PbrPainter(),
        };

        renderer.Render(scene, chosen);

        Assert.False(surface.IsBackground(20, 32));
        Assert.True(surface.IsBackground(44, 32));
    }

    [Fact]
    public void Render_CutoutWithoutTexCoords_DrawsTheWholeSurface()
    {
        var quad = MaskedQuad(SplitMask(), cutoff: 0.5f);
        quad.TexCoords = null;

        var (renderer, scene, surface) = MakeScene(quad);

        renderer.Render(scene, new MaterialPainter());

        Assert.False(surface.IsBackground(20, 32));
        Assert.False(surface.IsBackground(44, 32));
    }

    #endregion

    #region Shadows

    [Fact]
    public void ShadowMap_CutoutCaster_CastsThroughItsHoles()
    {
        var caster = MaskedQuad(SplitMask(), cutoff: 0.5f);

        var world = new SimpleWorld { Meshes = [caster], Lights = [] };

        var light = new DirectionalLight { Direction = new Vector3(0, 0, 1) };

        var renderer = new ShadowMapRenderer();
        var map = renderer.Render(world, light, new ShadowSettings { Enabled = true, Resolution = 64 });

        Assert.NotNull(map);

        var depth = map.DepthOf(0);

        var written = 0;
        var cleared = 0;

        foreach (var z in depth)
        {
            if (z < 1f)
            {
                written++;
            }
            else
            {
                cleared++;
            }
        }

        Assert.True(written > 0, "the opaque half of the mask cast nothing");
        Assert.True(cleared > 0, "the transparent half of the mask still cast a shadow");
    }

    [Fact]
    public void ShadowMap_WithoutACutoff_CastsTheWholeQuad()
    {
        var masked = ShadowFootprint(cutoff: 0.5f);
        var solid = ShadowFootprint(cutoff: 0f);

        Assert.True(solid > masked,
            $"a cutout caster should block less light than a solid one; solid {solid}, masked {masked}");
    }

    private static int ShadowFootprint(float cutoff)
    {
        var world = new SimpleWorld { Meshes = [MaskedQuad(SplitMask(), cutoff)], Lights = [] };

        var map = new ShadowMapRenderer()
            .Render(world, new DirectionalLight { Direction = new Vector3(0, 0, 1) }, new ShadowSettings { Enabled = true, Resolution = 64 });

        Assert.NotNull(map);

        var written = 0;

        foreach (var z in map.DepthOf(0))
        {
            if (z < 1f)
            {
                written++;
            }
        }

        return written;
    }

    #endregion
}
