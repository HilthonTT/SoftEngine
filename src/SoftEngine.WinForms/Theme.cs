using System.Drawing.Drawing2D;

namespace SoftEngine.WinForms;

/// <summary>
/// The editor's palette, after the look of a desktop 3D suite: neutral greys in a few steps of
/// depth, one blue for "on" and "selected", and an orange kept for the active object alone.
/// Every window and panel reads its colours from here, so the dialogs follow the main window.
/// </summary>
internal static class Theme
{
    // Depth, darkest first: the gaps between editors, the top bar, an editor's body, its header,
    // a panel inside it, then the widgets on the panel.
    public static readonly Color Gap = Color.FromArgb(22, 22, 22);
    public static readonly Color TopBar = Color.FromArgb(35, 35, 35);
    public static readonly Color Background = Color.FromArgb(40, 40, 40);
    public static readonly Color Surface = Color.FromArgb(48, 48, 48);
    public static readonly Color Header = Color.FromArgb(56, 56, 56);
    public static readonly Color Panel = Color.FromArgb(61, 61, 61);
    public static readonly Color PanelHeader = Color.FromArgb(66, 66, 66);
    public static readonly Color Widget = Color.FromArgb(84, 84, 84);
    public static readonly Color WidgetHover = Color.FromArgb(101, 101, 101);
    public static readonly Color WidgetSunken = Color.FromArgb(40, 40, 40);
    public static readonly Color Outline = Color.FromArgb(28, 28, 28);

    public static readonly Color Viewport = Color.FromArgb(57, 57, 57);

    public static readonly Color TextPrimary = Color.FromArgb(230, 230, 230);
    public static readonly Color TextSecondary = Color.FromArgb(160, 160, 160);
    public static readonly Color TextDisabled = Color.FromArgb(110, 110, 110);

    public static readonly Color Accent = Color.FromArgb(71, 114, 179);
    public static readonly Color AccentHover = Color.FromArgb(86, 132, 201);
    public static readonly Color Selection = Color.FromArgb(51, 77, 128);

    public static readonly Color ActiveObject = Color.FromArgb(255, 160, 40);

    public static readonly Color AxisX = Color.FromArgb(237, 82, 104);
    public static readonly Color AxisY = Color.FromArgb(131, 200, 21);
    public static readonly Color AxisZ = Color.FromArgb(72, 140, 238);

    public static readonly Font UiFont = new("Segoe UI", 9f);
    public static readonly Font UiFontBold = new("Segoe UI Semibold", 9f);
    public static readonly Font SmallFont = new("Segoe UI", 8f);

    // The logo is the executable's own icon (ApplicationIcon in the .csproj), so there is
    // one copy of it and every window shows the same thing the taskbar and Explorer do.
    public static readonly Icon? AppIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

    public static GraphicsPath RoundedRect(Rectangle bounds, int radius) =>
        RoundedRect((RectangleF)bounds, radius);

    public static GraphicsPath RoundedRect(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var d = MathF.Min(radius * 2f, MathF.Min(bounds.Width, bounds.Height));

        if (d <= 0f)
        {
            path.AddRectangle(bounds);
            return path;
        }

        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void FillRounded(Graphics g, Color color, RectangleF bounds, float radius)
    {
        using var path = RoundedRect(bounds, radius);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    public static void DrawRounded(Graphics g, Color color, RectangleF bounds, float radius)
    {
        using var path = RoundedRect(bounds, radius);
        using var pen = new Pen(color);
        g.DrawPath(pen, path);
    }

    public static Color Lerp(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t),
        (int)(a.G + (b.G - a.G) * t),
        (int)(a.B + (b.B - a.B) * t));
}
