using SoftEngine.Core.Gizmos;
using SoftEngine.Core.Pipeline.Debugging;

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

    /// <summary>
    /// Whether the frame rejects whole meshes that are hidden behind other meshes, before any
    /// of their vertices are transformed.
    ///
    /// <para>
    /// The companion to <see cref="HierarchicalZ"/> at the other end of the pipeline. That one
    /// drops a triangle once it has been transformed, projected and binned, and saves only its
    /// pixels; this one drops a mesh before any of that happens, and saves all of it. It costs
    /// a small depth-only pass over the few largest things on screen, so it earns its keep
    /// wherever big geometry stands in front of other geometry and gives most of it back in a
    /// scene of scattered objects with nothing between them.
    /// </para>
    /// </summary>
    public bool OcclusionCulling { get; set; } = true;

    /// <summary>
    /// Whether transparent surfaces are resolved per pixel instead of by sorting the triangles.
    ///
    /// <para>
    /// Off, the frame sorts its transparent triangles farthest-first by their mean depth and
    /// blends each as it is drawn. That is correct exactly when a triangle has one depth to be
    /// sorted by, and two panes of glass that intersect each other do not: whichever is drawn
    /// second is in front along the whole of the seam where they cross. Neither does a small
    /// triangle sorted against a large one it lies partly behind and partly in front of.
    /// </para>
    ///
    /// <para>
    /// On, each transparent fragment is depth-tested and then stored rather than blended, and a
    /// resolve blends every pixel's own list back to front once the pass is over — so the order
    /// is decided per pixel, where it is never ambiguous, and nothing depends on the order the
    /// triangles were drawn in. It costs the storage (see <see cref="FragmentBuffer"/>, which is
    /// also where the per-pixel fragment limit lives) and one pass over the covered pixels.
    /// </para>
    ///
    /// <para>
    /// Off by default, because it changes the picture wherever the sort was getting it wrong —
    /// which is the point, and is also why turning it on is a decision rather than a default.
    /// </para>
    /// </summary>
    public bool OrderIndependentTransparency { get; set; }

    /// <summary>
    /// Whether the frame is jittered by a fraction of a pixel and averaged with the previous ones —
    /// supersampling spread over time instead of over area.
    ///
    /// <para>
    /// It needs the velocity buffer, so turning it on adds a second pass over the frame's geometry
    /// (<see cref="Temporal.VelocityPass"/>). That is still a fraction of what
    /// <see cref="SuperSampler"/> costs for the same number of samples, and unlike supersampling it
    /// only converges while the camera is still — a scene in constant motion gets one sample per
    /// pixel and a slight softness for its trouble.
    /// </para>
    /// </summary>
    public bool TemporalAntiAliasing { get; set; }

    /// <summary>
    /// Whether moving surfaces are smeared along their motion. Also needs the velocity buffer, and
    /// shares the pass with <see cref="TemporalAntiAliasing"/> when both are on.
    /// </summary>
    public bool MotionBlur { get; set; }

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

    /// <summary>
    /// Which of the frame's own buffers to present instead of the shaded image. The pass
    /// runs last, over the finished frame, so everything else in the pipeline is unaffected
    /// by it — and the buffer being shown is the one the frame really used.
    /// </summary>
    public DebugView DebugView { get; set; } = DebugView.Off;

    /// <summary>
    /// Draws a wireframe marker for every light in the world — where it is, which way it faces and
    /// how far it reaches.
    ///
    /// A light is the one thing in a scene with no geometry, so it is the one thing that cannot be
    /// seen; "is the spot pointing where I think it is" otherwise has no answer except moving it and
    /// watching what changes.
    /// </summary>
    public bool ShowLights { get; set; }

    /// <summary>
    /// Indices of the meshes outlined over the finished image. Set by picking: a click has to answer
    /// "which of these is it" visibly, not only in a table.
    /// </summary>
    public List<int> HighlightedMeshes { get; } = [];

    /// <summary>
    /// The first highlighted mesh, or -1 when nothing is. Setting it replaces the whole selection with
    /// that one mesh.
    ///
    /// <para>
    /// Kept as the single-valued face of <see cref="HighlightedMeshes"/> because most callers only
    /// ever mean one — a click, a row in the object table, the mesh a gizmo is attached to — and
    /// because a backend that can only outline one (the GPU's) has something to read that is right
    /// rather than something that is arbitrary.
    /// </para>
    /// </summary>
    public int HighlightedMesh
    {
        get => HighlightedMeshes.Count > 0 ? HighlightedMeshes[0] : -1;

        set
        {
            HighlightedMeshes.Clear();

            if (value >= 0)
            {
                HighlightedMeshes.Add(value);
            }
        }
    }

    /// <summary>
    /// The transform handles drawn over the finished image, or null for none. The renderer
    /// only draws it — the same object is what the front-end hit-tests a click against and
    /// drives a drag through, so what you grab is always what you can see.
    /// </summary>
    public TransformGizmo? Gizmo { get; set; }
}
