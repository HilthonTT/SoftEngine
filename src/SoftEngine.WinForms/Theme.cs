using System.Drawing.Drawing2D;

namespace SoftEngine.WinForms;

/// <summary>Shared color palette and paint helpers for the dark UI.</summary>
internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(30, 30, 34);
    public static readonly Color Surface = Color.FromArgb(37, 37, 43);
    public static readonly Color Viewport = Color.FromArgb(20, 20, 24);
    public static readonly Color TextPrimary = Color.FromArgb(233, 233, 236);
    public static readonly Color TextSecondary = Color.FromArgb(150, 150, 160);
    public static readonly Color Accent = Color.FromArgb(138, 124, 245);
    public static readonly Color Selection = Color.FromArgb(56, 52, 84);

    public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
