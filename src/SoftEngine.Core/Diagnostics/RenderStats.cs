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

    private int _drawnPixelCount;
    private int _behindZPixelCount;

    public int DrawnPixelCount => _drawnPixelCount;

    public int BehindZPixelCount => _behindZPixelCount;

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
        _drawnPixelCount = 0;
        _behindZPixelCount = 0;
    }
}
