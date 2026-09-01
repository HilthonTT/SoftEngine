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

namespace SoftEngine.Core.Tests.Pipeline;

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

        Assert.True(motion.X > 1f, $"expected a rightward motion of several pixels, got {motion}");
        Assert.True(MathF.Abs(motion.Y) < 0.5f, $"nothing moved vertically, got {motion}");
    }

    [Fact]
    public void Velocity_MeasuresTheCameraMoving()
    {
        var (renderer, scene, _, camera) = Setup();

        renderer.Render(scene, new FlatPainter());

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

        Assert.Equal(TemporalJitter.Phases, seen.Count);

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

            var shifted = (after.Y / after.W - before.Y / before.W) * (64 - 1) * 0.5f;

            Assert.Equal(0.5f, shifted, 3);
        }
    }

    [Fact]
    public void TemporalAntiAliasing_ConvergesTowardASupersampledFrame()
    {
        const int size = 48;

        var reference = Supersampled(size, factor: 4);

        var single = Resolved(size, temporal: false);
        var accumulated = Resolved(size, temporal: true);

        var singleError = MeanError(single, reference);
        var temporalError = MeanError(accumulated, reference);

        Assert.True(temporalError < singleError,
            $"temporal resolve should approach the supersampled frame: {singleError:0.###} → {temporalError:0.###}");
    }

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

    private static int[] Resolved(int size, bool temporal)
    {
        var (renderer, scene, _, _) = Setup(size);
        renderer.Settings.TemporalAntiAliasing = temporal;

        var frames = temporal ? 24 : 1;

        for (var frame = 0; frame < frames; frame++)
        {
            renderer.Render(scene, new FlatPainter());
        }

        return [.. scene.Surface.Screen];
    }

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

        Assert.Equal(SharpestStep(0f, blur: false), SharpestStep(0f, blur: true), 1);
    }

    [Fact]
    public void VelocityView_DrawsSomethingOnlyWhenThereIsMotionToDraw()
    {
        var (renderer, scene, cube, _) = Setup();
        renderer.Settings.TemporalAntiAliasing = false;
        renderer.Settings.DebugView = DebugView.Velocity;

        renderer.Render(scene, new FlatPainter());

        var first = scene.Surface.GetColor(32, 32);

        cube.Position = new Vector3(2f, 0f, 0f);
        renderer.Render(scene, new FlatPainter());

        var second = scene.Surface.GetColor(32, 32);

        Assert.NotEqual(first, second);

        var background = ColorRGB.FromPacked(scene.Surface.GetColor(0, 0));
        Assert.Equal(0, background.R);
    }
}
