using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Tracing;

namespace SoftEngine.Gpu;

public static class RenderBackends
{
    public readonly record struct Result(
        IRenderer Renderer,
        RenderBackend Backend,
        GpuAdapter? Adapter,
        string? Fallback)
    {
        public string Describe() => Backend switch
        {
            RenderBackend.Gpu when Adapter is { } adapter => $"GPU — {adapter.Describe()}",
            RenderBackend.Trace => "CPU — path tracer",
            _ => "CPU — software rasterizer",
        };
    }

    public static Result Create(RenderBackend backend)
    {
        if (backend == RenderBackend.Cpu)
        {
            return new Result(new Renderer(), RenderBackend.Cpu, null, null);
        }

        if (backend == RenderBackend.Trace)
        {
            return new Result(new PathTracer(), RenderBackend.Trace, null, null);
        }

        if (GpuRenderer.TryCreate(out var gpu, out var error))
        {
            return new Result(gpu!, RenderBackend.Gpu, gpu!.Adapter, null);
        }

        return new Result(
            new Renderer(),
            RenderBackend.Cpu,
            null,
            backend == RenderBackend.Gpu ? error : null);
    }

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

            case "trace" or "tracer" or "pathtrace" or "path-trace" or "reference":
                backend = RenderBackend.Trace;
                return true;

            default:
                backend = RenderBackend.Automatic;
                return false;
        }
    }
}
