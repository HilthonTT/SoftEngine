namespace SoftEngine.Core.Diagnostics;

/// <summary>What tried to write a probed pixel.</summary>
public enum PixelWriteSource
{
    Clear,
    Triangle,

    /// <summary>
    /// One stored transparent fragment, blended by the order-independent transparency resolve
    /// rather than when it was shaded. The object and triangle are the ones that shaded it; the
    /// entries for a pixel arrive farthest-first, which is the order they were blended in.
    /// </summary>
    TransparentFragment,

    Sky,
    WireFrame,
    Grid,
    Axes,
    Skeleton,
    TransformGizmo,
    PostProcess,
    DebugView,
}
