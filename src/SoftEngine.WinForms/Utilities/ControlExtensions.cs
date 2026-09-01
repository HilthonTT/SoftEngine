using System.Numerics;

namespace SoftEngine.WinForms.Utilities;

public static class ControlHelper
{
    public static Vector2 NormalizeAroundCenter(this Control control, Point position)
    {
        var size = control.ClientSize;
        var extent = MathF.Max(1f, MathF.Min(size.Width, size.Height)) * 0.5f;

        return new Vector2(
            (position.X - size.Width * 0.5f) / extent,
            (position.Y - size.Height * 0.5f) / extent);
    }
}
