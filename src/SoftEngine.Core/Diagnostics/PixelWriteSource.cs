namespace SoftEngine.Core.Diagnostics;

/// <summary>What tried to write a probed pixel.</summary>
public enum PixelWriteSource
{
    Clear,
    Triangle,
    Sky,
    WireFrame,
    Grid,
    Axes,
    Skeleton,
    PostProcess,
}
