using System.Diagnostics;

namespace SoftEngine.Core.Diagnostics;

public sealed class RenderStats
{
    private readonly Stopwatch _caclSw = new();
    private readonly Stopwatch _painSw = new();

    public int TotalTriangleCount { get; internal set; }

    public int DrawnTriangleCount { get; internal set; }

    public int OutOfViewTriangleCount { get; internal set; }

    public int FacingBackTriangleCount { get; internal set; }

    public int BehindViewTriangleCount { get; internal set; }

    /// <summary>Triangles that straddled the near plane and were split instead of discarded.</summary>
    public int NearClippedTriangleCount { get; internal set; }

    private int _drawnPixelCount;
    private int _behindZPixelCount;
    private int _occludedTriangleCount;

    public int DrawnPixelCount => _drawnPixelCount;

    public int BehindZPixelCount => _behindZPixelCount;

    /// <summary>
    /// Triangle-in-tile pairs the fill phase dropped whole because the tile was already
    /// covered by nearer geometry. Counted per tile, so a triangle spanning several tiles
    /// can be rejected by some and drawn by others.
    /// </summary>
    public int OccludedTriangleCount => _occludedTriangleCount;

    /// <summary>Thread-safe batched count of coarse-depth rejections, flushed once per tile.</summary>
    public void AddOccludedTriangles(int count) => Interlocked.Add(ref _occludedTriangleCount, count);

    /// <summary>Thread-safe batched pixel counts, flushed by the rasterizer per scanline.</summary>
    public void AddPixelCounts(int drawn, int behindZ)
    {
        if (drawn != 0)
        {
            Interlocked.Add(ref _drawnPixelCount, drawn);
        }
        if (behindZ != 0)
        {
            Interlocked.Add(ref _behindZPixelCount, behindZ);
        }
    }

    public long CalculationTimeMs => _caclSw.ElapsedMilliseconds;

    public long PainterTimeMs => _painSw.ElapsedMilliseconds;

    public void PaintTime()
    {
        _caclSw.Stop();
        _painSw.Start();
    }

    public void CalculationTime()
    {
        _painSw.Stop();
        _caclSw.Start();
    }

    public void StopTime()
    {
        _painSw.Stop();
        _caclSw.Stop();
    }

    public void Clear()
    {
        _caclSw.Reset();
        _painSw.Reset();

        TotalTriangleCount = 0;
        DrawnTriangleCount = 0;
        FacingBackTriangleCount = 0;
        OutOfViewTriangleCount = 0;
        BehindViewTriangleCount = 0;
        NearClippedTriangleCount = 0;
        _drawnPixelCount = 0;
        _behindZPixelCount = 0;
        _occludedTriangleCount = 0;
    }
}
