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

namespace SoftEngine.Core.Tests;

public class SsrTests
{
    private sealed class FixedCamera(Vector3 position, Vector3 target) : ICamera
    {
        public Vector3 Position { get; set; } = position;

        public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, target, Vector3.UnitY);
    }

    /// <summary>
    /// A red block standing on a floor, seen from a low angle — the one arrangement where a
    /// screen-space reflection is guaranteed to have something on screen to find. The floor
    /// in front of the block reflects rays up and away into it, and every one of those rays
    /// crosses pixels the frame has drawn.
    /// </summary>
    private static Scene Room(bool mirrorFloor, int size = 128)
    {
        var world = new SimpleWorld();

        var floor = new Cube { Position = new Vector3(0f, -2f, 0f), Scale = new Vector3(14f, 0.25f, 14f) };

        floor.Material.Diffuse = new ColorRGB(200, 200, 205);
        floor.Material.Metallic = mirrorFloor ? 1f : 0f;
        floor.Material.Roughness = mirrorFloor ? 0.05f : 0.95f;

        var block = new Cube { Position = new Vector3(0f, 0.2f, 1.5f), Scale = new Vector3(2f, 2f, 2f) };

        // Saturated, so a reflection of it is unmistakable against a neutral floor, and bright
        // enough that the reflection survives the floor's own shading.
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

        // Both renders have to take the same path through the stack, or they differ by the
        // resolve rather than by the reflection: a stack with nothing enabled is skipped whole
        // and never encodes. A vignette of zero intensity is the neutral effect that keeps it
        // running — the same trick the occlusion tests use next door.
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

            // How far each pixel moved toward red, measured against its own blue so a change
            // in overall brightness does not read as a change in hue.
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

        // Disabled: nothing is going to read the channel, so the fill must not pay for it.
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

        // The bottom of the frame is floor: a metal, so its F0 is its albedo rather than the
        // dielectric four percent.
        var floor = SurfaceReflectance.FromPacked(reflectance[width / 2 + (scene.Surface.Height - 4) * width]);

        Assert.True(floor.IsReflective);
        Assert.True(floor.Reflectivity.R > 0.4f, $"floor F0 was {floor.Reflectivity.R}");
        Assert.InRange(floor.Roughness, 0f, 0.2f);

        // The top corners are background, which nothing drew and so reflects nothing.
        Assert.False(SurfaceReflectance.FromPacked(reflectance[0]).IsReflective);
    }

    [Fact]
    public void Ssr_ReflectsTheSceneInAMirrorFloor()
    {
        var without = Render(enabled: false);
        var with = Render(enabled: true);

        var (redder, bluer, changed) = CompareRed(without, with);

        Assert.True(changed > 200, $"only {changed} pixels changed");

        // The only thing above the floor is red, so whatever the floor picked up must be too.
        Assert.True(redder > bluer * 4, $"{redder} pixels went redder, {bluer} went bluer");
    }

    [Fact]
    public void Ssr_LeavesAMatteFloorAlone()
    {
        var without = Render(enabled: false, mirrorFloor: false);
        var with = Render(enabled: true, mirrorFloor: false);

        // Roughness 0.95 is past MaxRoughness: the prefiltered environment already answers a
        // reflection that wide, and one ray per pixel could only answer it as noise.
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
        // What a frame filled by the GPU backend looks like to the stack: depth is there,
        // because the backend transfers it back, and the surface channel is not, because its
        // fragment shaders write one target.
        //
        // Both frames go through a stack, and neither through a renderer that records
        // reflectance — comparing against a frame that never met the stack would compare the
        // resolves, which differ in the alpha byte alone.
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

        // No projection: the depth buffer cannot be turned back into positions, so there is
        // no scene to march through.
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

        // One lit, mirror-like pixel hard against an edge, so every neighbour the march or the
        // blur reaches for is off the frame.
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

        // The effect's buffers are grown and never shrunk, so a small frame after a large one
        // is where an index computed from the wrong width would land inside the array and read
        // a stale pixel instead of throwing.
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

        // A dielectric's reflection is colourless however coloured the surface is: the albedo
        // belongs to its diffuse term, not to what it mirrors.
        Assert.Equal(0.04f, plastic.Reflectivity.R, 0.01f);
        Assert.Equal(plastic.Reflectivity.R, plastic.Reflectivity.G, 0.01f);
        Assert.Equal(plastic.Reflectivity.R, plastic.Reflectivity.B, 0.01f);

        var gold = SurfaceReflectance.FromMetallic(new ColorRGB(255, 200, 80), 1f, 0.2f);

        // A metal's is its albedo, in linear light — which is what tints its reflections.
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

        // The default material is a plausible dielectric rather than a 35% mirror: Blinn-Phong's
        // specular strength is a highlight's brightness, not a reflectance.
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

        // A state nobody tagged reflects nothing, so a painter that says nothing costs nothing.
        Assert.False(default(RasterState).Reflectance.IsReflective);
    }
}
