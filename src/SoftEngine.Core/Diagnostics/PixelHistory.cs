namespace SoftEngine.Core.Diagnostics;

public sealed class PixelHistory(int x, int y, long frameNumber)
{
    public int X { get; } = x;

    public int Y { get; } = y;

    public long FrameNumber { get; } = frameNumber;

    public List<PixelWrite> Writes { get; } = [];

    public int FinalColor { get; internal set; }

    public int FinalDepth { get; internal set; }
}
