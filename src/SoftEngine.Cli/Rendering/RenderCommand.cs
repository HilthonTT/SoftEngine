using SoftEngine.Cli.Loading;
using SoftEngine.Cli.Options;
using SoftEngine.Core.Baking;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Scenes.Serialization;
using System.Diagnostics;

namespace SoftEngine.Cli.Rendering;

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

        if (input.Document is { } document)
        {
            SceneSerializer.Apply(document, framed.Scene, renderer.Settings, post);

            if (document.Rendering is { Painter: { Length: > 0 } named })
            {
                painter = PainterCatalog.Create(named, options.ResolveFiltering());
            }
        }

        BackendChoice.ApplyShutter(renderer, options);

        if (choice.Bakes)
        {
            Bake(options, framed);
        }

        var output = options.ResolveOutput();
        var renderTime = FrameSequence.Render(options, framed, renderer, painter, factor, output);

        (renderer as IDisposable)?.Dispose();

        RenderReport.Print(
            options, loaded, choice.Backend, renderer.Stats, factor, output, loadTime, renderTime);

        return 0;
    }

    private static void Bake(RenderOptions options, SceneBuilder.Framed framed)
    {
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
