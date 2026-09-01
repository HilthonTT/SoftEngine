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
        var response = BrdfLut.Integrate(1f, 0f);

        Assert.Equal(1f, response.X + response.Y, 2);
    }

    [Fact]
    public void BrdfLut_LosesMoreLightAsTheSurfaceRoughens()
    {
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
        var sun = Vector3.Normalize(new Vector3(0.2f, 1f, 0.1f));

        var perpendicular = Vector3.Normalize(Vector3.Cross(sun, Vector3.UnitX));
        var beside = Vector3.Normalize(sun * MathF.Cos(MathF.PI / 4f) + perpendicular * MathF.Sin(MathF.PI / 4f));

        var environment = CubeMap.Generate(32, direction =>
            Vector3.Dot(direction, sun) > 0.9f ? ColorRGB.White : ColorRGB.Black);

        var prefiltered = PrefilteredEnvironment.Build(environment, baseResolution: 32, levelCount: 5);

        var mirrorPeak = prefiltered.Sample(sun, 0f).Luminance;
        var roughPeak = prefiltered.Sample(sun, 1f).Luminance;

        Assert.True(mirrorPeak > roughPeak, $"mirror {mirrorPeak} should out-peak rough {roughPeak}");

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
        var offAxis = new MaterialVarying(Vector3.Zero, Vector3.Normalize(new Vector3(0.8f, 0f, 0.6f)), Vector4.Zero, Vector2.Zero);

        var metal = HeadOn(ColorRGB.White, metallic: 1f, roughness: 0.35f).Shade(offAxis);
        var dielectric = HeadOn(ColorRGB.White, metallic: 0f, roughness: 0.35f).Shade(offAxis);

        Assert.True(metal.Luminance < 0.05f, $"metal should be dark off-specular, got {metal.Luminance}");
        Assert.True(dielectric.Luminance > 0.3f, $"dielectric should be lit, got {dielectric.Luminance}");
    }

    [Fact]
    public void Metal_TintsItsReflectionWithItsAlbedo()
    {
        var gold = new ColorRGB(255, 180, 60);

        var metal = HeadOn(gold, metallic: 1f, roughness: 0.2f).Shade(Surface());
        var plastic = HeadOn(gold, metallic: 0f, roughness: 0.2f).Shade(Surface());

        Assert.True(metal.R > metal.B * 3f, "a gold metal's reflection should be strongly red-shifted");

        Assert.True(metal.R / MathF.Max(metal.B, 1e-4f) > plastic.R / MathF.Max(plastic.B, 1e-4f));
    }

    [Fact]
    public void Roughness_TradesPeakBrightnessForSpread()
    {
        var smooth = HeadOn(ColorRGB.Gray, metallic: 1f, roughness: 0.05f).Shade(Surface());
        var rough = HeadOn(ColorRGB.Gray, metallic: 1f, roughness: 0.9f).Shade(Surface());

        Assert.True(smooth.Luminance > rough.Luminance * 4f,
            $"smooth {smooth.Luminance} should far out-peak rough {rough.Luminance}");
    }

    [Fact]
    public void ADiffuseSurface_IsExposedLikeTheRestOfTheEngine()
    {
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

        Assert.Same(first, painter.Environment);

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
