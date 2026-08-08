using SoftEngine.Cli.Options;
using SoftEngine.Core.Imaging;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Rasterization;
using SoftEngine.Core.Scenes;
using System.Diagnostics;

namespace SoftEngine.Cli.Rendering;

/// <summary>
/// Renders one frame or a numbered sequence of them, and writes each as a PNG.
/// </summary>
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
            // Where this frame sits in the sequence, in [0, 1). Open at the top on purpose: a turntable
            // whose last frame repeats its first stutters when it loops.
            var progress = frames > 1 ? frame / (float)frames : 0f;

            // Animations are advanced before the frame rather than during it: rendering must not move
            // time. This runs even at t = 0, because the hierarchy still has to be posed once — a rig
            // that has never been updated renders at whatever its nodes happened to be constructed
            // with. On a static model it walks two empty lists.
            scene.World.Update(MathF.Max(options.Time, 0f) + frame * interval);

            if (options.Turntable != 0f)
            {
                // The camera walks the arc rather than the model turning: a scene has lights and a sky
                // in it, and spinning the geometry inside them looks like the lighting is spinning too.
                framed.Camera.Orbit(options.Yaw + options.Turntable * progress, options.Pitch, framed.Distance);
            }

            var renderStart = Stopwatch.GetTimestamp();
            renderer.Render(scene, painter);
            renderTime += Stopwatch.GetElapsedTime(renderStart);

            var path = frames > 1 ? Numbered(output, frame) : output;

            Save(scene, options, factor, path);

            if (frames > 1)
            {
                // One line per frame, overwritten: a hundred-frame render should not scroll the reason
                // it was slow off the top of the terminal.
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

        // Cleared background pixels are 0x00000000, which would save as transparent — honest for a
        // compositing workflow and surprising for everyone else, who asked for a picture.
        var opaque = new int[options.Width * options.Height];

        for (var i = 0; i < opaque.Length; i++)
        {
            opaque[i] = pixels[i] | unchecked((int)0xFF000000);
        }

        PngCodec.Save(path, opaque, options.Width, options.Height);
    }

    /// <summary>
    /// One frame's path: the output name with a four-digit index before its extension.
    ///
    /// Zero-padded and fixed-width because every tool that reads a sequence — ffmpeg, an image
    /// viewer's "open as animation", a shell glob — sorts the names as text, and frame.10.png sorts
    /// before frame.2.png.
    /// </summary>
    public static string Numbered(string output, int frame)
    {
        var directory = Path.GetDirectoryName(output);
        var name = Path.GetFileNameWithoutExtension(output);
        var extension = Path.GetExtension(output);

        var numbered = $"{name}.{frame:D4}{extension}";

        return string.IsNullOrEmpty(directory) ? numbered : Path.Combine(directory, numbered);
    }
}
