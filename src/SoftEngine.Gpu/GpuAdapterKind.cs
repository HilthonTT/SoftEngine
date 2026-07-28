namespace SoftEngine.Gpu;

/// <summary>
/// What kind of device is behind an OpenGL context.
///
/// The distinction that matters here is the last one. "Use the GPU" is a request for
/// hardware, and an OpenGL context is perfectly happy to be served by a CPU implementation —
/// Mesa's llvmpipe, Windows' GDI Generic fallback, SwiftShader — which would run this
/// engine's own software rasterizer's job on the CPU anyway, only through a driver, and
/// slower. Offering that as "GPU rendering" would be a lie the frame time then has to tell.
/// </summary>
public enum GpuAdapterKind
{
    /// <summary>Nothing has been probed yet, or the strings named nothing recognizable.</summary>
    Unknown,

    /// <summary>A separate graphics card with its own memory.</summary>
    Discrete,

    /// <summary>A graphics processor on the CPU package, sharing system memory. Still hardware.</summary>
    Integrated,

    /// <summary>A CPU implementation of OpenGL. Hardware-accelerated in name only.</summary>
    Software,
}
