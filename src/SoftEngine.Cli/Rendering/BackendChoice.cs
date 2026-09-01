using SoftEngine.Cli.Options;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Tracing;
using SoftEngine.Gpu;

namespace SoftEngine.Cli.Rendering;

internal static class BackendChoice
{
    internal sealed record Choice(RenderBackends.Result Backend, bool Bakes);

    public static Choice Create(RenderOptions options)
    {
        var reflects = options.Post.Contains("ssr");

        var requested = (options.Bake || reflects) && options.Backend == RenderBackend.Automatic
            ? RenderBackend.Cpu
            : options.Backend;

        var selection = RenderBackends.Create(requested);
        var renderer = selection.Renderer;

        var bakes = options.Bake && renderer is Renderer;

        if (options.Bake && !bakes)
        {
            Console.Error.WriteLine(renderer is PathTracer
                ? "softengine: the path tracer computes indirect light as it goes — nothing to bake."
                : "softengine: this backend holds its ambient light as six values and cannot read a " +
                  "volume; the frame will be lit by the environment instead.");
        }

        if (reflects && renderer is not Renderer)
        {
            Console.Error.WriteLine(renderer is PathTracer
                ? "softengine: the path tracer reflects the scene as it goes — --post ssr adds nothing."
                : "softengine: this backend records nothing about its surfaces, so --post ssr has " +
                  "nothing to reflect with; the frame keeps its environment reflections only.");
        }

        if (selection.Fallback is { } fallback)
        {
            Console.Error.WriteLine($"softengine: {fallback}");
            Console.Error.WriteLine("softengine: rendering on the CPU instead.");
        }

        return new Choice(selection, bakes);
    }

    public static void Configure(IRenderer renderer, RenderOptions options, float sceneRadius)
    {
        renderer.Diagnostics.CaptureEvents = false;

        if (renderer is PathTracer tracer)
        {
            tracer.Trace.SamplesPerPixel = options.Samples;
            tracer.Trace.MaxBounces = options.Bounces;
            tracer.Trace.DirectLightScale = options.PhysicalExposure ? 1f : MathF.PI;
        }

        renderer.Settings.BackFaceCulling = options.BackFaceCulling;
        renderer.Settings.OrderIndependentTransparency = options.OrderIndependentTransparency;
        renderer.Settings.ShowTriangles = options.Wireframe;
        renderer.Settings.ShowXZGrid = options.Grid;
        renderer.Settings.ShowAxes = options.Axes;
        renderer.Settings.SkeletonTickSize = sceneRadius * 0.05f;
    }

    public static void ApplyShutter(IRenderer renderer, RenderOptions options)
    {
        if (options.Shutter <= 0f)
        {
            return;
        }

        renderer.Settings.MotionBlur = true;

        if (renderer is Renderer cpu)
        {
            cpu.MotionBlur.ShutterFraction = options.Shutter;
        }
    }
}
