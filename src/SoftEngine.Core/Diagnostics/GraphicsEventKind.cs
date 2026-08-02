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
    OcclusionBufferRender,
    VelocityBufferRender,
    MeshSkipInactive,
    MeshCullBoundingSphere,
    MeshCullOccluded,
    MeshTransformVertices,
    MeshCullTriangles,
    PainterDrawTriangles,
    SkyRender,

    /// <summary>
    /// The order-independent transparency resolve: the pass that blends every pixel's stored
    /// fragments, farthest first. Its arguments are the fragments stored, the pixels they
    /// covered, and how many of them a full pixel had to composite together.
    /// </summary>
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
