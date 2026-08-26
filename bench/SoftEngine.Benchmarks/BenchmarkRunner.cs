using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Rasterization;
using System.Diagnostics;

namespace SoftEngine.Benchmarks;

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
    public static BenchmarkResult Run(
        BenchmarkScene scene,
        int width,
        int height,
        int frames,
        int warmup,
        bool hierarchicalZ = true,
        bool occlusionCulling = true,
        bool vectorizedSpans = true,
        bool nearestMeshesFirst = true,
        bool parallelCullPhase = true)
    {
        var (renderer, built, painter) = scene.Build(width, height);
        renderer.Settings.HierarchicalZ = hierarchicalZ;
        renderer.Settings.OcclusionCulling = occlusionCulling;
        renderer.Settings.NearestMeshesFirst = nearestMeshesFirst;

        // A static rather than a renderer setting, so it is set around the run and put back
        // afterwards. Nothing else is rendering — the harness measures one scene at a time.
        var restoreSpans = ScanlineRasterizer.VectorizedSpans;
        ScanlineRasterizer.VectorizedSpans = vectorizedSpans;

        var restoreCullPhase = Renderer.ParallelCullPhase;
        Renderer.ParallelCullPhase = parallelCullPhase;

        double[] samples;

        try
        {
            for (var i = 0; i < warmup; i++)
            {
                renderer.Render(built, painter);
            }

            samples = new double[frames];

            for (var i = 0; i < frames; i++)
            {
                var start = Stopwatch.GetTimestamp();
                renderer.Render(built, painter);
                samples[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }
        }
        finally
        {
            ScanlineRasterizer.VectorizedSpans = restoreSpans;
            Renderer.ParallelCullPhase = restoreCullPhase;
        }

        Array.Sort(samples);

        return new BenchmarkResult(
            scene.Name,
            samples[frames / 2],
            samples[0],
            samples[System.Math.Min((int)(frames * 0.95), frames - 1)],
            renderer.Stats.TotalTriangleCount,
            renderer.Stats.DrawnTriangleCount,
            renderer.Stats.DrawnPixelCount,
            renderer.Stats.OccluderMeshCount,
            renderer.Stats.OccludedMeshCount);
    }
}
