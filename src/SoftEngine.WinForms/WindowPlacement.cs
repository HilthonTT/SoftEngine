namespace SoftEngine.WinForms;

/// <summary>
/// Where the window was left.
///
/// <para>
/// Stored as the <em>restore</em> bounds rather than whatever <see cref="Control.Bounds"/> happens to
/// read: a maximized window's bounds are the screen's, so saving those and reopening un-maximized
/// would fill the display with a window that thinks it is a normal one and has nowhere smaller to
/// go back to. Minimized is not recorded at all — nobody wants an application that opens into the
/// task bar because that is where they left it.
/// </para>
/// </summary>
internal sealed class WindowPlacement
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Maximized { get; set; }
}
