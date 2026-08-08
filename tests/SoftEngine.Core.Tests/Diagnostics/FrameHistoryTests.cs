using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry.Primitives;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Cameras;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Scenes.Projections;
using System.Numerics;

namespace SoftEngine.Core.Tests;

public class FrameHistoryTests
{
    private sealed class FixedCamera : ICamera
    {
        public Vector3 Position { get; set; } = new(0f, 0f, 6f);

        public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Vector3.Zero, Vector3.UnitY);
    }

    private static (Renderer Renderer, Scene Scene) Build()
    {
        var renderer = new Renderer();

        var scene = new Scene
        {
            Surface = new FrameBuffer(64, 48) { Stats = renderer.Stats },
            Camera = new FixedCamera(),
            Projection = new PerspectiveProjection(MathF.PI / 4f, 0.1f, 100f),
            World = new SimpleWorld
            {
                Meshes = [new Cube()],
                Lights = [new DirectionalLight { Direction = new Vector3(-0.3f, -0.6f, -0.7f) }],
            },
        };

        return (renderer, scene);
    }

    /// <summary>
    /// Off by default. Keeping frames is the one piece of instrumentation here that genuinely
    /// allocates — a copy of the event buffer per frame — so nobody pays for it unasked.
    /// </summary>
    [Fact]
    public void History_IsOffUntilACapacityIsSet()
    {
        var (renderer, scene) = Build();

        renderer.Render(scene, new GouraudPainter());
        renderer.Render(scene, new GouraudPainter());

        Assert.Empty(renderer.Diagnostics.Frames);
    }

    [Fact]
    public void History_KeepsTheMostRecentFramesUpToItsCapacity()
    {
        var (renderer, scene) = Build();
        renderer.Diagnostics.HistoryCapacity = 3;

        for (var i = 0; i < 7; i++)
        {
            renderer.Render(scene, new GouraudPainter());
        }

        var frames = renderer.Diagnostics.Frames;

        Assert.Equal(3, frames.Count);

        // Oldest first, and the three that survived are the last three rendered.
        Assert.Equal(5, frames[0].FrameNumber);
        Assert.Equal(6, frames[1].FrameNumber);
        Assert.Equal(7, frames[2].FrameNumber);
    }

    /// <summary>
    /// The event log is a single growable array reused frame after frame. A capture holding a
    /// reference to it would describe whichever frame happened to be rendering when it was read,
    /// which is exactly the failure a history exists to prevent.
    /// </summary>
    [Fact]
    public void Capture_CopiesTheEventsRatherThanReferencingTheLiveLog()
    {
        var (renderer, scene) = Build();
        renderer.Diagnostics.CaptureEvents = true;
        renderer.Diagnostics.HistoryCapacity = 4;

        renderer.Render(scene, new GouraudPainter());

        var first = renderer.Diagnostics.Frames[0];
        var recorded = (GraphicsEvent[])first.Events.Clone();

        Assert.NotEmpty(recorded);

        // Two more frames, which overwrite the live buffer several times over.
        renderer.Render(scene, new GouraudPainter());
        renderer.Render(scene, new GouraudPainter());

        Assert.Equal(recorded, renderer.Diagnostics.Frames[0].Events);
        Assert.Equal(1, renderer.Diagnostics.Frames[0].FrameNumber);
    }

    /// <summary>
    /// <see cref="RenderStats"/> is cleared and refilled every frame, so a capture that held the
    /// object would report the newest numbers under an old frame's name.
    /// </summary>
    [Fact]
    public void Capture_FreezesTheFramesOwnCounts()
    {
        var (renderer, scene) = Build();
        renderer.Diagnostics.HistoryCapacity = 4;

        renderer.Render(scene, new GouraudPainter());

        var triangles = renderer.Diagnostics.Frames[0].Stats.TotalTriangles;

        Assert.True(triangles > 0);

        // A second frame with nothing in it: the live stats drop to zero, and the kept frame
        // must not follow them down.
        ((SimpleWorld)scene.World).Meshes.Clear();
        renderer.Render(scene, new GouraudPainter());

        Assert.Equal(0, renderer.Stats.TotalTriangleCount);
        Assert.Equal(triangles, renderer.Diagnostics.Frames[0].Stats.TotalTriangles);
    }

    [Fact]
    public void HistoryCapacity_LoweredBelowWhatIsKept_TrimsTheOldest()
    {
        var (renderer, scene) = Build();
        renderer.Diagnostics.HistoryCapacity = 5;

        for (var i = 0; i < 5; i++)
        {
            renderer.Render(scene, new GouraudPainter());
        }

        renderer.Diagnostics.HistoryCapacity = 2;

        Assert.Equal(2, renderer.Diagnostics.Frames.Count);
        Assert.Equal(4, renderer.Diagnostics.Frames[0].FrameNumber);
    }

    [Fact]
    public void ClearHistory_EmptiesIt()
    {
        var (renderer, scene) = Build();
        renderer.Diagnostics.HistoryCapacity = 4;

        renderer.Render(scene, new GouraudPainter());
        renderer.Diagnostics.ClearHistory();

        Assert.Empty(renderer.Diagnostics.Frames);
    }

    [Fact]
    public void FrameCaptured_FiresOncePerRenderedFrame()
    {
        var (renderer, scene) = Build();
        renderer.Diagnostics.HistoryCapacity = 4;

        var fired = 0;
        renderer.Diagnostics.FrameCaptured += (s, e) => fired++;

        renderer.Render(scene, new GouraudPainter());
        renderer.Render(scene, new GouraudPainter());

        Assert.Equal(2, fired);
    }

    /// <summary>A frame rendered with no pixel selected carries no history, and says so.</summary>
    [Fact]
    public void Capture_WithNoProbedPixel_HasNoPixelHistory()
    {
        var (renderer, scene) = Build();
        renderer.Diagnostics.HistoryCapacity = 2;

        renderer.Render(scene, new GouraudPainter());

        Assert.Null(renderer.Diagnostics.Frames[0].PixelHistory);
    }

    [Fact]
    public void Capture_WithAProbedPixel_KeepsThatFramesWrites()
    {
        var (renderer, scene) = Build();
        renderer.Diagnostics.HistoryCapacity = 2;
        renderer.Diagnostics.SetProbe(32, 24);

        renderer.Render(scene, new GouraudPainter());

        var history = renderer.Diagnostics.Frames[0].PixelHistory;

        Assert.NotNull(history);
        Assert.Equal(1, history.FrameNumber);
        Assert.Equal(32, history.X);
    }
}
