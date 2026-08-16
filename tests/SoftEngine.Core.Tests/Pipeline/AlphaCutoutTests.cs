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

/// <summary>
/// Alpha-tested (cutout) materials: a texel below the material's cutoff is not drawn, does not
/// write depth, and does not block the light.
///
/// The distinction being tested throughout is between a cutout and the two things it is not.
/// A blended surface is see-through and writes no depth; an opaque one draws the whole quad.
/// A cutout draws part of the quad, at full opacity, with depth — and leaves the rest as
/// though the geometry were never there.
/// </summary>
public class AlphaCutoutTests
{
    private sealed class FixedCamera(Vector3 position) : ICamera
    {
        public Vector3 Position { get; set; } = position;

        public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Vector3.Zero, Vector3.UnitY);
    }

    /// <summary>
    /// A 2×1 texture: the left texel opaque white, the right one fully transparent white. The
    /// colour is the same on both sides on purpose — anything that shows up in the rendered
    /// frame is the <em>alpha</em> being read, not the colour.
    /// </summary>
    private static Texture SplitMask(byte leftAlpha = 255, byte rightAlpha = 0) =>
        new(2, 1, [
            unchecked((int)((uint)leftAlpha << 24 | 0x00FFFFFF)),
            unchecked((int)((uint)rightAlpha << 24 | 0x00FFFFFF)),
        ]);

    /// <summary>
    /// A camera-facing quad spanning x ∈ [-1, 1], UV.u running 0 → 1 with it, so the mask's
    /// left texel covers the left half of the quad and its right texel the right half.
    /// </summary>
    private static Mesh MaskedQuad(Texture mask, float cutoff, float z = 0f)
    {
        Vector3[] vertices = [new(-1, -1, z), new(1, -1, z), new(1, 1, z), new(-1, 1, z)];
        Triangle[] triangles = [new(0, 1, 2), new(2, 3, 0)];
        Vector3[] normals = [-Vector3.UnitZ, -Vector3.UnitZ, -Vector3.UnitZ, -Vector3.UnitZ];

        // The sampler treats u as spanning the whole image, so 0.25 and 0.75 are the centres
        // of the two texels — the quad's own halves.
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

    /// <summary>
    /// Alpha is filtered on the same footing as the colour — halfway between an opaque texel
    /// and a transparent one is half. Without it a cutout's edge would be a staircase of whole
    /// texels while the colour beside it was smooth.
    /// </summary>
    [Fact]
    public void SampleAlpha_Bilinear_BlendsBetweenTexels()
    {
        var sampler = new TextureSampler(SplitMask(), 0, TextureFiltering.Bilinear);

        // The midpoint between the two texel centres (0.25 and 0.75).
        Assert.Equal(0.5f, sampler.SampleAlpha(0.5f, 0.5f), 2);
    }

    #endregion

    #region The fill

    /// <summary>
    /// The whole claim, in one frame: half the quad is drawn and half is not, from a mask whose
    /// two halves differ in alpha alone.
    /// </summary>
    [Fact]
    public void Render_CutoutQuad_DrawsOnlyWhereTheMaskIsOpaque()
    {
        var (renderer, scene, surface) = MakeScene(MaskedQuad(SplitMask(), cutoff: 0.5f));

        renderer.Render(scene, new TexturedPainter());

        Assert.False(surface.IsBackground(20, 32));
        Assert.True(surface.IsBackground(44, 32));
    }

    /// <summary>
    /// A cut-out pixel must leave the depth buffer alone as well as the colour. If it writes
    /// depth, the hole it made occludes whatever is behind it — which looks like a
    /// transparent-sorting bug rather than like a cutout that half worked.
    /// </summary>
    [Fact]
    public void Render_CutoutQuad_LeavesNoDepthWhereItRejected()
    {
        var behind = MaskedQuad(SplitMask(leftAlpha: 255, rightAlpha: 255), cutoff: 0f, z: 2f);
        behind.TriangleColors[0] = ColorRGB.Red;
        behind.TriangleColors[1] = ColorRGB.Red;
        behind.Material.DiffuseMap = null;

        var (renderer, scene, surface) = MakeScene(MaskedQuad(SplitMask(), cutoff: 0.5f), behind);

        renderer.Render(scene, new TexturedPainter());

        // Through the hole, the far quad — not the background, and not the near quad.
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

    /// <summary>
    /// The cutout is a per-pixel test inside the fill, so it has to survive the two paths the
    /// fill has. A block-at-a-time span that rejected whole vectors would cut the silhouette
    /// into a vector-wide staircase and disagree with the scalar tail beside it.
    /// </summary>
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

    /// <summary>
    /// Every painter that samples an albedo map can cut out of it. The material and
    /// physically-based paths share one varying and one wrapper, but they are separate
    /// instantiations of the fill and each has to be handed the mask.
    /// </summary>
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

    /// <summary>
    /// A material may name a cutoff and the mesh still have no UVs to read the mask at.
    /// Sampling one anyway would read texel (0, 0) across the whole surface and either keep
    /// everything or reject everything — so the cutout is simply not applied.
    /// </summary>
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

    /// <summary>
    /// The half of the roadmap item that is not about the picture: a leaf that is a hole in the
    /// frame has to be a hole in the shadow too, or a canopy shades the ground as a solid disc.
    /// </summary>
    [Fact]
    public void ShadowMap_CutoutCaster_CastsThroughItsHoles()
    {
        var caster = MaskedQuad(SplitMask(), cutoff: 0.5f);

        var world = new SimpleWorld { Meshes = [caster], Lights = [] };

        // Straight down the +Z axis, which is the direction the quad faces — so the map is the
        // quad seen face on, and the mask maps onto it.
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

        // Roughly half the quad's footprint blocks the light and half does not; the exact
        // counts depend on the fit, so what is asserted is that both happened.
        Assert.True(written > 0, "the opaque half of the mask cast nothing");
        Assert.True(cleared > 0, "the transparent half of the mask still cast a shadow");
    }

    /// <summary>The same caster with no cutoff fills its whole footprint, as it always did.</summary>
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
