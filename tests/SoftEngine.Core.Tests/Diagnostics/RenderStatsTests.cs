using SoftEngine.Core.Diagnostics;

namespace SoftEngine.Core.Tests.Diagnostics;

/// <summary>
/// The pixel counters, which the fill phase adds to from every thread it is running on.
///
/// They are striped across cache lines so that the adds stop contending — see
/// <see cref="RenderStats"/> — and striping a counter is exactly the kind of change that can
/// leave it fast and wrong. These pin down that it is still a counter: exact under
/// concurrency, and back to zero when the frame is cleared.
/// </summary>
public class RenderStatsTests
{
    [Fact]
    public void PixelCounts_AreExactAcrossThreads()
    {
        var stats = new RenderStats();

        const int threads = 16;
        const int adds = 20_000;

        Parallel.For(0, threads, _ =>
        {
            for (var i = 0; i < adds; i++)
            {
                stats.AddPixelCounts(1, 2);
            }
        });

        Assert.Equal(threads * adds, stats.DrawnPixelCount);
        Assert.Equal(threads * adds * 2, stats.BehindZPixelCount);
    }

    /// <summary>
    /// A frame's totals have to survive being read from a thread that never added to them —
    /// which is every read, since the fill phase's workers are gone by the time anybody asks.
    /// </summary>
    [Fact]
    public void PixelCounts_AddedOnOtherThreads_AreVisibleToTheCaller()
    {
        var stats = new RenderStats();

        var worker = new Thread(() => stats.AddPixelCounts(7, 11));
        worker.Start();
        worker.Join();

        Assert.Equal(7, stats.DrawnPixelCount);
        Assert.Equal(11, stats.BehindZPixelCount);
    }

    [Fact]
    public void PixelCounts_IgnoreZeroes()
    {
        var stats = new RenderStats();

        stats.AddPixelCounts(0, 0);
        stats.AddPixelCounts(3, 0);
        stats.AddPixelCounts(0, 5);

        Assert.Equal(3, stats.DrawnPixelCount);
        Assert.Equal(5, stats.BehindZPixelCount);
    }

    [Fact]
    public void Clear_ZeroesEveryStripe()
    {
        var stats = new RenderStats();

        Parallel.For(0, 16, _ => stats.AddPixelCounts(100, 100));

        stats.Clear();

        Assert.Equal(0, stats.DrawnPixelCount);
        Assert.Equal(0, stats.BehindZPixelCount);
    }
}
