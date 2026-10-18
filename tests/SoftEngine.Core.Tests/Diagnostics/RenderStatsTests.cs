using SoftEngine.Core.Diagnostics;

namespace SoftEngine.Core.Tests.Diagnostics;

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
