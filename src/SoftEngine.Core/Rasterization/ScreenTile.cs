namespace SoftEngine.Core.Rasterization;

public readonly struct ScreenTile(int xFrom, int yFrom, int xTo, int yTo)
{
    public readonly int XFrom = xFrom;
    public readonly int YFrom = yFrom;
    public readonly int XTo = xTo;
    public readonly int YTo = yTo;

    public static readonly ScreenTile Full = new(0, 0, int.MaxValue, int.MaxValue);
}
