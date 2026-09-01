namespace SoftEngine.Core.Diagnostics;

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
    OcclusionBufferRender,
    VelocityBufferRender,
    MeshSkipInactive,
    MeshCullBoundingSphere,
    MeshCullOccluded,
    MeshTransformVertices,
    MeshCullTriangles,
    PainterDrawTriangles,
    SkyRender,

    TransparencyResolve,

    WireFrameOverlayDraw,
    GizmoDrawGrid,
    GizmoDrawAxes,
    GizmoDrawSkeleton,
    GizmoDrawTransform,
    PostProcessApply,
    DebugViewRender,
    FramePresent,
}
