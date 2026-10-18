using SoftEngine.Core.Buffers;
using SoftEngine.Core.Imaging;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Tests.Golden;

internal static class GoldenImage
{
    public const string UpdateVariable = "SOFTENGINE_UPDATE_GOLDEN";

    private static readonly string RootDirectory = LocateRoot();

    public static string ReferenceDirectory => Path.Combine(RootDirectory, "References");

    private static string ArtifactDirectory => Path.Combine(RootDirectory, "Artifacts");

    private static bool IsUpdating =>
        Environment.GetEnvironmentVariable(UpdateVariable) is "1" or "true" or "TRUE";

    public static void Verify(string name, FrameBuffer surface, GoldenTolerance? tolerance = null)
    {
        ArgumentNullException.ThrowIfNull(surface, nameof(surface));

        Verify(name, surface.Screen, surface.Width, surface.Height, tolerance);
    }

    public static void Verify(string name, ReadOnlySpan<int> pixels, int width, int height, GoldenTolerance? tolerance = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(name));

        var gate = tolerance ?? GoldenTolerance.Default;
        var actual = Opaque(pixels, width, height);
        var reference = Path.Combine(ReferenceDirectory, name + ".png");

        if (IsUpdating)
        {
            PngCodec.Save(reference, actual, width, height);
            return;
        }

        if (!File.Exists(reference))
        {
            PngCodec.Save(reference, actual, width, height);

            Assert.Fail(
                $"No baseline for '{name}'. One has been written from this run to{Environment.NewLine}" +
                $"  {reference}{Environment.NewLine}" +
                $"Open it, check it is the image you intended, and commit it.");
        }

        var (expected, expectedWidth, expectedHeight) = PngCodec.Load(reference);

        if (expectedWidth != width || expectedHeight != height)
        {
            Assert.Fail(
                $"Baseline '{name}' is {expectedWidth}x{expectedHeight}, but the frame is {width}x{height}. " +
                $"Re-record it with {UpdateVariable}=1.");
        }

        var comparison = ImageDiff.Compare(expected, actual, width, height, gate);

        if (comparison.IsWithin(gate))
        {
            return;
        }

        var actualPath = Path.Combine(ArtifactDirectory, name + ".actual.png");
        var diffPath = Path.Combine(ArtifactDirectory, name + ".diff.png");

        PngCodec.Save(actualPath, actual, width, height);
        PngCodec.Save(diffPath, ImageDiff.Render(expected, actual, width, height, gate), width, height);

        Assert.Fail(
            $"Rendered frame '{name}' no longer matches its baseline.{Environment.NewLine}" +
            $"{comparison.Describe(gate)}{Environment.NewLine}" +
            $"  expected             {reference}{Environment.NewLine}" +
            $"  actual               {actualPath}{Environment.NewLine}" +
            $"  difference           {diffPath}{Environment.NewLine}" +
            $"If the change is intended, re-record with {UpdateVariable}=1 and commit the new baseline.");
    }

    public static void VerifyIdentical(string what, ReadOnlySpan<int> expected, ReadOnlySpan<int> actual, int width, int height)
    {
        var comparison = ImageDiff.Compare(expected, actual, width, height, GoldenTolerance.Exact);

        if (comparison.DifferingPixels == 0)
        {
            return;
        }

        Directory.CreateDirectory(ArtifactDirectory);

        var diffPath = Path.Combine(ArtifactDirectory, what + ".diff.png");
        PngCodec.Save(diffPath, ImageDiff.Render(expected, actual, width, height, GoldenTolerance.Exact), width, height);

        Assert.Fail(
            $"{what} changed the rendered image.{Environment.NewLine}" +
            $"{comparison.Describe(GoldenTolerance.Exact)}{Environment.NewLine}" +
            $"  difference           {diffPath}");
    }

    private static int[] Opaque(ReadOnlySpan<int> pixels, int width, int height)
    {
        var opaque = new int[width * height];

        for (var i = 0; i < opaque.Length; i++)
        {
            opaque[i] = pixels[i] | unchecked((int)0xFF000000);
        }

        return opaque;
    }

    private static string LocateRoot([CallerFilePath] string? path = null) =>
        Path.GetDirectoryName(path) ?? AppContext.BaseDirectory;
}
