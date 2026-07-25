namespace SoftEngine.Core.Pipeline;

public sealed class RendererSettings
{
    public bool BackFaceCulling { get; set; }

    /// <summary>
    /// Whether the fill phase rejects triangles against the farthest depth already stored in
    /// the tile they would be drawn into. It earns its keep when the scene has depth
    /// complexity — geometry hidden behind other geometry — and costs a periodic scan of the
    /// tile's depth where it has none.
    /// </summary>
    public bool HierarchicalZ { get; set; } = true;

    public bool ShowTriangles { get; set; }

    public bool ShowXZGrid { get; set; }

    public bool ShowAxes { get; set; }
}
