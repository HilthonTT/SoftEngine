namespace SoftEngine.Core.Diagnostics;

/// <summary>Every write attempt at one pixel over one frame, in the order the pipeline made them.</summary>
public sealed class PixelHistory(int x, int y, long frameNumber)
{
    public int X { get; } = x;

    public int Y { get; } = y;

    public long FrameNumber { get; } = frameNumber;

    public List<PixelWrite> Writes { get; } = [];

    /// <summary>The render target's content at this pixel once the frame finished.</summary>
    public int FinalColor { get; internal set; }

    /// <summary>The z-buffer's content at this pixel once the frame finished.</summary>
    public int FinalDepth { get; internal set; }
}
