using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry.Primitives;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Cameras;
using SoftEngine.Core.Scenes.Projections;
using SoftEngine.WinForms.Debugging;
using System.Numerics;

namespace SoftEngine.WinForms.Tests.Debugging;

public class FrameHistoryCursorTests
{
    private sealed class FixedCamera : ICamera
    {
        public Vector3 Position { get; set; } = new(0f, 0f, 6f);

        public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Vector3.Zero, Vector3.UnitY);
    }

    private static (Renderer Renderer, Scene Scene) Build(int capacity)
    {
        var renderer = new Renderer();
        renderer.Diagnostics.HistoryCapacity = capacity;

        var scene = new Scene
        {
            Surface = new FrameBuffer(16, 16) { Stats = renderer.Stats },
            Camera = new FixedCamera(),
            Projection = new PerspectiveProjection(MathF.PI / 4f, 0.1f, 100f),
            World = new SimpleWorld { Meshes = [new Cube()] },
        };

        return (renderer, scene);
    }

    private static IReadOnlyList<FrameCapture> Render(Renderer renderer, Scene scene, int frames)
    {
        for (var i = 0; i < frames; i++)
        {
            renderer.Render(scene, new FlatPainter());
        }

        return renderer.Diagnostics.Frames;
    }

    private static IReadOnlyList<FrameCapture> Render(int frames)
    {
        var (renderer, scene) = Build(capacity: 8);

        return Render(renderer, scene, frames);
    }

    [Fact]
    public void Following_StepBackPinsTheNewestFrame()
    {
        var frames = Render(3);
        var cursor = new FrameHistoryCursor();

        Assert.True(cursor.Step(frames, -1));

        Assert.Equal(frames[^1].FrameNumber, cursor.PinnedFrameNumber);
        Assert.Equal(2, cursor.IndexIn(frames));
    }

    [Fact]
    public void Following_StepForwardHasNowhereToGo()
    {
        var frames = Render(3);
        var cursor = new FrameHistoryCursor();

        Assert.False(cursor.Step(frames, +1));
        Assert.False(cursor.IsPinned);
        Assert.False(cursor.CanStepForward(frames));
    }

    [Fact]
    public void StepForwardPastTheNewest_FollowsTheRendererAgain()
    {
        var frames = Render(3);
        var cursor = new FrameHistoryCursor();
        cursor.Pin(frames[^1].FrameNumber);

        Assert.True(cursor.Step(frames, +1));

        Assert.False(cursor.IsPinned);
        Assert.Null(cursor.PinnedIn(frames));
    }

    [Fact]
    public void StepBackAtTheOldest_StaysOnIt()
    {
        var frames = Render(3);
        var cursor = new FrameHistoryCursor();
        cursor.Pin(frames[0].FrameNumber);

        cursor.Step(frames, -1);

        Assert.Equal(frames[0].FrameNumber, cursor.PinnedFrameNumber);
        Assert.False(cursor.CanStepBack(frames));
        Assert.True(cursor.CanStepForward(frames));
    }

    [Fact]
    public void APinnedFrameThatAgedOut_ShowsTheOldestStillKept()
    {
        var (renderer, scene) = Build(capacity: 2);
        var cursor = new FrameHistoryCursor();
        cursor.Pin(Render(renderer, scene, 2)[0].FrameNumber);

        var frames = Render(renderer, scene, 3);

        Assert.Equal(0, cursor.IndexIn(frames));
        Assert.Same(frames[0], cursor.PinnedIn(frames));
        Assert.NotEqual(cursor.PinnedFrameNumber, frames[0].FrameNumber);
    }

    [Fact]
    public void AnEmptyHistory_CannotBeStepped()
    {
        var cursor = new FrameHistoryCursor();

        Assert.False(cursor.Step([], -1));
        Assert.Equal(-1, cursor.IndexIn([]));
        Assert.False(cursor.CanStepBack([]));
    }

    [Fact]
    public void Release_ReportsWhetherAnythingWasPinned()
    {
        var cursor = new FrameHistoryCursor();

        Assert.False(cursor.Release());

        cursor.Pin(4);

        Assert.True(cursor.Release());
        Assert.False(cursor.IsPinned);
    }
}
