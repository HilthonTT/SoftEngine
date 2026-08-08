using SoftEngine.Cli.Loading;
using SoftEngine.Cli.Options;
using SoftEngine.Core.Baking;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Scenes.Serialization;
using System.Diagnostics;

namespace SoftEngine.Cli.Rendering;

/// <summary>
/// One render, start to finish: resolve the input, load it, choose a backend, build the scene and
/// the post chain, bake if asked to, write the frames, report.
///
/// <para>
/// The order is the interesting part and it is all visible here — in particular that a scene
/// document is applied <em>after</em> everything derived from the model and the flags, because the
/// point of naming a document is that it carries the settings somebody actually chose.
/// </para>
/// </summary>
internal static class RenderCommand
{
    public static int Execute(RenderOptions options)
    {
        var input = SceneInput.Resolve(options);

        if (input.Error is { } error)
        {
            Console.Error.WriteLine($"softengine: {error}");
            return 1;
        }

        var loadStart = Stopwatch.GetTimestamp();
        var loaded = WorldLoader.Load(input.ModelPath!);
        var loadTime = Stopwatch.GetElapsedTime(loadStart);

        var factor = SuperSampler.ClampFactor(options.SuperSampling);

        var choice = BackendChoice.Create(options);
        var renderer = choice.Backend.Renderer;

        BackendChoice.Configure(renderer, options, loaded.Radius);

        if (options.DebugView is { } requested)
        {
            if (!DebugViewNames.TryParse(requested, out var view))
            {
                Console.Error.WriteLine($"softengine: unknown buffer view '{requested}'");
                return 1;
            }

            renderer.Settings.DebugView = view;
        }

        var framed = SceneBuilder.Build(options, loaded, renderer, factor);
        var post = PostChain.Build(options, loaded);

        renderer.PostProcess = post;

        var painter = PainterCatalog.Create(options.Painter, options.ResolveFiltering());

        // Last, so it wins over everything derived above — which is the point of naming a document:
        // it carries the settings somebody actually chose.
        if (input.Document is { } document)
        {
            SceneSerializer.Apply(document, framed.Scene, renderer.Settings, post);

            if (document.Rendering is { Painter: { Length: > 0 } named })
            {
                painter = PainterCatalog.Create(named, options.ResolveFiltering());
            }
        }

        // After the document, which carries a motion-blur switch that would otherwise overwrite it.
        BackendChoice.ApplyShutter(renderer, options);

        if (choice.Bakes)
        {
            Bake(options, framed);
        }

        var output = options.ResolveOutput();
        var renderTime = FrameSequence.Render(options, framed, renderer, painter, factor, output);

        // The GPU renderer owns a context, a window and a pile of buffers; the CPU one owns
        // nothing and does not implement IDisposable.
        (renderer as IDisposable)?.Dispose();

        RenderReport.Print(
            options, loaded, choice.Backend, renderer.Stats, factor, output, loadTime, renderTime);

        return 0;
    }

    private static void Bake(RenderOptions options, SceneBuilder.Framed framed)
    {
        // Posed first: a bake measures light bouncing off the geometry where it stands, and a rig
        // that has never been updated stands wherever its nodes were constructed. A sequence bakes
        // once, at its first frame — an irradiance volume is a statement about an arrangement of a
        // world, and rebaking it per frame would cost more than the frames do.
        framed.Scene.World.Update(MathF.Max(options.Time, 0f));

        var bakeStart = Stopwatch.GetTimestamp();

        framed.Scene.Irradiance = IrradianceBaker.Bake(framed.Scene, new BakeSettings
        {
            Resolution = options.BakeResolution,
            Rays = options.BakeRays,
            Bounces = options.BakeBounces,
        });

        var bakeTime = Stopwatch.GetElapsedTime(bakeStart);

        if (!options.Stats)
        {
            return;
        }

        var volume = framed.Scene.Irradiance;

        Console.WriteLine(
            $"baked {volume.CountX}×{volume.CountY}×{volume.CountZ} probes " +
            $"({volume.ValidCount} outside geometry) in {bakeTime.TotalMilliseconds:F0} ms");
    }
}
