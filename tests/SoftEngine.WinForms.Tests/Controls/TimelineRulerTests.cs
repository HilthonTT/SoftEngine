using SoftEngine.WinForms.Controls;

namespace SoftEngine.WinForms.Tests.Controls;

public class TimelineRulerTests
{
    [Theory]
    [InlineData(1.01f)] // 24.24 frames: the ruler draws 25
    [InlineData(2.5f)]
    [InlineData(1f)]
    public void ClickingATick_PosesTheFrameItIsLabelled(float duration)
    {
        using var ruler = new TimelineRuler { Width = 600, Height = 40, Duration = duration };
        var lastWholeFrame = (int)MathF.Floor(duration * TimelineRuler.FramesPerSecond);

        for (var frame = 0; frame <= lastWholeFrame; frame++)
        {
            var x = (int)MathF.Round(ruler.XOf(frame));
            var posed = ruler.TimeAt(x) * TimelineRuler.FramesPerSecond;

            Assert.Equal(frame, MathF.Round(posed));
        }
    }

    [Fact]
    public void ThePastTheEndTick_PosesNoLaterThanTheClipEnds()
    {
        using var ruler = new TimelineRuler { Width = 600, Height = 40, Duration = 1.01f };

        Assert.Equal(1.01f, ruler.TimeAt(ruler.Width));
    }
}
