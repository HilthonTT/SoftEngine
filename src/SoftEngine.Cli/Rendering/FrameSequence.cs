using SoftEngine.Cli.Options;
using SoftEngine.Core.Imaging;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Rasterization;
using SoftEngine.Core.Scenes;
using System.Diagnostics;

namespace SoftEngine.Cli.Rendering;

internal static class FrameSequence
{
    public static TimeSpan Render(
        RenderOptions options,
        SceneBuilder.Framed framed,
        IRenderer renderer,
        IPainter? painter,
        int factor,
        string output)
    {
        var scene = framed.Scene;
        var frames = System.Math.Max(1, options.Frames);
        var interval = options.Fps > 0f ? 1f / options.Fps : 0f;
        var renderTime = TimeSpan.Zero;

        for (var frame = 0; frame < frames; frame++)
        {
            var progress = frames > 1 ? frame / (float)frames : 0f;

            scene.World.Update(MathF.Max(options.Time, 0f) + frame * interval);

            if (options.Turntable != 0f)
            {
                framed.Camera.Orbit(options.Yaw + options.Turntable * progress, options.Pitch, framed.Distance);
            }

            var renderStart = Stopwatch.GetTimestamp();
            renderer.Render(scene, painter);
            renderTime += Stopwatch.GetElapsedTime(renderStart);

            var path = frames > 1 ? Numbered(output, frame) : output;

            Save(scene, options, factor, path);

            if (frames > 1)
            {
                Console.Write($"\r  frame {frame + 1}/{frames} → {Path.GetFileName(path)}   ");
            }
        }

        return renderTime;
    }

    private static void Save(Scene scene, RenderOptions options, int factor, string path)
    {
        int[] pixels;

        if (factor == 1)
        {
            pixels = scene.Surface.Screen;
        }
        else
        {
            pixels = new int[options.Width * options.Height];
            SuperSampler.Resolve(scene.Surface, pixels, options.Width, options.Height, factor);
        }

        var opaque = new int[options.Width * options.Height];

        for (var i = 0; i < opaque.Length; i++)
        {
            opaque[i] = pixels[i] | unchecked((int)0xFF000000);
        }

        PngCodec.Save(path, opaque, options.Width, options.Height);
    }

    public static string Numbered(string output, int frame)
    {
        var directory = Path.GetDirectoryName(output);
        var name = Path.GetFileNameWithoutExtension(output);
        var extension = Path.GetExtension(output);

        var numbered = $"{name}.{frame:D4}{extension}";

        return string.IsNullOrEmpty(directory) ? numbered : Path.Combine(directory, numbered);
    }
}
