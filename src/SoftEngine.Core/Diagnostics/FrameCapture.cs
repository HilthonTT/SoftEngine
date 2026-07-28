namespace SoftEngine.Core.Diagnostics;

/// <summary>
/// One finished frame's diagnostics, kept after the frame itself is gone.
///
/// <para>
/// The debugger's panels normally read the renderer's live log, which is one buffer reused every
/// frame — so the moment you see something worth looking at, the frame that produced it has
/// already been overwritten by the next one. That is fine while the camera is still and
/// unbearable while anything moves, which is exactly when the interesting frames happen.
/// </para>
///
/// <para>
/// <b>What is not here is the image.</b> A capture holds the event list, the probed pixel's
/// history and the counts — everything the three panels draw — and none of the pixels. A frame
/// at 1920×1080 is eight megabytes of colour and as much again of depth, and keeping a dozen of
/// those to answer "what did the renderer do" would spend a hundred and sixty megabytes on the
/// one question the panels never ask. Stepping back through history changes what the panels
/// show, not what the viewport shows.
/// </para>
/// </summary>
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

    /// <summary>
    /// The frame's events, copied out of the log rather than referenced. The log is a single
    /// growable array reused frame after frame, so a capture holding it would describe whichever
    /// frame happened to be rendering when you looked.
    /// </summary>
    public GraphicsEvent[] Events { get; }

    /// <summary>The probed pixel's write history, or null when no pixel was selected that frame.</summary>
    public PixelHistory? PixelHistory { get; }

    public FrameStats Stats { get; }

    public override string ToString() => $"Frame #{FrameNumber}";
}

/// <summary>
/// A frame's counts and timings, frozen. <see cref="RenderStats"/> itself is cleared and refilled
/// every frame, so a history entry needs a copy rather than the object.
/// </summary>
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

    /// <summary>Total frame time, which is the two halves the renderer measures separately.</summary>
    public long TotalTimeMs => CalculationTimeMs + PainterTimeMs;
}
