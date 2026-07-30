using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Geometry.Primitives;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Pipeline.Debugging;
using SoftEngine.Core.Pipeline.Temporal;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Cameras;
using SoftEngine.Core.Scenes.Projections;
using System.Numerics;

namespace SoftEngine.Core.Tests;

public class TemporalTests
{
    private sealed class MovableCamera(Vector3 position) : ICamera
    {
        public Vector3 Position { get; set; } = position;

        public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Position - Vector3.UnitZ, Vector3.UnitY);
    }

    private static Scene SceneWith(IWorld world, ICamera camera, int size = 64)
    {
        return new Scene
        {
            World = world,
            Camera = camera,
            Projection = new PerspectiveProjection(MathF.PI / 4f, 0.1f, 100f),
            Surface = new FrameBuffer(size, size) { Stats = new RenderStats() },
            GammaCorrect = true,
            HighDynamicRange = true,
        };
    }

    private static (Renderer Renderer, Scene Scene, Cube Cube, MovableCamera Camera) Setup(int size = 64)
    {
        var world = new SimpleWorld();
        var cube = new Cube { Scale = new Vector3(4f, 4f, 4f) };
        world.Meshes.Add(cube);

        var camera = new MovableCamera(new Vector3(0, 0, 12f));
        var scene = SceneWith(world, camera, size);

        var renderer = new Renderer();
        renderer.Settings.TemporalAntiAliasing = true;

        return (renderer, scene, cube, camera);
    }

    [Fact]
    public void Velocity_IsEmptyOnTheFirstFrame()
    {
        var (renderer, scene, _, _) = Setup();

        renderer.Render(scene, new FlatPainter());

        // Nothing to compare against: every velocity is zero because nothing is known, and the
        // buffer says so rather than claiming a still frame.
        Assert.False(renderer.Velocity.IsFilled);
    }

    [Fact]
    public void Velocity_IsZeroWhenNothingMoves()
    {
        var (renderer, scene, _, _) = Setup();

        renderer.Render(scene, new FlatPainter());
        renderer.Render(scene, new FlatPainter());

        Assert.True(renderer.Velocity.IsFilled);
        Assert.True(renderer.Velocity.IsCovered(32, 32));

        Assert.True(renderer.Velocity.MaxSpeed() < 0.01f,
            $"a still scene should not be moving, got {renderer.Velocity.MaxSpeed()} px");
    }

    [Fact]
    public void Velocity_MeasuresAMeshMovingRight()
    {
        var (renderer, scene, cube, _) = Setup();

        renderer.Render(scene, new FlatPainter());

        cube.Position = new Vector3(1f, 0f, 0f);
        renderer.Render(scene, new FlatPainter());

        var velocity = renderer.Velocity;

        Assert.True(velocity.IsCovered(32, 32));

        var motion = velocity.At(32, 32);

        // The mesh moved along +x in the world, which is to the right on screen — and the velocity
        // points back at where the surface came from, so it is positive.
        Assert.True(motion.X > 1f, $"expected a rightward motion of several pixels, got {motion}");
        Assert.True(MathF.Abs(motion.Y) < 0.5f, $"nothing moved vertically, got {motion}");
    }

    [Fact]
    public void Velocity_MeasuresTheCameraMoving()
    {
        var (renderer, scene, _, camera) = Setup();

        renderer.Render(scene, new FlatPainter());

        // The camera slides right, so the world slides left across the frame — and a velocity that
        // says where a surface came from points the other way again.
        camera.Position = new Vector3(1f, 0f, 12f);
        renderer.Render(scene, new FlatPainter());

        var motion = renderer.Velocity.At(32, 32);

        Assert.True(motion.X < -1f, $"expected a leftward motion of several pixels, got {motion}");
    }

    [Fact]
    public void Velocity_ScalesWithHowFarSomethingMoved()
    {
        static float Speed(float distance)
        {
            var (renderer, scene, cube, _) = Setup();

            renderer.Render(scene, new FlatPainter());

            cube.Position = new Vector3(distance, 0f, 0f);
            renderer.Render(scene, new FlatPainter());

            return renderer.Velocity.At(32, 32).X;
        }

        var single = Speed(0.5f);
        var doubled = Speed(1f);

        Assert.True(single > 0.5f, $"expected a measurable motion, got {single}");
        Assert.Equal(2f, doubled / single, 1);
    }

    [Fact]
    public void Velocity_LeavesTheBackgroundUncovered()
    {
        var (renderer, scene, _, _) = Setup();

        renderer.Render(scene, new FlatPainter());
        renderer.Render(scene, new FlatPainter());

        // The cube covers the middle of the frame and nothing covers the corner.
        Assert.True(renderer.Velocity.IsCovered(32, 32));
        Assert.False(renderer.Velocity.IsCovered(0, 0));
    }

    [Fact]
    public void Velocity_IsNotMeasuredWhenNothingAsksForIt()
    {
        var (renderer, scene, _, _) = Setup();
        renderer.Settings.TemporalAntiAliasing = false;

        renderer.Render(scene, new FlatPainter());
        renderer.Render(scene, new FlatPainter());

        Assert.False(renderer.Velocity.IsFilled);
    }

    [Fact]
    public void ResetHistory_MakesTheNextFrameTheFirstOne()
    {
        var (renderer, scene, _, _) = Setup();

        renderer.Render(scene, new FlatPainter());
        renderer.Render(scene, new FlatPainter());

        Assert.True(renderer.Velocity.IsFilled);

        renderer.ResetHistory();
        renderer.Render(scene, new FlatPainter());

        Assert.False(renderer.Velocity.IsFilled);
    }

    [Fact]
    public void Jitter_MovesTheSampleGridAndNothingElse()
    {
        var projection = new PerspectiveProjection(MathF.PI / 4f, 0.1f, 100f).ProjectionMatrix(64, 64);

        var jittered = TemporalJitter.Apply(projection, new Vector2(0.25f, 0f), 64, 64);

        // A point at any depth has to shift by the same number of pixels, which is what makes this a
        // change to where the frame is sampled rather than a change to where anything is.
        foreach (var depth in new[] { -2f, -20f, -80f })
        {
            var view = new Vector4(0.3f, -0.2f, depth, 1f);

            var before = Vector4.Transform(view, projection);
            var after = Vector4.Transform(view, jittered);

            var shifted = (after.X / after.W - before.X / before.W) * (64 - 1) * 0.5f;

            Assert.Equal(0.25f, shifted, 3);
            Assert.Equal(before.Y / before.W, after.Y / after.W, 5);
            Assert.Equal(before.Z / before.W, after.Z / after.W, 5);
        }
    }

    [Fact]
    public void Jitter_WalksAWholeCycleWithinThePixel()
    {
        var seen = new HashSet<Vector2>();

        for (var frame = 0; frame < TemporalJitter.Phases; frame++)
        {
            var offset = TemporalJitter.Offset(frame);

            Assert.True(MathF.Abs(offset.X) <= 0.5f, $"{offset} is outside the pixel");
            Assert.True(MathF.Abs(offset.Y) <= 0.5f, $"{offset} is outside the pixel");

            seen.Add(offset);
        }

        // Every phase distinct, or the average is of fewer samples than it claims.
        Assert.Equal(TemporalJitter.Phases, seen.Count);

        // And it repeats, so a still image converges instead of drifting.
        Assert.Equal(TemporalJitter.Offset(0), TemporalJitter.Offset(TemporalJitter.Phases));
    }

    [Fact]
    public void Jitter_OnAParallelProjectionShiftsEveryDepthEqually()
    {
        var projection = new OrthographicProjection(10f, 0.1f, 100f).ProjectionMatrix(64, 64);
        var jittered = TemporalJitter.Apply(projection, new Vector2(0f, 0.5f), 64, 64);

        foreach (var depth in new[] { -1f, -50f })
        {
            var view = new Vector4(0.1f, 0.4f, depth, 1f);

            var before = Vector4.Transform(view, projection);
            var after = Vector4.Transform(view, jittered);

            // Screen y counts downward, so a positive offset moves ndc y up by the same amount.
            var shifted = (after.Y / after.W - before.Y / before.W) * (64 - 1) * 0.5f;

            Assert.Equal(0.5f, shifted, 3);
        }
    }

    [Fact]
    public void TemporalAntiAliasing_ConvergesTowardASupersampledFrame()
    {
        // The claim being tested is not "the image changes" but "the image gets closer to the right
        // answer" — and there is a right answer available to compare against, because supersampling
        // computes the same average by rendering the area instead of the frames.
        const int size = 48;

        var reference = Supersampled(size, factor: 4);

        var single = Resolved(size, temporal: false);
        var accumulated = Resolved(size, temporal: true);

        var singleError = MeanError(single, reference);
        var temporalError = MeanError(accumulated, reference);

        Assert.True(temporalError < singleError,
            $"temporal resolve should approach the supersampled frame: {singleError:0.###} → {temporalError:0.###}");
    }

    /// <summary>The scene rendered at a multiple of the resolution and averaged down — the answer.</summary>
    private static int[] Supersampled(int size, int factor)
    {
        var world = new SimpleWorld();
        world.Meshes.Add(new Cube { Scale = new Vector3(4f, 4f, 4f) });

        var scene = SceneWith(world, new MovableCamera(new Vector3(0, 0, 12f)), size);
        scene.Surface = new FrameBuffer(size * factor, size * factor) { Stats = new RenderStats() };

        new Renderer().Render(scene, new FlatPainter());

        var resolved = new int[size * size];
        SuperSampler.Resolve(scene.Surface, resolved, size, size, factor);

        return resolved;
    }

    /// <summary>The same scene at one sample per pixel, with and without the temporal average.</summary>
    private static int[] Resolved(int size, bool temporal)
    {
        var (renderer, scene, _, _) = Setup(size);
        renderer.Settings.TemporalAntiAliasing = temporal;

        // Long enough for the blend to have converged: at 10% per frame, twenty-four frames is
        // within a percent of the limit.
        var frames = temporal ? 24 : 1;

        for (var frame = 0; frame < frames; frame++)
        {
            renderer.Render(scene, new FlatPainter());
        }

        return [.. scene.Surface.Screen];
    }

    /// <summary>Mean absolute per-channel difference between two frames, in byte levels.</summary>
    private static float MeanError(int[] frame, int[] reference)
    {
        var total = 0f;

        for (var i = 0; i < reference.Length; i++)
        {
            var a = ColorRGB.FromPacked(frame[i]);
            var b = ColorRGB.FromPacked(reference[i]);

            total += MathF.Abs(a.R - b.R) + MathF.Abs(a.G - b.G) + MathF.Abs(a.B - b.B);
        }

        return total / (reference.Length * 3);
    }

    [Fact]
    public void TemporalAntiAliasing_LeavesAStillFrameLookingLikeItself()
    {
        // Converging must not mean drifting: after many frames of a still scene, the middle of a flat
        // surface has to hold the colour it was shaded, not something the blend wandered to.
        var (renderer, scene, _, _) = Setup();

        renderer.Render(scene, new FlatPainter());
        var shaded = ColorRGB.FromPacked(scene.Surface.GetColor(32, 32));

        for (var frame = 0; frame < 24; frame++)
        {
            renderer.Render(scene, new FlatPainter());
        }

        var settled = ColorRGB.FromPacked(scene.Surface.GetColor(32, 32));

        Assert.True(MathF.Abs(settled.R - shaded.R) <= 2, $"{shaded.R} drifted to {settled.R}");
        Assert.True(MathF.Abs(settled.G - shaded.G) <= 2, $"{shaded.G} drifted to {settled.G}");
    }

    [Fact]
    public void MotionBlur_SmearsAMovingMeshAndLeavesAStillOneAlone()
    {
        // The sharpest step along a row through the middle of the frame. A silhouette with nothing
        // blurring it steps from background to surface in one pixel; smeared over several, the
        // largest single step falls in proportion. Counting "partly shaded" pixels would not show it
        // — the cube's own faces are already mid-grey — and the total variation across a monotone
        // edge is unchanged by blurring it, which is exactly what an edge like this is.
        static float SharpestStep(float distance, bool blur)
        {
            var (renderer, scene, cube, _) = Setup();

            renderer.Settings.TemporalAntiAliasing = false;
            renderer.Settings.MotionBlur = blur;
            renderer.MotionBlur.ShutterFraction = 1f;

            renderer.Render(scene, new FlatPainter());

            cube.Position = new Vector3(distance, 0f, 0f);
            renderer.Render(scene, new FlatPainter());

            var surface = scene.Surface;
            var sharpest = 0f;

            for (var x = 1; x < surface.Width; x++)
            {
                var left = ColorRGB.FromPacked(surface.GetColor(x - 1, 32));
                var right = ColorRGB.FromPacked(surface.GetColor(x, 32));

                var step = MathF.Abs(left.R - right.R) + MathF.Abs(left.G - right.G) + MathF.Abs(left.B - right.B);

                sharpest = MathF.Max(sharpest, step);
            }

            return sharpest;
        }

        var sharp = SharpestStep(1.5f, blur: false);
        var smeared = SharpestStep(1.5f, blur: true);

        Assert.True(smeared < sharp * 0.75f, $"a moving mesh should smear: a step of {sharp} became {smeared}");

        // And a still one must come out untouched: the shutter is open over no motion at all.
        Assert.Equal(SharpestStep(0f, blur: false), SharpestStep(0f, blur: true), 1);
    }

    [Fact]
    public void VelocityView_DrawsSomethingOnlyWhenThereIsMotionToDraw()
    {
        var (renderer, scene, cube, _) = Setup();
        renderer.Settings.TemporalAntiAliasing = false;
        renderer.Settings.DebugView = DebugView.Velocity;

        renderer.Render(scene, new FlatPainter());

        // First frame: the pass ran but had nothing to compare against, so the view has nothing to
        // show and leaves the shaded image alone.
        var first = scene.Surface.GetColor(32, 32);

        cube.Position = new Vector3(2f, 0f, 0f);
        renderer.Render(scene, new FlatPainter());

        var second = scene.Surface.GetColor(32, 32);

        Assert.NotEqual(first, second);

        // Grey where nothing moves, coloured where something does.
        var background = ColorRGB.FromPacked(scene.Surface.GetColor(0, 0));
        Assert.Equal(0, background.R);
    }
}
