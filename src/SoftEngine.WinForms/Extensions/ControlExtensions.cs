using System.Numerics;

namespace SoftEngine.WinForms.Extensions;

public static class ControlHelper
{
    public static Vector2 NormalizePointClient(this Control control, Point position) =>
        new(position.X * (2f / control.Width) - 1.0f, position.Y * (2f / control.Height) - 1.0f);
}
