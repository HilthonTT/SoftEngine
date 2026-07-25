using System.Numerics;

namespace SoftEngine.WinForms.Extensions;

public static class ControlHelper
{
    /// <summary>
    /// Maps a point in a control to coordinates centred on it, with the shorter side spanning
    /// -1..1 in both axes. Keeping the units square is what stops a drag on a wide viewport
    /// turning further horizontally than the same drag does vertically.
    /// </summary>
    public static Vector2 NormalizeAroundCenter(this Control control, Point position)
    {
        var size = control.ClientSize;
        var extent = MathF.Max(1f, MathF.Min(size.Width, size.Height)) * 0.5f;

        return new Vector2(
            (position.X - size.Width * 0.5f) / extent,
            (position.Y - size.Height * 0.5f) / extent);
    }
}
