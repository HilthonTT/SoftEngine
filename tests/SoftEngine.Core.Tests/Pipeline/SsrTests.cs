using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Geometry.Primitives;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Pipeline.PostProcess;
using SoftEngine.Core.Rasterization;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Cameras;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Scenes.Projections;
using SoftEngine.Core.Shading;
using System.Numerics;

namespace SoftEngine.Core.Tests.Pipeline;

public class SsrTests
{
    private sealed class FixedCamera(Vector3 position, Vector3 target) : ICamera
    {
        public Vector3 Position { get; set; } = position;

        public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, target, Vector3.UnitY);
    }

    private static Scene Room(bool mirrorFloor, int size = 128)
    {
        var world = new SimpleWorld();

        var floor = new Cube { Position = new Vector3(0f, -2f, 0f), Scale = new Vector3(14f, 0.25f, 14f) };

        floor.Material.Diffuse = new ColorRGB(200, 200, 205);
        floor.Material.Metallic = mirrorFloor ? 1f : 0f;
        floor.Material.Roughness = mirrorFloor ? 0.05f : 0.95f;

        var block = new Cube { Position = new Vector3(0f, 0.2f, 1.5f), Scale = new Vector3(2f, 2f, 2f) };

        block.Material.Diffuse = new ColorRGB(255, 20, 20);
        block.Material.Metallic = 0f;
        block.Material.Roughness = 0.6f;
        block.Material.Emissive = new ColorRGB(180, 0, 0);

        world.Meshes.Add(floor);
        world.Meshes.Add(block);
        world.Lights.Add(new DirectionalLight { Direction = new Vector3(-0.3f, -1f, 0.2f) });

        return new Scene
        {
            World = world,
            Camera = new FixedCamera(new Vector3(0f, 0.4f, -9f), new Vector3(0f, -0.9f, 0f)),
            Projection = new PerspectiveProjection(MathF.PI / 4f, 0.5f, 100f),
            Surface = new FrameBuffer(size, size) { Stats = new RenderStats() },
            GammaCorrect = true,
        };
    }

    private static Scene Render(bool enabled, bool mirrorFloor = true, float strength = 1f)
    {
        var scene = Room(mirrorFloor);

        var renderer = new Renderer
        {
            PostProcess = new PostProcessStack(),
        };

        renderer.PostProcess.Effects.Add(new SsrEffect
        {
            Enabled = enabled,
            Strength = strength,
            MaxDistance = 30f,
            Thickness = 1.2f,
        });

        renderer.PostProcess.Effects.Add(new VignetteEffect { Enabled = true, Intensity = 0f });

        renderer.Render(scene, new PbrPainter());

        return scene;
    }

    private static (int Redder, int Bluer, int Changed) CompareRed(Scene without, Scene with)
    {
        var a = without.Surface.Screen;
        var b = with.Surface.Screen;

        var redder = 0;
        var bluer = 0;
        var changed = 0;

        for (var i = 0; i < a.Length; i++)
        {
            if (a[i] == b[i])
            {
                continue;
            }

            changed++;

            var before = ((a[i] >> 16) & 0xFF) - (a[i] & 0xFF);
            var after = ((b[i] >> 16) & 0xFF) - (b[i] & 0xFF);

            if (after > before)
            {
                redder++;
            }
            else if (after < before)
            {
                bluer++;
            }
        }

        return (redder, bluer, changed);
    }

    [Fact]
    public void Reflectance_IsNotRecordedUnlessSomethingAsks()
    {
        var scene = Room(mirrorFloor: true);

        new Renderer().Render(scene, new PbrPainter());

        Assert.False(scene.Surface.IsRecordingReflectance);
        Assert.True(scene.Surface.Reflectance.IsEmpty);
    }

    [Fact]
    public void Reflectance_IsTurnedOnByAnEnabledEffectThatNeedsIt()
    {
        var scene = Room(mirrorFloor: true);

        var renderer = new Renderer { PostProcess = new PostProcessStack() };
        var effect = new SsrEffect { Enabled = false };

        renderer.PostProcess.Effects.Add(effect);
        renderer.Render(scene, new PbrPainter());

        Assert.False(scene.Surface.IsRecordingReflectance);

        effect.Enabled = true;
        renderer.Render(scene, new PbrPainter());

        Assert.True(scene.Surface.IsRecordingReflectance);
    }

    [Fact]
    public void Reflectance_RecordsWhatThePainterSaysTheSurfaceIs()
    {
        var scene = Room(mirrorFloor: true);

        var renderer = new Renderer { PostProcess = new PostProcessStack() };
        renderer.PostProcess.Effects.Add(new SsrEffect { Enabled = true });
        renderer.Render(scene, new PbrPainter());

        var reflectance = scene.Surface.Reflectance;
        var width = scene.Surface.Width;

        var floor = SurfaceReflectance.FromPacked(reflectance[width / 2 + (scene.Surface.Height - 4) * width]);

        Assert.True(floor.IsReflective);
        Assert.True(floor.Reflectivity.R > 0.4f, $"floor F0 was {floor.Reflectivity.R}");
        Assert.InRange(floor.Roughness, 0f, 0.2f);

        Assert.False(SurfaceReflectance.FromPacked(reflectance[0]).IsReflective);
    }

    [Fact]
    public void Ssr_ReflectsTheSceneInAMirrorFloor()
    {
        var without = Render(enabled: false);
        var with = Render(enabled: true);

        var (redder, bluer, changed) = CompareRed(without, with);

        Assert.True(changed > 200, $"only {changed} pixels changed");

        Assert.True(redder > bluer * 4, $"{redder} pixels went redder, {bluer} went bluer");
    }

    [Fact]
    public void Ssr_LeavesAMatteFloorAlone()
    {
        var without = Render(enabled: false, mirrorFloor: false);
        var with = Render(enabled: true, mirrorFloor: false);

        Assert.Equal(without.Surface.Screen, with.Surface.Screen);
    }

    [Fact]
    public void Ssr_WithZeroStrength_ChangesNothing()
    {
        var neutral = Render(enabled: true, strength: 0f);
        var off = Render(enabled: false);

        Assert.Equal(off.Surface.Screen, neutral.Surface.Screen);
    }

    [Fact]
    public void Ssr_IsDeterministic()
    {
        var first = Render(enabled: true);
        var second = Render(enabled: true);

        Assert.Equal(first.Surface.Screen, second.Surface.Screen);
    }

    [Fact]
    public void Ssr_WithoutReflectance_DoesNothing()
    {
        Assert.Equal(WithoutReflectance(reflect: false), WithoutReflectance(reflect: true));

        static int[] WithoutReflectance(bool reflect)
        {
            var scene = Room(mirrorFloor: true);

            new Renderer().Render(scene, new PbrPainter());

            var stack = new PostProcessStack();
            stack.Effects.Add(new SsrEffect { Enabled = reflect, Strength = 1f });
            stack.Effects.Add(new VignetteEffect { Enabled = true, Intensity = 0f });
            stack.Apply(scene.Surface, scene.Projection);

            return scene.Surface.Screen;
        }
    }

    [Fact]
    public void Ssr_WithoutAProjection_FindsNoDepthAndDoesNothing()
    {
        var scene = Room(mirrorFloor: true);

        var renderer = new Renderer { PostProcess = new PostProcessStack() };
        renderer.PostProcess.Effects.Add(new SsrEffect { Enabled = true });
        renderer.Render(scene, new PbrPainter());

        var before = (int[])scene.Surface.Screen.Clone();

        var stack = new PostProcessStack();
        stack.Effects.Add(new SsrEffect { Enabled = true, Strength = 1f });

        stack.Apply(scene.Surface);

        Assert.Equal(before, scene.Surface.Screen);
    }

    [Theory]
    [InlineData(5, 5, 0, 0)]
    [InlineData(5, 5, 4, 4)]
    [InlineData(9, 3, 8, 0)]
    [InlineData(3, 9, 0, 8)]
    public void Ssr_GeometryOnTheFrameBorder_DoesNotReadPastTheBuffer(int width, int height, int x, int y)
    {
        var surface = new FrameBuffer(width, height);

        surface.SetReflectanceRecording(true);
        surface.SetDepthRange(0.5f, 100f);
        surface.Clear();

        surface.PutPixel(x, y, FrameBuffer.DepthResolution / 2, LinearColor.White);
        surface.RecordReflectance(x, y, SurfaceReflectance.FromMetallic(ColorRGB.White, 1f, 0.05f).Packed);

        var stack = new PostProcessStack();
        stack.Effects.Add(new SsrEffect { Enabled = true, MaxDistance = 10f });

        stack.Apply(surface, new PerspectiveProjection(MathF.PI / 4f, 0.5f, 100f));
    }

    [Fact]
    public void Ssr_AfterALargerFrame_StillDoesNotReadPastTheBuffer()
    {
        var stack = new PostProcessStack();
        stack.Effects.Add(new SsrEffect { Enabled = true, MaxDistance = 10f });

        var projection = new PerspectiveProjection(MathF.PI / 4f, 0.5f, 100f);

        foreach (var size in (int[])[32, 5])
        {
            var surface = new FrameBuffer(size, size);

            surface.SetReflectanceRecording(true);
            surface.SetDepthRange(0.5f, 100f);
            surface.Clear();

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    surface.PutPixel(x, y, FrameBuffer.DepthResolution / 2, LinearColor.White);
                    surface.RecordReflectance(x, y, SurfaceReflectance.FromMetallic(ColorRGB.White, 1f, 0.3f).Packed);
                }
            }

            stack.Apply(surface, projection);
        }
    }

    [Fact]
    public void SurfaceReflectance_RoundTripsThroughItsPacking()
    {
        var value = SurfaceReflectance.FromMetallic(new ColorRGB(255, 128, 0), 1f, 0.25f);
        var read = SurfaceReflectance.FromPacked(value.Packed);

        Assert.Equal(value.Reflectivity.R, read.Reflectivity.R);
        Assert.Equal(value.Reflectivity.G, read.Reflectivity.G);
        Assert.Equal(value.Reflectivity.B, read.Reflectivity.B);
        Assert.Equal(value.Roughness, read.Roughness, 0.005f);
    }

    [Fact]
    public void SurfaceReflectance_ADielectricReflectsFourPercentAndAMetalReflectsItsAlbedo()
    {
        var plastic = SurfaceReflectance.FromMetallic(new ColorRGB(220, 30, 30), 0f, 0.4f);

        Assert.Equal(0.04f, plastic.Reflectivity.R, 0.01f);
        Assert.Equal(plastic.Reflectivity.R, plastic.Reflectivity.G, 0.01f);
        Assert.Equal(plastic.Reflectivity.R, plastic.Reflectivity.B, 0.01f);

        var gold = SurfaceReflectance.FromMetallic(new ColorRGB(255, 200, 80), 1f, 0.2f);

        Assert.True(gold.Reflectivity.R > gold.Reflectivity.G);
        Assert.True(gold.Reflectivity.G > gold.Reflectivity.B);
        Assert.True(gold.Reflectivity.R > 0.9f);
    }

    [Fact]
    public void SurfaceReflectance_AMatteSurfaceIsNotWorthTracingARayFor()
    {
        Assert.False(SurfaceReflectance.None.IsReflective);
        Assert.False(SurfaceReflectance.FromSpecular(0f, 32f).IsReflective);
        Assert.False(default(SurfaceReflectance).IsReflective);

        var standard = SurfaceReflectance.FromMaterial(new Material());

        Assert.True(standard.IsReflective);
        Assert.InRange(standard.Reflectivity.R, 0.01f, 0.06f);
    }

    [Fact]
    public void RasterState_WithReflectance_RoundTripsAndSurvivesTheOtherTags()
    {
        var reflectance = SurfaceReflectance.FromMetallic(ColorRGB.White, 1f, 0.5f);

        var state = default(RasterState)
            .WithReflectance(reflectance)
            .WithMipLevel(3)
            .WithOpacity(0.5f);

        Assert.Equal(reflectance.Packed, state.PackedReflectance);
        Assert.Equal(3, state.MipLevel);
        Assert.Equal(0.5f, state.Alpha);

        Assert.False(default(RasterState).Reflectance.IsReflective);
    }
}
