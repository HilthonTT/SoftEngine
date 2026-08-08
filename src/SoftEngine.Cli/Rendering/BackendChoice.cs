using SoftEngine.Cli.Options;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Tracing;
using SoftEngine.Gpu;

namespace SoftEngine.Cli.Rendering;

/// <summary>
/// Picks the backend that can actually do what was asked for, and says so when the two differ.
///
/// <para>
/// Only the software rasterizer reads an irradiance volume — the GPU backend holds its ambient
/// light in six uniforms, which is a cube and not a grid. So "pick a backend for me" must not pick
/// the one that would quietly ignore what was asked for; an explicit <c>--gpu</c> still gets what
/// it asked for, and is told what it costs. Screen-space reflections need the same treatment for
/// the same reason: they read a per-pixel record of what each surface is made of, and only the
/// software rasterizer writes one.
/// </para>
/// </summary>
internal static class BackendChoice
{
    /// <summary>The chosen backend, and whether the bake it was asked for is worth performing.</summary>
    /// <param name="Backend">The backend to draw with.</param>
    /// <param name="Bakes">Whether an irradiance bake will be read by the renderer that got chosen.</param>
    internal sealed record Choice(RenderBackends.Result Backend, bool Bakes);

    public static Choice Create(RenderOptions options)
    {
        var reflects = options.Post.Contains("ssr");

        var requested = (options.Bake || reflects) && options.Backend == RenderBackend.Automatic
            ? RenderBackend.Cpu
            : options.Backend;

        var selection = RenderBackends.Create(requested);
        var renderer = selection.Renderer;

        // Whether the bake is worth doing at all: the two other backends ignore a volume, one
        // because it cannot hold one and one because it is busy computing the thing a volume
        // approximates. Baking anyway would spend minutes on something nothing will read.
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

        // Said before the render rather than after it, so a fallback explains the frame time that
        // is about to follow instead of arriving too late to.
        if (selection.Fallback is { } fallback)
        {
            Console.Error.WriteLine($"softengine: {fallback}");
            Console.Error.WriteLine("softengine: rendering on the CPU instead.");
        }

        return new Choice(selection, bakes);
    }

    /// <summary>
    /// Applies the settings that are the renderer's own rather than the scene's.
    /// </summary>
    public static void Configure(IRenderer renderer, RenderOptions options, float sceneRadius)
    {
        // The event log allocates nothing but does real work per mesh, and none of it can reach a
        // pixel. A batch render should be a recording of the renderer, not of its debugger.
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

    /// <summary>
    /// Turns on motion blur, if <c>--shutter</c> asked for it.
    ///
    /// <para>
    /// Applied after a scene document rather than with the rest of the settings, because a document
    /// carries a <c>MotionBlur</c> flag of its own and applying one overwrites whatever the flags
    /// set. Everything else on <see cref="RendererSettings"/> is deliberately the other way round —
    /// a document wins, since naming one is asking for the setup it recorded — but <c>--shutter</c>
    /// names a number the document has no field for, and honouring the number while discarding the
    /// switch that makes it do anything would render an unblurred frame for a flag that asked for a
    /// blurred one.
    /// </para>
    /// </summary>
    public static void ApplyShutter(IRenderer renderer, RenderOptions options)
    {
        if (options.Shutter <= 0f)
        {
            return;
        }

        // Motion blur needs two frames to have anything to measure, which a sequence has and a
        // single render does not — so it is only offered alongside one, and the flag says so.
        renderer.Settings.MotionBlur = true;

        if (renderer is Renderer cpu)
        {
            cpu.MotionBlur.ShutterFraction = options.Shutter;
        }
    }
}
