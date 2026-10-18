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

    public int NearClippedTriangleCount { get; internal set; }

    public int OccludedMeshTriangleCount { get; internal set; }

    public int OccludedMeshCount { get; internal set; }

    public int OccluderMeshCount { get; internal set; }

    public int TransparentFragmentCount { get; internal set; }

    public int TransparentPixelCount { get; internal set; }

    public int TransparentOverflowCount { get; internal set; }

    private int _occludedTriangleCount;

    private const int StripeStride = 16;

    private static readonly int StripeCount = StripeCountFor(Environment.ProcessorCount);

    private static readonly int StripeMask = StripeCount - 1;

    private readonly int[] _pixelStripes = new int[StripeCount * StripeStride];

    public int DrawnPixelCount => SumStripes(0);

    public int BehindZPixelCount => SumStripes(1);

    public int OccludedTriangleCount => _occludedTriangleCount;

    public void AddOccludedTriangles(int count) => Interlocked.Add(ref _occludedTriangleCount, count);

    public void AddPixelCounts(int drawn, int behindZ)
    {
        if (drawn == 0 && behindZ == 0)
        {
            return;
        }

        var stripe = (Environment.CurrentManagedThreadId & StripeMask) * StripeStride;

        if (drawn != 0)
        {
            Interlocked.Add(ref _pixelStripes[stripe], drawn);
        }
        if (behindZ != 0)
        {
            Interlocked.Add(ref _pixelStripes[stripe + 1], behindZ);
        }
    }

    private int SumStripes(int offset)
    {
        var total = 0;

        for (var i = offset; i < _pixelStripes.Length; i += StripeStride)
        {
            total += Volatile.Read(ref _pixelStripes[i]);
        }

        return total;
    }

    private static int StripeCountFor(int processors)
    {
        var stripes = 8;

        while (stripes < processors && stripes < 64)
        {
            stripes *= 2;
        }

        return stripes;
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
        OccludedMeshTriangleCount = 0;
        OccludedMeshCount = 0;
        OccluderMeshCount = 0;
        TransparentFragmentCount = 0;
        TransparentPixelCount = 0;
        TransparentOverflowCount = 0;
        Array.Clear(_pixelStripes);
        _occludedTriangleCount = 0;
    }
}
