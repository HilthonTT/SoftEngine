namespace SoftEngine.Gpu;

/// <summary>Which rasterizer a frame is drawn by.</summary>
public enum RenderBackend
{
    /// <summary>
    /// Use a graphics adapter when there is one and the software rasterizer when there is
    /// not. The default everywhere, because it is the only setting that is right on every
    /// machine.
    /// </summary>
    Automatic,

    /// <summary>This engine's own software rasterizer, on the CPU. Always available.</summary>
    Cpu,

    /// <summary>
    /// A graphics adapter, through OpenGL. Falls back to the CPU with an explanation when
    /// there is no adapter — including when OpenGL is served by a CPU implementation, which
    /// is not what anyone selecting this is asking for.
    /// </summary>
    Gpu,

    /// <summary>
    /// Not a rasterizer at all: the CPU path tracer, which answers the same scene by following
    /// light through it. Orders of magnitude slower and the only one of the three that computes
    /// interreflection, so it is what the other two are checked against rather than an
    /// alternative to them.
    /// </summary>
    Trace,
}
