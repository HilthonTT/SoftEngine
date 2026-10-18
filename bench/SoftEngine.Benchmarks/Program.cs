using SoftEngine.Benchmarks;
using System.Diagnostics;
using System.Globalization;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

var options = Options.Parse(args);

if (options.ShowHelp)
{
    Options.PrintUsage();
    return 0;
}

var scenes = BenchmarkScene.All
    .Where(scene => options.Scene is null || scene.Name.Contains(options.Scene, StringComparison.OrdinalIgnoreCase))
    .ToList();

if (scenes.Count == 0)
{
    Console.Error.WriteLine(
        $"No scene matches '{options.Scene}'. Known scenes: {string.Join(", ", BenchmarkScene.All.Select(s => s.Name))}");

    return 1;
}

Console.WriteLine(
    $"SoftEngine benchmarks — {options.Width}×{options.Height}, {Environment.ProcessorCount} hardware threads, " +
    $"{options.Frames} frames after {options.Warmup} warm-up");

if (!IsOptimized())
{
    Console.WriteLine();
    Console.WriteLine("  ! This build is not optimized — rebuild with -c Release, or the numbers measure the debugger.");
}

Console.WriteLine();

Console.WriteLine(options.Compare != ComparedFeature.None
    ? $"{"scene",-16}{"median",10}{"min",10}{"p95",10}{options.ComparisonLabel,10}{"speedup",9}{"occluders",11}{"hidden",9}"
    : $"{"scene",-16}{"median",10}{"min",10}{"p95",10}{"triangles",12}{"drawn",12}{"occluders",11}{"hidden",9}");

var rows = new List<(BenchmarkResult Result, BenchmarkResult? Baseline)>();

foreach (var scene in scenes)
{
    var result = BenchmarkRunner.Run(scene, options.Width, options.Height, options.Frames, options.Warmup);

    BenchmarkResult? baseline = options.Compare switch
    {
        ComparedFeature.HierarchicalZ =>
            BenchmarkRunner.Run(scene, options.Width, options.Height, options.Frames, options.Warmup, hierarchicalZ: false),
        ComparedFeature.Occlusion =>
            BenchmarkRunner.Run(scene, options.Width, options.Height, options.Frames, options.Warmup, occlusionCulling: false),
        ComparedFeature.VectorizedSpans =>
            BenchmarkRunner.Run(scene, options.Width, options.Height, options.Frames, options.Warmup, vectorizedSpans: false),
        ComparedFeature.NearestMeshesFirst =>
            BenchmarkRunner.Run(scene, options.Width, options.Height, options.Frames, options.Warmup, nearestMeshesFirst: false),
        ComparedFeature.ParallelCullPhase =>
            BenchmarkRunner.Run(scene, options.Width, options.Height, options.Frames, options.Warmup, parallelCullPhase: false),
        ComparedFeature.HalfSpaceFill =>
            BenchmarkRunner.Run(scene, options.Width, options.Height, options.Frames, options.Warmup, halfSpaceFill: true),
        _ => null,
    };

    rows.Add((result, baseline));

    if (baseline is { } without)
    {
        var speedup = (without.MedianMs / result.MedianMs).ToString("0.00", CultureInfo.InvariantCulture) + "×";

        Console.WriteLine(
            $"{result.Scene,-16}{Ms(result.MedianMs),10}{Ms(result.MinMs),10}{Ms(result.P95Ms),10}" +
            $"{Ms(without.MedianMs),10}{speedup,9}{result.Occluders,11:N0}{result.HiddenMeshes,9:N0}");
    }
    else
    {
        Console.WriteLine(
            $"{result.Scene,-16}{Ms(result.MedianMs),10}{Ms(result.MinMs),10}{Ms(result.P95Ms),10}" +
            $"{result.Triangles,12:N0}{result.DrawnTriangles,12:N0}{result.Occluders,11:N0}{result.HiddenMeshes,9:N0}");
    }
}

Console.WriteLine();

foreach (var scene in scenes)
{
    Console.WriteLine($"  {scene.Name,-16}{scene.Description}");
}

if (options.CsvPath is { } csvPath)
{
    WriteCsv(csvPath, rows);

    Console.WriteLine();
    Console.WriteLine($"Wrote {csvPath}");
}

return 0;

static string Ms(double value) => value.ToString("0.00", CultureInfo.InvariantCulture) + "ms";

static bool IsOptimized() =>
    typeof(SoftEngine.Core.Pipeline.Renderer).Assembly
        .GetCustomAttributes(typeof(DebuggableAttribute), false)
        .OfType<DebuggableAttribute>()
        .FirstOrDefault() is not { IsJITOptimizerDisabled: true };

static void WriteCsv(string path, List<(BenchmarkResult Result, BenchmarkResult? Baseline)> rows)
{
    using var writer = new StreamWriter(path);
    writer.WriteLine("scene,median_ms,min_ms,p95_ms,median_baseline_ms,triangles,drawn_triangles,pixels");

    foreach (var (result, baseline) in rows)
    {
        writer.WriteLine(string.Join(
            ',',
            result.Scene,
            result.MedianMs.ToString("0.000", CultureInfo.InvariantCulture),
            result.MinMs.ToString("0.000", CultureInfo.InvariantCulture),
            result.P95Ms.ToString("0.000", CultureInfo.InvariantCulture),
            baseline?.MedianMs.ToString("0.000", CultureInfo.InvariantCulture) ?? string.Empty,
            result.Triangles,
            result.DrawnTriangles,
            result.Pixels));
    }
}
