using SoftEngine.Core.Pipeline;

namespace SoftEngine.Gpu;

/// <summary>
/// Builds the renderer a backend choice names, and says what actually happened.
///
/// <para>
/// The falling back is the point. "GPU" is a request, not a fact: there may be no driver, no
/// display, a driver too old for the shaders, or an OpenGL that turns out to be a CPU
/// rasterizer wearing a driver's name. Every one of those has the same right answer — render
/// on the CPU and say so — and having one place that decides it keeps the viewer, the
/// command line and the tests from each inventing their own.
/// </para>
/// </summary>
public static class RenderBackends
{
    /// <summary>
    /// What a <see cref="Create"/> produced: the renderer, the backend it really is, the
    /// device when there is one, and the reason it is not what was asked for when it isn't.
    /// </summary>
    /// <param name="Renderer">The renderer to draw with. Never null.</param>
    /// <param name="Backend">
    /// <see cref="RenderBackend.Cpu"/> or <see cref="RenderBackend.Gpu"/> — never
    /// <see cref="RenderBackend.Automatic"/>, which is a request rather than an outcome.
    /// </param>
    /// <param name="Adapter">The device a GPU render is running on, or null on the CPU.</param>
    /// <param name="Fallback">
    /// Why the GPU was not used, when it was asked for and could not be. Null otherwise —
    /// including when the CPU was chosen deliberately, which is not a fallback.
    /// </param>
    public readonly record struct Result(
        IRenderer Renderer,
        RenderBackend Backend,
        GpuAdapter? Adapter,
        string? Fallback)
    {
        /// <summary>One line naming what a frame will be drawn by, for a status bar or a log.</summary>
        public string Describe() => Backend == RenderBackend.Gpu && Adapter is { } adapter
            ? $"GPU — {adapter.Describe()}"
            : "CPU — software rasterizer";
    }

    /// <summary>
    /// Creates the renderer for a backend choice. Must be called from the thread that will
    /// render, and that thread must be able to create a window: an OpenGL context belongs to
    /// one thread and needs a surface to be created against.
    /// </summary>
    public static Result Create(RenderBackend backend)
    {
        if (backend == RenderBackend.Cpu)
        {
            return new Result(new Renderer(), RenderBackend.Cpu, null, null);
        }

        if (GpuRenderer.TryCreate(out var gpu, out var error))
        {
            return new Result(gpu!, RenderBackend.Gpu, gpu!.Adapter, null);
        }

        // Automatic asked for whatever works, so ending up on the CPU is the answer rather
        // than a disappointment, and it says nothing. An explicit GPU request that landed
        // here has something to explain.
        return new Result(
            new Renderer(),
            RenderBackend.Cpu,
            null,
            backend == RenderBackend.Gpu ? error : null);
    }

    /// <summary>Parses the name a command line or a settings file uses.</summary>
    public static bool TryParse(string? name, out RenderBackend backend)
    {
        switch (name?.Trim().ToLowerInvariant())
        {
            case "auto" or "automatic" or null or "":
                backend = RenderBackend.Automatic;
                return true;

            case "cpu" or "software":
                backend = RenderBackend.Cpu;
                return true;

            case "gpu" or "hardware" or "opengl":
                backend = RenderBackend.Gpu;
                return true;

            default:
                backend = RenderBackend.Automatic;
                return false;
        }
    }
}
