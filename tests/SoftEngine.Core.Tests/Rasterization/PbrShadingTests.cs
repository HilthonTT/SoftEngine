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

namespace SoftEngine.Core.Tests.Rasterization;

public class PbrShadingTests
{
    private sealed class FixedCamera(Vector3 position) : ICamera
    {
        public Vector3 Position { get; set; } = position;

        public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Vector3.Zero, Vector3.UnitY);
    }

    /// <summary>
    /// A surface at the origin facing +Z, with the eye and one white light straight in front
    /// of it, so every term of the model is evaluated head-on and the answer can be reasoned
    /// about by hand.
    /// </summary>
    private static PbrShader HeadOn(
        ColorRGB baseColor,
        float metallic,
        float roughness,
        AmbientCube? ambient = null,
        PrefilteredEnvironment? environment = null,
        LinearColor emissive = default)
    {
        var light = new DirectionalLight { Direction = -Vector3.UnitZ, Color = ColorRGB.White };

        return new PbrShader(
            baseColor,
            default, default, default, default, default,
            emissive,
            metallic,
            roughness,
            1f,
            LightSet.Of(light),
            new Vector3(0, 0, 10),
            ambient ?? new AmbientCube(0f),
            environment,
            shadows: null);
    }

    private static MaterialVarying Surface() =>
        new(Vector3.Zero, Vector3.UnitZ, Vector4.Zero, Vector2.Zero);

    #region Environment BRDF

    [Fact]
    public void BrdfLut_NeverReflectsMoreLightThanArrives()
    {
        // scale + bias is what a surface with F0 = 1 — a perfect reflector — returns for a
        // white environment. Above 1 would be a surface emitting light it never received.
        for (var x = 0; x < BrdfLut.Resolution; x++)
        {
            for (var y = 0; y < BrdfLut.Resolution; y++)
            {
                var nDotV = (x + 0.5f) / BrdfLut.Resolution;
                var roughness = (y + 0.5f) / BrdfLut.Resolution;

                var response = BrdfLut.Sample(nDotV, roughness);

                Assert.InRange(response.X, 0f, 1.001f);
                Assert.InRange(response.Y, 0f, 1.001f);
                Assert.InRange(response.X + response.Y, 0f, 1.001f);
            }
        }
    }

    [Fact]
    public void BrdfLut_AMirrorReflectsEverythingItReceives()
    {
        // At roughness 0 the lobe is a single direction and the geometry term stops taking
        // anything away, so all of the light comes back.
        var response = BrdfLut.Integrate(1f, 0f);

        Assert.Equal(1f, response.X + response.Y, 2);
    }

    [Fact]
    public void BrdfLut_LosesMoreLightAsTheSurfaceRoughens()
    {
        // Multiple scattering between microfacets is not modelled, so a rough surface loses
        // the light that would have bounced a second time. That the loss is monotonic is the
        // property worth pinning: it is what makes roughness read as a single dial.
        var smooth = BrdfLut.Integrate(0.8f, 0.1f);
        var rough = BrdfLut.Integrate(0.8f, 0.9f);

        Assert.True(smooth.X + smooth.Y > rough.X + rough.Y);
    }

    [Fact]
    public void BrdfLut_SamplingAgreesWithTheIntegralItWasBuiltFrom()
    {
        foreach (var roughness in new[] { 0.2f, 0.5f, 0.85f })
        {
            foreach (var nDotV in new[] { 0.25f, 0.6f, 0.95f })
            {
                var sampled = BrdfLut.Sample(nDotV, roughness);
                var integrated = BrdfLut.Integrate(nDotV, roughness);

                Assert.Equal(integrated.X, sampled.X, 1);
                Assert.Equal(integrated.Y, sampled.Y, 1);
            }
        }
    }

    #endregion

    #region Prefiltered environment

    [Fact]
    public void PrefilteredEnvironment_OfAUniformSky_IsThatSkyAtEveryRoughness()
    {
        // The white furnace test: convolving a constant with any normalized kernel has to
        // give the constant back. Anything else is energy the filter invented or dropped.
        var grey = new ColorRGB(180, 180, 180);
        var environment = CubeMap.Generate(8, _ => grey);

        var prefiltered = PrefilteredEnvironment.Build(environment, baseResolution: 16, levelCount: 4);

        LinearColor expected = grey;

        foreach (var roughness in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
        {
            foreach (var direction in new[] { Vector3.UnitX, -Vector3.UnitY, Vector3.UnitZ, Vector3.Normalize(Vector3.One) })
            {
                var sample = prefiltered.Sample(direction, roughness);

                Assert.Equal(expected.R, sample.R, 2);
                Assert.Equal(expected.G, sample.G, 2);
                Assert.Equal(expected.B, sample.B, 2);
            }
        }
    }

    [Fact]
    public void PrefilteredEnvironment_SpreadsABrightSpotAsRoughnessClimbs()
    {
        // A single bright direction in an otherwise black sky. A mirror still sees it; a
        // rough surface has smeared it over the hemisphere, so the peak drops and the
        // surroundings lift.
        var sun = Vector3.Normalize(new Vector3(0.2f, 1f, 0.1f));

        // Half a right angle off the sun: outside the disc, so a mirror sees nothing there,
        // but well inside the lobe a fully rough surface gathers over.
        var perpendicular = Vector3.Normalize(Vector3.Cross(sun, Vector3.UnitX));
        var beside = Vector3.Normalize(sun * MathF.Cos(MathF.PI / 4f) + perpendicular * MathF.Sin(MathF.PI / 4f));

        var environment = CubeMap.Generate(32, direction =>
            Vector3.Dot(direction, sun) > 0.9f ? ColorRGB.White : ColorRGB.Black);

        var prefiltered = PrefilteredEnvironment.Build(environment, baseResolution: 32, levelCount: 5);

        var mirrorPeak = prefiltered.Sample(sun, 0f).Luminance;
        var roughPeak = prefiltered.Sample(sun, 1f).Luminance;

        Assert.True(mirrorPeak > roughPeak, $"mirror {mirrorPeak} should out-peak rough {roughPeak}");

        // Beside the sun a mirror sees nothing at all, while a fully rough surface has
        // gathered some of it.
        Assert.Equal(0f, prefiltered.Sample(beside, 0f).Luminance, 3);
        Assert.True(prefiltered.Sample(beside, 1f).Luminance > 0f,
            "a fully rough surface should gather light from well off its reflection direction");
    }

    [Fact]
    public void PrefilteredEnvironment_ScalesWithTheIntensityItWasBuiltWith()
    {
        var environment = CubeMap.Generate(8, _ => ColorRGB.White);

        var full = PrefilteredEnvironment.Build(environment, 8, 3);
        var half = PrefilteredEnvironment.Build(environment, 8, 3, intensity: 0.5f);

        foreach (var roughness in new[] { 0f, 0.5f, 1f })
        {
            var expected = full.Sample(Vector3.UnitY, roughness).Luminance * 0.5f;

            Assert.Equal(expected, half.Sample(Vector3.UnitY, roughness).Luminance, 3);
        }
    }

    #endregion

    #region The shader

    [Fact]
    public void Metal_ScattersNoDiffuse()
    {
        // A metal's electrons absorb what is not reflected, so there is no subsurface
        // scattering to come back out. Lit from a direction the specular lobe does not point
        // in, it is nearly black — where the same albedo as a dielectric is plainly lit.
        var offAxis = new MaterialVarying(Vector3.Zero, Vector3.Normalize(new Vector3(0.8f, 0f, 0.6f)), Vector4.Zero, Vector2.Zero);

        var metal = HeadOn(ColorRGB.White, metallic: 1f, roughness: 0.35f).Shade(offAxis);
        var dielectric = HeadOn(ColorRGB.White, metallic: 0f, roughness: 0.35f).Shade(offAxis);

        Assert.True(metal.Luminance < 0.05f, $"metal should be dark off-specular, got {metal.Luminance}");
        Assert.True(dielectric.Luminance > 0.3f, $"dielectric should be lit, got {dielectric.Luminance}");
    }

    [Fact]
    public void Metal_TintsItsReflectionWithItsAlbedo()
    {
        // Gold reflects yellow; a dielectric's highlight is the colour of the light whatever
        // the surface underneath it is.
        var gold = new ColorRGB(255, 180, 60);

        var metal = HeadOn(gold, metallic: 1f, roughness: 0.2f).Shade(Surface());
        var plastic = HeadOn(gold, metallic: 0f, roughness: 0.2f).Shade(Surface());

        Assert.True(metal.R > metal.B * 3f, "a gold metal's reflection should be strongly red-shifted");

        // The dielectric's diffuse carries the tint too, but its highlight does not — so the
        // ratio between channels is closer to the light's own.
        Assert.True(metal.R / MathF.Max(metal.B, 1e-4f) > plastic.R / MathF.Max(plastic.B, 1e-4f));
    }

    [Fact]
    public void Roughness_TradesPeakBrightnessForSpread()
    {
        // Same light, same albedo: the smooth surface concentrates the reflection into a
        // small bright lobe, the rough one spreads it. Measured at the mirror direction,
        // where the smooth one's lobe is pointing.
        var smooth = HeadOn(ColorRGB.Gray, metallic: 1f, roughness: 0.05f).Shade(Surface());
        var rough = HeadOn(ColorRGB.Gray, metallic: 1f, roughness: 0.9f).Shade(Surface());

        Assert.True(smooth.Luminance > rough.Luminance * 4f,
            $"smooth {smooth.Luminance} should far out-peak rough {rough.Luminance}");
    }

    [Fact]
    public void ADiffuseSurface_IsExposedLikeTheRestOfTheEngine()
    {
        // The BRDF is scaled by π so a scene does not change brightness when the viewer
        // switches to this painter. A white matte surface lit head-on by a unit light should
        // land near white, as it does under Lambert — not at 1/π of it.
        var white = HeadOn(ColorRGB.White, metallic: 0f, roughness: 1f).Shade(Surface());

        Assert.InRange(white.Luminance, 0.75f, 1.3f);
    }

    [Fact]
    public void NoLightAndNoEnvironment_LeavesOnlyWhatTheSurfaceEmits()
    {
        var shader = new PbrShader(
            ColorRGB.White,
            default, default, default, default, default,
            new LinearColor(0.5f, 0.25f, 0f),
            metallic: 0f,
            roughness: 0.5f,
            normalStrength: 1f,
            LightSet.Of(new DirectionalLight { Direction = Vector3.UnitZ, Color = ColorRGB.Black }),
            new Vector3(0, 0, 10),
            new AmbientCube(0f),
            environment: null,
            shadows: null);

        var shaded = shader.Shade(Surface());

        Assert.Equal(0.5f, shaded.R, 3);
        Assert.Equal(0.25f, shaded.G, 3);
        Assert.Equal(0f, shaded.B, 3);
    }

    [Fact]
    public void AnEnvironment_LightsASurfaceWithNoLightsAtAll()
    {
        var environment = CubeMap.Generate(8, _ => ColorRGB.White);
        var prefiltered = PrefilteredEnvironment.Build(environment, 8, 3);

        var unlit = new DirectionalLight { Direction = Vector3.UnitZ, Color = ColorRGB.Black };

        var shader = new PbrShader(
            ColorRGB.White,
            default, default, default, default, default,
            LinearColor.Black,
            metallic: 1f,
            roughness: 0.1f,
            normalStrength: 1f,
            LightSet.Of(unlit),
            new Vector3(0, 0, 10),
            new AmbientCube(0f),
            prefiltered,
            shadows: null);

        // A near-mirror metal under a white sky reflects nearly all of it, from the image
        // rather than from any light source.
        Assert.True(shader.Shade(Surface()).Luminance > 0.7f);
    }

    #endregion

    #region Through the pipeline

    [Fact]
    public void PbrPainter_ShadesASceneEndToEnd()
    {
        var world = new SimpleWorld();
        world.Meshes.Add(new IcoSphere(3) { Scale = new Vector3(2f, 2f, 2f) });
        world.Lights.Add(new DirectionalLight { Direction = new Vector3(-0.3f, -0.4f, 1f) });

        var scene = new Scene
        {
            World = world,
            Camera = new FixedCamera(new Vector3(0, 0, -8f)),
            Projection = new PerspectiveProjection(MathF.PI / 4f, 0.1f, 100f),
            Surface = new FrameBuffer(64, 64) { Stats = new RenderStats() },
            GammaCorrect = true,
            Environment = SkyBox.Gradient(new Vector3(-0.3f, -0.6f, 1f), resolution: 16),
        };

        var painter = new PbrPainter { EnvironmentResolution = 8 };

        new Renderer().Render(scene, painter);

        Assert.NotNull(painter.Environment);

        // The sphere is at the centre of the frame and lit, so the middle pixel is neither
        // the cleared background nor black.
        var centre = scene.Surface.GetColor(32, 32);

        Assert.NotEqual(0, centre & 0x00FFFFFF);
    }

    [Fact]
    public void PbrPainter_ReusesThePrefilteredEnvironmentBetweenFrames()
    {
        var world = new SimpleWorld();
        world.Meshes.Add(new Cube());

        var scene = new Scene
        {
            World = world,
            Camera = new FixedCamera(new Vector3(0, 0, -5f)),
            Projection = new PerspectiveProjection(MathF.PI / 4f, 0.1f, 100f),
            Surface = new FrameBuffer(32, 32) { Stats = new RenderStats() },
            Environment = SkyBox.Gradient(new Vector3(0, -1f, 0), resolution: 8),
        };

        var painter = new PbrPainter { EnvironmentResolution = 8 };
        var renderer = new Renderer();

        renderer.Render(scene, painter);
        var first = painter.Environment;

        renderer.Render(scene, painter);

        // Convolving five cube maps per frame would cost more than the frame does.
        Assert.Same(first, painter.Environment);

        // …but a change of intensity is a different answer, and has to be rebuilt.
        scene.AmbientIntensity = 0.8f;
        renderer.Render(scene, painter);

        Assert.NotSame(first, painter.Environment);
    }

    [Fact]
    public void PbrPainter_WithoutAnEnvironment_StillShades()
    {
        var world = new SimpleWorld();
        world.Meshes.Add(new Cube { Scale = new Vector3(2f, 2f, 2f) });
        world.Lights.Add(new DirectionalLight { Direction = new Vector3(0, 0, 1f) });

        var scene = new Scene
        {
            World = world,
            Camera = new FixedCamera(new Vector3(0, 0, -6f)),
            Projection = new PerspectiveProjection(MathF.PI / 4f, 0.1f, 100f),
            Surface = new FrameBuffer(32, 32) { Stats = new RenderStats() },
            GammaCorrect = true,
        };

        var painter = new PbrPainter();

        new Renderer().Render(scene, painter);

        Assert.Null(painter.Environment);
        Assert.NotEqual(0, scene.Surface.GetColor(16, 16) & 0x00FFFFFF);
    }

    #endregion
}
