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

    /// <summary>
    /// Draws the world's node hierarchy as bones over the finished image. A rig is invisible
    /// in a rendered frame by construction, so this is the only way to see what a pose is
    /// actually doing to it.
    /// </summary>
    public bool ShowSkeleton { get; set; }

    /// <summary>
    /// Length of each joint's axis tick, in world units. Models are authored at scales two
    /// orders of magnitude apart, so the front-end sizes this to whatever it has loaded.
    /// </summary>
    public float SkeletonTickSize { get; set; } = 1f;
}
