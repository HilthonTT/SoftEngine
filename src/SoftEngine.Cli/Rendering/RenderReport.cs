using SoftEngine.Cli.Loading;
using SoftEngine.Cli.Options;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Gpu;

namespace SoftEngine.Cli.Rendering;

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

        if (stats.TransparentFragmentCount > 0)
        {
            Console.WriteLine(
                $"  glass   {stats.TransparentFragmentCount} fragments over {stats.TransparentPixelCount} pixels" +
                (stats.TransparentOverflowCount > 0
                    ? $", {stats.TransparentOverflowCount} merged past the per-pixel limit"
                    : string.Empty));
        }
    }

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

        return 0;
    }

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
