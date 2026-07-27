using System.Globalization;

namespace SoftEngine.Benchmarks;

internal sealed record Options(
    int Width,
    int Height,
    int Frames,
    int Warmup,
    string? Scene,
    bool Compare,
    string? CsvPath,
    bool ShowHelp)
{
    public static Options Parse(string[] args)
    {
        var width = 1280;
        var height = 720;
        var frames = 60;
        var warmup = 10;
        string? scene = null;
        var compare = false;
        string? csv = null;
        var help = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--width" or "-w":
                    width = NextInt(args, ref i, width);
                    break;

                case "--height" or "-h":
                    height = NextInt(args, ref i, height);
                    break;

                case "--frames" or "-n":
                    frames = System.Math.Max(1, NextInt(args, ref i, frames));
                    break;

                case "--warmup":
                    warmup = System.Math.Max(0, NextInt(args, ref i, warmup));
                    break;

                case "--scene" or "-s":
                    scene = NextText(args, ref i);
                    break;

                case "--csv":
                    csv = NextText(args, ref i);
                    break;

                case "--compare":
                    compare = true;
                    break;

                case "--help" or "-?":
                    help = true;
                    break;
            }
        }

        return new Options(width, height, frames, warmup, scene, compare, csv, help);
    }

    public static void PrintUsage()
    {
        Console.WriteLine("""
            SoftEngine headless benchmark harness.

              --width  <px>    render width  (default 1280)
              --height <px>    render height (default 720)
              --frames <n>     measured frames per scene (default 60)
              --warmup <n>     discarded frames before measuring (default 10)
              --scene  <name>  run only scenes whose name contains this
              --compare        also measure with hierarchical-Z off, and report the ratio
              --csv    <path>  write the results as CSV as well
            """);

        Console.WriteLine();
        Console.WriteLine("Scenes:");

        foreach (var scene in BenchmarkScene.All)
        {
            Console.WriteLine($"  {scene.Name,-16} {scene.Description}");
        }
    }

    private static int NextInt(string[] args, ref int i, int fallback)
    {
        if (i + 1 < args.Length &&
            int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            i++;
            return value;
        }

        return fallback;
    }

    private static string? NextText(string[] args, ref int i)
    {
        if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
        {
            i++;
            return args[i];
        }

        return null;
    }
}
