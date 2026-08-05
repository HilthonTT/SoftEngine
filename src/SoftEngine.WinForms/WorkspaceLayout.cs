namespace SoftEngine.WinForms;

/// <summary>
/// Where the window was left.
///
/// <para>
/// Stored as the <em>restore</em> bounds rather than whatever <see cref="Form.Bounds"/> happens to
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

/// <summary>
/// Which panels were open, how the space between them was divided, and which sidebar sections
/// were rolled up.
///
/// <para>
/// All of it is nullable so that a file written by an older build — or by a build that did not
/// know about a panel yet — leaves that piece at its default instead of reading a 0 as "the
/// sidebar is zero pixels wide". The distances are in pixels and are clamped against the
/// splitters' own minimums when they are applied, because a layout saved on a large monitor is
/// routinely reopened on a smaller one.
/// </para>
/// </summary>
internal sealed class WorkspaceLayout
{
    public bool? ShowPixelHistory { get; set; }
    public bool? ShowObjectTable { get; set; }
    public bool? ShowEventList { get; set; }
    public bool? ShowStatsOverlay { get; set; }

    public int? SidebarWidth { get; set; }
    public int? SidebarHeight { get; set; }
    public int? ViewportWidth { get; set; }
    public int? ViewportHeight { get; set; }

    public bool? DisplayExpanded { get; set; }
    public bool? ShadingExpanded { get; set; }
    public bool? PostExpanded { get; set; }
}
