using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Geometry.Primitives;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Pipeline.PostProcess;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Cameras;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Scenes.Projections;
using SoftEngine.Core.Shading;
using System.Numerics;

namespace SoftEngine.Core.Tests;

public class SsaoTests
{
    private sealed class FixedCamera(Vector3 position) : ICamera
    {
        public Vector3 Position { get; set; } = position;

        public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Vector3.Zero, Vector3.UnitY);
    }

    private static Scene Corner(int size = 96)
    {
        // Two walls meeting at a right angle behind the camera's line of sight — a crease,
        // which is exactly the geometry a shadow map at any sane resolution cannot resolve
        // and screen-space occlusion can.
        var world = new SimpleWorld();

        world.Meshes.Add(new Cube { Position = new Vector3(0, -3f, 0), Scale = new Vector3(6f, 0.25f, 6f) });
        world.Meshes.Add(new Cube { Position = new Vector3(0, 0f, 3f), Scale = new Vector3(6f, 6f, 0.25f) });
        world.Lights.Add(new DirectionalLight { Direction = new Vector3(0f, -0.3f, 1f) });

        return new Scene
        {
            World = world,
            Camera = new FixedCamera(new Vector3(0, 0.5f, -12f)),
            Projection = new PerspectiveProjection(MathF.PI / 4f, 0.5f, 100f),
            Surface = new FrameBuffer(size, size) { Stats = new RenderStats() },
            GammaCorrect = true,
        };
    }

    [Fact]
    public void ReadViewDepth_RecoversTheDistanceGeometryWasDrawnAt()
    {
        var scene = Corner();
        new Renderer().Render(scene, new GouraudPainter());

        Assert.True(scene.Surface.HasRecoverableDepth);

        var depth = new float[scene.Surface.Width * scene.Surface.Height];
        scene.Surface.ReadViewDepth(depth);

        // The floor and back wall sit around 12 to 15 units from the camera.
        var centre = depth[scene.Surface.Width / 2 + scene.Surface.Height / 2 * scene.Surface.Width];

        Assert.True(float.IsFinite(centre));
        Assert.InRange(centre, 5f, 25f);

        // A corner of the frame the geometry does not reach is background.
        Assert.True(float.IsPositiveInfinity(depth[0]));
    }

    [Fact]
    public void ReadViewDepth_UnderAParallelProjection_ReportsNoUsableDepth()
    {
        var surface = new FrameBuffer(8, 8);
        surface.SetLinearDepthRange();
        surface.Clear();

        Assert.False(surface.HasRecoverableDepth);

        var depth = new float[64];
        surface.ReadViewDepth(depth);

        Assert.All(depth, d => Assert.True(float.IsPositiveInfinity(d)));
    }

    private static Scene RenderWithSsao(bool enabled, float strength = 0.9f)
    {
        var scene = Corner();

        var renderer = new Renderer
        {
            PostProcess = new PostProcessStack(),
        };

        renderer.PostProcess.Effects.Add(new SsaoEffect
        {
            Enabled = enabled,
            Strength = strength,
            Radius = 1.5f,
            BlurRadius = 2,
        });

        // The two renders have to take the same path through the stack, or they differ by
        // the resolve rather than by the occlusion: a stack with nothing enabled is skipped
        // whole, and never encodes. A vignette of zero intensity is the neutral effect that
        // keeps it running.
        renderer.PostProcess.Effects.Add(new VignetteEffect { Enabled = true, Intensity = 0f });

        renderer.Render(scene, new GouraudPainter());

        return scene;
    }

    [Fact]
    public void Ssao_DarkensSomePixelsAndBrightensNone()
    {
        var without = RenderWithSsao(enabled: false);
        var with = RenderWithSsao(enabled: true);

        var size = without.Surface.Width;
        var darkened = 0;
        var brightened = 0;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var plain = ColorRGB.FromPacked(without.Surface.GetColor(x, y));
                var occluded = ColorRGB.FromPacked(with.Surface.GetColor(x, y));

                if (occluded.R < plain.R)
                {
                    darkened++;
                }
                else if (occluded.R > plain.R)
                {
                    brightened++;
                }
            }
        }

        // Occlusion only ever multiplies by something at or below 1, so a brighter pixel
        // would mean the effect is doing something other than occluding.
        Assert.Equal(0, brightened);
        Assert.True(darkened > 0, "the corner produced no occlusion at all");
    }

    [Fact]
    public void Ssao_LeavesTheBackgroundAlone()
    {
        var with = RenderWithSsao(enabled: true);
        var without = RenderWithSsao(enabled: false);

        // The top-left corner is background in both: nothing was drawn there, so there is
        // no surface to occlude.
        Assert.Equal(without.Surface.GetColor(1, 1), with.Surface.GetColor(1, 1));
    }

    [Fact]
    public void Ssao_WithZeroStrength_ChangesNothing()
    {
        var neutral = RenderWithSsao(enabled: true, strength: 0f);
        var off = RenderWithSsao(enabled: false);

        var size = neutral.Surface.Width;

        for (var y = 0; y < size; y += 7)
        {
            for (var x = 0; x < size; x += 7)
            {
                Assert.Equal(off.Surface.GetColor(x, y), neutral.Surface.GetColor(x, y));
            }
        }
    }

    [Fact]
    public void Ssao_IsDeterministic()
    {
        // The kernel and its per-pixel rotations are fixed, so two renders of one scene are
        // identical — an effect seeded from a live random source would not be.
        var first = RenderWithSsao(enabled: true);
        var second = RenderWithSsao(enabled: true);

        Assert.Equal(first.Surface.Screen, second.Surface.Screen);
    }

    [Fact]
    public void Ssao_WithoutAProjection_FindsNoDepthAndDoesNothing()
    {
        var scene = Corner();
        new Renderer().Render(scene, new GouraudPainter());

        var before = (int[])scene.Surface.Screen.Clone();

        var stack = new PostProcessStack();
        stack.Effects.Add(new SsaoEffect { Enabled = true, Strength = 1f, Radius = 1.5f });

        // No projection: the depth buffer cannot be turned back into positions, so there is
        // nothing for the effect to work from.
        stack.Apply(scene.Surface, null);

        // Compared without the alpha byte, which the stack's encode forces opaque whether
        // or not any effect changed a colour.
        for (var i = 0; i < before.Length; i++)
        {
            Assert.Equal(before[i] & 0x00FFFFFF, scene.Surface.Screen[i] & 0x00FFFFFF);
        }
    }

    /// <summary>
    /// A frame drawn by hand rather than rendered, so the depth pattern is exactly the one
    /// under test: a single lit pixel somewhere on the border, background everywhere else.
    /// </summary>
    private static FrameBuffer BorderPixel(int width, int height, int x, int y)
    {
        var surface = new FrameBuffer(width, height) { Stats = new RenderStats() };

        surface.SetDepthRange(1f, 100f);
        surface.Clear();
        surface.PutPixel(x, y, FrameBuffer.DepthResolution / 2, new LinearColor(0.5f, 0.5f, 0.5f));

        return surface;
    }

    private static void RunSsao(FrameBuffer surface)
    {
        var stack = new PostProcessStack();
        stack.Effects.Add(new SsaoEffect { Enabled = true, Radius = 0.5f });

        stack.Apply(surface, new PerspectiveProjection(MathF.PI / 4f, 1f, 100f));
    }

    [Theory]
    [InlineData(16, 16, 8, 15)]   // bottom edge, the one that used to throw
    [InlineData(16, 16, 15, 8)]   // right edge
    [InlineData(16, 16, 15, 15)]  // bottom-right corner, both at once
    [InlineData(16, 16, 0, 0)]    // top-left corner, the opposite border
    [InlineData(16, 16, 8, 0)]    // top edge
    public void Ssao_GeometryOnTheFrameBorder_DoesNotReadPastTheBuffer(int width, int height, int x, int y)
    {
        // Reconstructing a normal reads the pixels either side. A lit pixel on the last row
        // has background above it and no row below at all, so both neighbours are at infinity
        // — and the tie-break used to resolve to the pixel that does not exist, one past the
        // end of the depth buffer. The buffer is only ever grown, so this read landed in a
        // stale larger frame's data most of the time and threw the rest of the time, which is
        // why it only surfaced after zooming with ambient occlusion on.
        var surface = BorderPixel(width, height, x, y);

        RunSsao(surface);

        // A pixel with no usable neighbour gets no normal and therefore no occlusion, so it
        // has to come through the effect unchanged rather than merely not crashing.
        Assert.NotEqual(0, surface.Screen[x + y * width] & 0x00FFFFFF);
    }

    [Fact]
    public void Ssao_AfterALargerFrame_StillDoesNotReadPastTheBuffer()
    {
        // The depth buffer is grown and never shrunk, so a smaller frame that follows a
        // larger one leaves readable slack past its end. That slack is what hid this.
        var stack = new PostProcessStack();
        stack.Effects.Add(new SsaoEffect { Enabled = true, Radius = 0.5f });

        var projection = new PerspectiveProjection(MathF.PI / 4f, 1f, 100f);

        stack.Apply(BorderPixel(64, 64, 32, 63), projection);

        var small = BorderPixel(16, 16, 8, 15);
        stack.Apply(small, projection);

        Assert.NotEqual(0, small.Screen[8 + 15 * 16] & 0x00FFFFFF);
    }

    [Fact]
    public void ViewPositionAt_OutsideTheFrame_ReadsAsBackground()
    {
        var scene = Corner(32);
        new Renderer().Render(scene, new GouraudPainter());

        PostProcessTarget? captured = null;

        var stack = new PostProcessStack();
        stack.Effects.Add(new DepthProbeEffect(target => captured = target));
        stack.Apply(scene.Surface, scene.Projection);

        Assert.NotNull(captured);

        // There is no recorded geometry outside the frame, which is what background means.
        foreach (var (x, y) in new[] { (-1, 0), (0, -1), (captured!.Width, 0), (0, captured.Height) })
        {
            Assert.True(float.IsNegativeInfinity(captured.ViewPositionAt(x, y).Z));
        }
    }

    /// <summary>An effect that does nothing but hand the target it was given to a callback.</summary>
    private sealed class DepthProbeEffect(Action<PostProcessTarget> inspect) : IPostEffect
    {
        public string Name => "Probe";

        public bool Enabled { get; set; } = true;

        public bool NeedsDepth => true;

        public void Apply(PostProcessTarget target) => inspect(target);
    }

    [Fact]
    public void PostProcessTarget_ProjectionRoundTrips()
    {
        var scene = Corner(64);
        new Renderer().Render(scene, new GouraudPainter());

        PostProcessTarget? captured = null;

        var stack = new PostProcessStack();
        stack.Effects.Add(new DepthProbeEffect(t => captured = t));
        stack.Apply(scene.Surface, scene.Projection);

        var target = captured!;
        Assert.True(target.HasDepth);

        // Every pixel showing geometry must project back onto itself.
        var checkedAny = false;

        for (var y = 4; y < target.Height - 4; y += 5)
        {
            for (var x = 4; x < target.Width - 4; x += 5)
            {
                if (float.IsPositiveInfinity(target.ViewDepth[x + y * target.Width]))
                {
                    continue;
                }

                var position = target.ViewPositionAt(x, y);

                Assert.True(target.ProjectToScreen(position, out var backX, out var backY, out _));
                Assert.Equal(x, backX);
                Assert.Equal(y, backY);

                checkedAny = true;
            }
        }

        Assert.True(checkedAny, "the scene drew nothing to check");
    }
}
