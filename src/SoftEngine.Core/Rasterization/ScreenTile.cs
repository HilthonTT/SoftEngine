namespace SoftEngine.Core.Rasterization;

/// <summary>
/// The rectangular block of the render target a rasterizer call may write: columns
/// [<see cref="XFrom"/>, <see cref="XTo"/>) and rows [<see cref="YFrom"/>, <see cref="YTo"/>).
/// Giving each worker thread its own tile keeps pixel ownership disjoint, so triangles can be
/// filled in parallel without z-buffer races.
///
/// A tile is a contiguous rectangle rather than a stride of scattered rows, so a fill walks
/// the framebuffer the way it is laid out in memory, and the renderer can bin each triangle
/// into the handful of tiles it actually touches instead of handing every triangle to every
/// worker.
/// </summary>
public readonly struct ScreenTile(int xFrom, int yFrom, int xTo, int yTo)
{
    public readonly int XFrom = xFrom;
    public readonly int YFrom = yFrom;
    public readonly int XTo = xTo;
    public readonly int YTo = yTo;

    /// <summary>
    /// The whole render target — the sequential (non-parallel) tile. The bounds are open
    /// rather than the surface's size because the rasterizer clamps to the surface anyway,
    /// which keeps the struct independent of any one framebuffer.
    /// </summary>
    public static readonly ScreenTile Full = new(0, 0, int.MaxValue, int.MaxValue);
}
