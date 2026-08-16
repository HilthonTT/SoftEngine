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

    /// <summary>
    /// Triangles belonging to meshes the occlusion pass rejected whole, because something
    /// nearer already covered every pixel they could have reached.
    ///
    /// <para>
    /// Counted apart from <see cref="OutOfViewTriangleCount"/> rather than folded into it. A
    /// mesh rejected here was inside the frustum and facing the camera — it was in view, and
    /// merely hidden — so adding it to the out-of-view total would answer a question nobody
    /// asked and lose the one number that says whether the pass is paying for itself.
    /// </para>
    /// </summary>
    public int OccludedMeshTriangleCount { get; internal set; }

    /// <summary>Meshes the occlusion pass rejected whole.</summary>
    public int OccludedMeshCount { get; internal set; }

    /// <summary>Meshes the occlusion pass rasterized to do it.</summary>
    public int OccluderMeshCount { get; internal set; }

    /// <summary>
    /// Transparent fragments stored for the order-independent resolve, and the pixels they
    /// covered. Both zero when the frame sorted its transparent triangles instead.
    /// </summary>
    public int TransparentFragmentCount { get; internal set; }

    /// <summary>Pixels holding at least one stored transparent fragment.</summary>
    public int TransparentPixelCount { get; internal set; }

    /// <summary>
    /// How many times a pixel held its maximum number of fragments and had to composite its two
    /// farthest into one. Zero means the frame's transparency resolved exactly; a large number
    /// means the scene wants a bigger <see cref="Buffers.FragmentBuffer.Capacity"/>.
    /// </summary>
    public int TransparentOverflowCount { get; internal set; }

    private int _occludedTriangleCount;

    /// <summary>
    /// Ints per stripe: one 64-byte cache line, which is what a stripe is for.
    /// </summary>
    private const int StripeStride = 16;

    /// <summary>
    /// How many stripes the pixel counters are spread over — a power of two at least as large
    /// as the machine's thread count, so the fill's workers rarely land on the same one, and
    /// capped because past a point the summing costs more than the collisions would.
    /// </summary>
    private static readonly int StripeCount = StripeCountFor(Environment.ProcessorCount);

    private static readonly int StripeMask = StripeCount - 1;

    /// <summary>
    /// The two pixel counters, one pair per stripe, each pair alone on its own cache line.
    ///
    /// <para>
    /// They used to be two ints that every tile in the fill phase incremented atomically once
    /// per scanline — on a twenty-thread machine drawing a frame of small triangles, half a
    /// million atomic increments a frame onto the same two words. The counters were correct and
    /// the cost was not in the increments: it was the cache line they shared, bouncing between
    /// every core in the machine for the whole of the fill. Measured on the benchmark scenes,
    /// that one line was between a third and two thirds of the frame — the 4,096-cube scene
    /// spent more time contending over its own statistics than it spent rasterizing.
    /// </para>
    ///
    /// <para>
    /// Striping gives each worker a line of its own to add into and sums them when somebody
    /// asks, which is once a frame. The adds stay atomic because two threads can still hash to
    /// the same stripe and the totals have to stay exact — but an atomic add to a line nobody
    /// else is touching costs what an ordinary one does.
    /// </para>
    ///
    /// <para>
    /// Drawn and rejected sit next to each other within a stripe on purpose: they are written
    /// together, so a scanline touches one line rather than two.
    /// </para>
    /// </summary>
    private readonly int[] _pixelStripes = new int[StripeCount * StripeStride];

    public int DrawnPixelCount => SumStripes(0);

    public int BehindZPixelCount => SumStripes(1);

    /// <summary>
    /// Triangle-in-tile pairs the fill phase dropped whole because the tile was already
    /// covered by nearer geometry. Counted per tile, so a triangle spanning several tiles
    /// can be rejected by some and drawn by others.
    /// </summary>
    public int OccludedTriangleCount => _occludedTriangleCount;

    /// <summary>Thread-safe batched count of coarse-depth rejections, flushed once per tile.</summary>
    public void AddOccludedTriangles(int count) => Interlocked.Add(ref _occludedTriangleCount, count);

    /// <summary>
    /// Thread-safe batched pixel counts, flushed by the rasterizer per scanline. See
    /// <see cref="_pixelStripes"/> for why the calling thread decides where they land.
    /// </summary>
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

    /// <summary>
    /// One counter's total across every stripe. Read once a frame, against adds that happen
    /// hundreds of thousands of times in one — which is the whole shape of the trade.
    /// </summary>
    private int SumStripes(int offset)
    {
        var total = 0;

        for (var i = offset; i < _pixelStripes.Length; i += StripeStride)
        {
            total += Volatile.Read(ref _pixelStripes[i]);
        }

        return total;
    }

    /// <summary>The smallest power of two at or above the thread count, between 8 and 64.</summary>
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
