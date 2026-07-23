using System.Globalization;

namespace SoftEngine.Core.Diagnostics;

/// <summary>
/// One step of a frame's render pipeline, as shown in the graphics event list.
///
/// The payload is three floats rather than a formatted string: recording an event
/// must stay allocation-free (a busy scene emits thousands per frame), so the text
/// is built by <see cref="Describe"/> only for the rows the UI actually draws.
/// </summary>
public readonly record struct GraphicsEvent(GraphicsEventKind Kind, int ObjectId, float A, float B, float C)
{
    private static readonly string[] _names = Enum.GetNames<GraphicsEventKind>();

    public string Name => _names[(int)Kind];

    /// <summary>The scene object this event acts on, or an empty string when it acts on none.</summary>
    public string ObjectName => ObjectId < 0 ? string.Empty : $"obj:{ObjectId}";

    /// <summary>Human-readable parameters for this event. Allocates — call it per displayed row only.</summary>
    public string Describe()
    {
        var c = CultureInfo.InvariantCulture;

        return Kind switch
        {
            GraphicsEventKind.FrameBegin =>
                string.Create(c, $"frame #{(long)A}"),
            GraphicsEventKind.RendererSetViewport =>
                string.Create(c, $"0, 0, {(int)A} × {(int)B}"),
            GraphicsEventKind.FrameBufferSetDepthRange =>
                string.Create(c, $"near {A:0.###}, far {B:0.###}"),
            GraphicsEventKind.FrameBufferClearRenderTarget =>
                string.Create(c, $"obj:{ObjectId} — {(int)A} × {(int)B} → #000000"),
            GraphicsEventKind.FrameBufferClearDepthBuffer =>
                string.Create(c, $"obj:{ObjectId} — {(int)A} × {(int)B} → 1.0"),
            GraphicsEventKind.CameraSetViewMatrix =>
                string.Create(c, $"obj:{ObjectId} — eye ({A:0.##}, {B:0.##}, {C:0.##})"),
            GraphicsEventKind.ProjectionSetProjectionMatrix =>
                string.Create(c, $"obj:{ObjectId} — near {A:0.###}, far {B:0.###}, aspect {C:0.###}"),
            GraphicsEventKind.PainterPrepare =>
                string.Create(c, $"obj:{ObjectId}"),
            GraphicsEventKind.MeshSkipInactive =>
                string.Create(c, $"obj:{ObjectId} — {(int)A} triangles skipped"),
            GraphicsEventKind.MeshCullBoundingSphere =>
                string.Create(c, $"obj:{ObjectId} — outside frustum, {(int)A} triangles rejected"),
            GraphicsEventKind.MeshTransformVertices =>
                string.Create(c, $"obj:{ObjectId} — {(int)A} vertices → view space"),
            GraphicsEventKind.MeshCullTriangles =>
                string.Create(c, $"obj:{ObjectId} — {(int)A} visible, {(int)B} back-facing, {(int)C} clipped"),
            GraphicsEventKind.PainterDrawTriangles =>
                string.Create(c, $"obj:{ObjectId} — {(int)A} triangles"),
            GraphicsEventKind.WireFrameOverlayDraw =>
                string.Create(c, $"{(int)A} triangles"),
            GraphicsEventKind.GizmoDrawGrid =>
                string.Create(c, $"{(int)A} lines, {B:0.#} … {C:0.#}"),
            GraphicsEventKind.GizmoDrawAxes =>
                "X, Y, Z",
            GraphicsEventKind.FramePresent =>
                string.Create(c, $"{(int)A} pixels drawn, {(int)B} z-rejected"),
            _ => string.Empty,
        };
    }
}
