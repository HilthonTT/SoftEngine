using SoftEngine.Core.Gizmos;
using SoftEngine.Core.Pipeline.Debugging;

namespace SoftEngine.Core.Pipeline;

public sealed class RendererSettings
{
    public bool BackFaceCulling { get; set; }

    public bool HierarchicalZ { get; set; } = true;

    public bool NearestMeshesFirst { get; set; } = true;

    public bool OcclusionCulling { get; set; } = true;

    public bool OrderIndependentTransparency { get; set; }

    public bool TemporalAntiAliasing { get; set; }

    public bool MotionBlur { get; set; }

    public bool ShowTriangles { get; set; }

    public bool ShowXZGrid { get; set; }

    public bool ShowAxes { get; set; }

    public bool ShowSkeleton { get; set; }

    public float SkeletonTickSize { get; set; } = 1f;

    public DebugView DebugView { get; set; } = DebugView.Off;

    public bool ShowLights { get; set; }

    public List<int> HighlightedMeshes { get; } = [];

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

    public TransformGizmo? Gizmo { get; set; }
}
