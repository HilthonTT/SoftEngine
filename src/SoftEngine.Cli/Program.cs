using SoftEngine.Cli.Options;
using SoftEngine.Cli.Rendering;
using SoftEngine.Gpu;

var options = RenderOptionsParser.Parse(args);

if (options.ShowHelp || args.Length == 0)
{
    UsageText.Print();
    return options.ShowHelp ? 0 : 1;
}

if (options.GpuPreference is { } preference && !GpuPreferences.TryApply(preference, out var preferenceError))
{
    Console.Error.WriteLine($"softengine: {preferenceError}");
    return 1;
}

if (options.ShowGpuInfo)
{
    return RenderReport.PrintGpuInfo();
}

if (options.Errors.Count > 0)
{
    foreach (var error in options.Errors)
    {
        Console.Error.WriteLine($"softengine: {error}");
    }

    Console.Error.WriteLine("Try --help.");
    return 1;
}

try
{
    return RenderCommand.Execute(options);
}
catch (Exception exception) when (
    exception is IOException or NotSupportedException or InvalidDataException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"softengine: {exception.Message}");
    return 1;
}
