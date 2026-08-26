using SoftEngine.Cli.Options;
using SoftEngine.Cli.Rendering;
using SoftEngine.Gpu;

// The entry point does the four things that decide whether a render happens at all — read the
// arguments, answer --help and --gpu-info, report anything unreadable, and turn the exceptions a
// user can actually cause into a message. The render itself is RenderCommand.

var options = RenderOptionsParser.Parse(args);

if (options.ShowHelp || args.Length == 0)
{
    UsageText.Print();
    return options.ShowHelp ? 0 : 1;
}

// Before --gpu-info, and before any render: the adapter preference decides which driver's
// OpenGL is loaded, and that is settled by the first context this process creates. Applied only
// when --adapter actually said something — see RenderOptions.GpuPreference.
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
    // The failures a user can actually cause — a missing file, a format nothing here reads, a
    // directory that cannot be written. Anything else is a bug and deserves its stack trace.
    Console.Error.WriteLine($"softengine: {exception.Message}");
    return 1;
}
