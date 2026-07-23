namespace SoftEngine.Core.Diagnostics;

/// <summary>The pipeline step a <see cref="GraphicsEvent"/> records.</summary>
public enum GraphicsEventKind
{
    FrameBegin,
    RendererSetViewport,
    FrameBufferSetDepthRange,
    FrameBufferClearRenderTarget,
    FrameBufferClearDepthBuffer,
    CameraSetViewMatrix,
    ProjectionSetProjectionMatrix,
    PainterPrepare,
    MeshSkipInactive,
    MeshCullBoundingSphere,
    MeshTransformVertices,
    MeshCullTriangles,
    PainterDrawTriangles,
    WireFrameOverlayDraw,
    GizmoDrawGrid,
    GizmoDrawAxes,
    FramePresent,
}
