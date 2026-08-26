using SoftEngine.Cli.Loading;
using SoftEngine.Cli.Options;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Gpu;

namespace SoftEngine.Cli.Rendering;

/// <summary>
/// What the program says once the pixels are written: where they went, and — with
/// <c>--stats</c> — what they cost.
/// </summary>
internal static class RenderReport
{
    public static void Print(
        RenderOptions options,
        LoadedWorld loaded,
        RenderBackends.Result backend,
        RenderStats stats,
        int factor,
        string output,
        TimeSpan loadTime,
        TimeSpan renderTime)
    {
        var frames = System.Math.Max(1, options.Frames);
        var rendered = factor > 1 ? $" (rendered {factor}×)" : string.Empty;

        if (frames > 1)
        {
            Console.WriteLine();
            Console.WriteLine(
                $"{FrameSequence.Numbered(output, 0)} … {FrameSequence.Numbered(output, frames - 1)}  " +
                $"{options.Width}×{options.Height}{rendered}");
            Console.WriteLine(
                $"  {frames} frames at {options.Fps:0.##} fps — " +
                $"{frames / MathF.Max(options.Fps, 1e-3f):0.##} s of animation");
        }
        else
        {
            Console.WriteLine($"{output}  {options.Width}×{options.Height}{rendered}");
        }

        if (loaded.SkippedTextures > 0)
        {
            Console.WriteLine(
                $"  {loaded.SkippedTextures} texture(s) could not be decoded — this renderer reads PNG only, " +
                "so those surfaces are untextured.");
        }

        if (!options.Stats)
        {
            return;
        }

        Console.WriteLine($"  drawn by {backend.Describe()}");
        Console.WriteLine($"  load    {loadTime.TotalMilliseconds:0.#} ms");
        Console.WriteLine($"  render  {renderTime.TotalMilliseconds:0.#} ms");
        Console.WriteLine($"  meshes  {loaded.World.Meshes.Count} ({stats.OccludedMeshCount} hidden behind {stats.OccluderMeshCount} occluder(s))");
        Console.WriteLine($"  tris    {stats.TotalTriangleCount} total, {stats.DrawnTriangleCount} drawn");
        Console.WriteLine($"  pixels  {stats.DrawnPixelCount} drawn");

        // Only when the frame actually stored fragments. The overflow count goes with them
        // because it is the one number that says the resolve was approximate.
        if (stats.TransparentFragmentCount > 0)
        {
            Console.WriteLine(
                $"  glass   {stats.TransparentFragmentCount} fragments over {stats.TransparentPixelCount} pixels" +
                (stats.TransparentOverflowCount > 0
                    ? $", {stats.TransparentOverflowCount} merged past the per-pixel limit"
                    : string.Empty));
        }
    }

    /// <summary>
    /// What <c>--gpu-info</c> prints: the adapter a render would land on, and — on a machine
    /// with more than one — what else there is and how to ask for it.
    ///
    /// <para>
    /// The installed list is printed before the probe rather than after it, because it is the
    /// answer to the question somebody with two adapters is actually asking. The probe below
    /// says which one they are getting; this says which ones there are.
    /// </para>
    /// </summary>
    public static int PrintGpuInfo()
    {
        PrintAdapterChoice();

        if (GpuAvailability.Probe(out var adapter, out var error))
        {
            Console.WriteLine($"  adapter   {adapter!.Renderer}");
            Console.WriteLine($"  vendor    {adapter.Vendor}");
            Console.WriteLine($"  kind      {adapter.Kind}");
            Console.WriteLine($"  opengl    {adapter.Version}");
            Console.WriteLine($"  glsl      {adapter.ShadingLanguage}");

            return 0;
        }

        Console.WriteLine("  no graphics adapter is available for rendering.");
        Console.WriteLine($"  {error}");

        // Not an error: "there is no GPU here" is a true answer to the question that was asked.
        return 0;
    }

    /// <summary>
    /// The adapters this machine has drivers for and the preference in force, on a machine
    /// where that is a choice. Silent on a machine with one adapter, where naming it twice
    /// would only be noise.
    /// </summary>
    private static void PrintAdapterChoice()
    {
        if (!GpuDevices.HasChoice)
        {
            return;
        }

        Console.WriteLine("  installed");

        foreach (var device in GpuDevices.Installed)
        {
            var flag = device.Kind switch
            {
                GpuAdapterKind.Discrete => "--adapter high",
                GpuAdapterKind.Integrated => "--adapter low",
                _ => string.Empty,
            };

            Console.WriteLine($"      {device.Name,-44}{flag}");
        }

        Console.WriteLine($"  preference {GpuPreferences.Describe(GpuPreferences.Current)}");
        Console.WriteLine();
    }
}
