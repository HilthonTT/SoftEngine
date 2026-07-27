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
    ShadowMapRender,
    PainterPrepare,
    MeshSkipInactive,
    MeshCullBoundingSphere,
    MeshTransformVertices,
    MeshCullTriangles,
    PainterDrawTriangles,
    SkyRender,
    WireFrameOverlayDraw,
    GizmoDrawGrid,
    GizmoDrawAxes,
    GizmoDrawSkeleton,
    GizmoDrawTransform,
    PostProcessApply,
    DebugViewRender,
    FramePresent,
}
