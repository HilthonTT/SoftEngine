namespace SoftEngine.WinForms;

/// <summary>
/// How the window was arranged when it closed. Every member is optional, so a settings file
/// from an older layout — whose members no longer exist — loads as "use the defaults".
/// </summary>
internal sealed class WorkspaceLayout
{
    public string? ActiveWorkspace { get; set; }

    public bool? ShowStatsOverlay { get; set; }

    public bool? ShowSidebar { get; set; }

    public bool? ShowToolShelf { get; set; }

    public string? PropertiesTab { get; set; }

    /// <summary>Distance from the window's right edge to the outliner and properties column.</summary>
    public int? SidebarColumnWidth { get; set; }

    public int? OutlinerHeight { get; set; }

    public int? TimelineHeight { get; set; }

    public int? PixelHistoryWidth { get; set; }

    public int? EventListWidth { get; set; }

    public int? ObjectTableHeight { get; set; }
}
