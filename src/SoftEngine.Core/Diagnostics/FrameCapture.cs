namespace SoftEngine.Core.Diagnostics;

public sealed class FrameCapture
{
    internal FrameCapture(long frameNumber, GraphicsEvent[] events, PixelHistory? pixelHistory, FrameStats stats)
    {
        FrameNumber = frameNumber;
        Events = events;
        PixelHistory = pixelHistory;
        Stats = stats;
    }

    public long FrameNumber { get; }

    public GraphicsEvent[] Events { get; }

    public PixelHistory? PixelHistory { get; }

    public FrameStats Stats { get; }

    public override string ToString() => $"Frame #{FrameNumber}";
}

public readonly record struct FrameStats(
    int TotalTriangles,
    int DrawnTriangles,
    int OutOfViewTriangles,
    int BackFacingTriangles,
    int BehindViewTriangles,
    int NearClippedTriangles,
    int OccludedMeshes,
    int OccluderMeshes,
    int DrawnPixels,
    int BehindZPixels,
    long CalculationTimeMs,
    long PainterTimeMs)
{
    public static FrameStats Of(RenderStats stats)
    {
        ArgumentNullException.ThrowIfNull(stats, nameof(stats));

        return new FrameStats(
            stats.TotalTriangleCount,
            stats.DrawnTriangleCount,
            stats.OutOfViewTriangleCount,
            stats.FacingBackTriangleCount,
            stats.BehindViewTriangleCount,
            stats.NearClippedTriangleCount,
            stats.OccludedMeshCount,
            stats.OccluderMeshCount,
            stats.DrawnPixelCount,
            stats.BehindZPixelCount,
            stats.CalculationTimeMs,
            stats.PainterTimeMs);
    }

    public long TotalTimeMs => CalculationTimeMs + PainterTimeMs;
}
