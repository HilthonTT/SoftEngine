namespace SoftEngine.WinForms;

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
