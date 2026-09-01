using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Rasterization;
using System.Diagnostics;

namespace SoftEngine.Benchmarks;

internal static class BenchmarkRunner
{
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
