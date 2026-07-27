using System.Diagnostics;

namespace SoftEngine.Benchmarks;

/// <summary>What one scene measured to.</summary>
internal readonly record struct BenchmarkResult(
    string Scene,
    double MedianMs,
    double MinMs,
    double P95Ms,
    int Triangles,
    int DrawnTriangles,
    int Pixels);

internal static class BenchmarkRunner
{
    /// <summary>
    /// Renders a scene <paramref name="frames"/> times and reports the median.
    ///
    /// The median rather than the mean, because a frame time distribution on a desktop OS has
    /// a long right tail that belongs to the scheduler rather than to the renderer — one
    /// preempted frame moves a mean and cannot move a median. Warm-up frames are discarded for
    /// the same reason in the other direction: the first frame through a scene pays for JIT,
    /// for the vertex buffers, the tile bins and the mip chains being allocated, and for the
    /// prefiltered environment when there is one.
    /// </summary>
    public static BenchmarkResult Run(BenchmarkScene scene, int width, int height, int frames, int warmup, bool hierarchicalZ)
    {
        var (renderer, built, painter) = scene.Build(width, height);
        renderer.Settings.HierarchicalZ = hierarchicalZ;

        for (var i = 0; i < warmup; i++)
        {
            renderer.Render(built, painter);
        }

        var samples = new double[frames];

        for (var i = 0; i < frames; i++)
        {
            var start = Stopwatch.GetTimestamp();
            renderer.Render(built, painter);
            samples[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        Array.Sort(samples);

        return new BenchmarkResult(
            scene.Name,
            samples[frames / 2],
            samples[0],
            samples[System.Math.Min((int)(frames * 0.95), frames - 1)],
            renderer.Stats.TotalTriangleCount,
            renderer.Stats.DrawnTriangleCount,
            renderer.Stats.DrawnPixelCount);
    }
}
