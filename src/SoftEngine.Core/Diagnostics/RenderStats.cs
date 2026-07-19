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

    public int DrawnPixelCount { get; internal set; }

    public int DrawPixelCount { get; internal set; }

    public int BehindZPixelCount { get; internal set; }

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
        _caclSw.Stop();
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
        DrawnPixelCount = 0;
        BehindZPixelCount = 0;
    }
}
