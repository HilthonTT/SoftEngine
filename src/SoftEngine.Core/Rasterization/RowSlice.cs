using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Rasterization;

/// <summary>
/// The subset of screen rows a rasterizer call may write: every <see cref="Stride"/>-th
/// row starting at <see cref="Offset"/>. Giving each worker thread its own slice keeps
/// pixel ownership disjoint, so triangles can be filled in parallel without z-buffer races.
/// Interleaving rows (rather than contiguous bands) keeps the load balanced even when the
/// geometry is concentrated in a few screen bands.
/// </summary>
public readonly struct RowSlice(int offset, int stride)
{
    public readonly int Offset = offset;
    public readonly int Stride = System.Math.Max(1, stride);

    /// <summary>All rows — the sequential (non-parallel) slice.</summary>
    public static readonly RowSlice Full = new(0, 1);

    /// <summary>The first row at or after <paramref name="y"/> that belongs to this slice.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int FirstOwnedRowAtOrAfter(int y)
    {
        var remainder = (y - Offset) % Stride;
        if (remainder != 0)
        {
            y += remainder < 0 ? -remainder : Stride - remainder;
        }
        return y;
    }
}
