using SoftEngine.Core.Buffers;
using SoftEngine.Core.Imaging;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Tests.Golden;

/// <summary>
/// Asserts that a rendered frame still matches the image committed for it.
///
/// <para>
/// This is the class of regression the rest of the suite cannot reach. A unit test states a
/// property — this triangle is back-facing, that matrix round-trips, the near plane splits a
/// straddling triangle into two — and a renderer can satisfy every one of them while producing
/// a picture that is visibly wrong. Nothing in 385 passing tests notices that the specular term
/// came out a tenth dimmer, that a normal map is being sampled with the green channel flipped,
/// or that the tone-map curve shifted: each is a change in a number no test names, and all
/// three are immediately obvious in the frame.
/// </para>
///
/// <para>
/// So the frame itself is the assertion. Baselines live beside this file in
/// <c>References/</c>, are ordinary PNGs a reviewer can open in a diff, and are regenerated
/// deliberately rather than automatically — see <see cref="UpdateVariable"/>.
/// </para>
/// </summary>
internal static class GoldenImage
{
    /// <summary>
    /// Set this environment variable to <c>1</c> to rewrite every baseline the run touches
    /// instead of asserting against it.
    ///
    /// <para>
    /// It is deliberately not a flag on the assertion, and deliberately not automatic on a
    /// mismatch. A golden-image suite is only worth having if updating it is an act someone
    /// performs and a reviewer sees in the diff — a harness that quietly re-records whatever
    /// the renderer just did will agree with every regression it ever meets.
    /// </para>
    /// </summary>
    public const string UpdateVariable = "SOFTENGINE_UPDATE_GOLDEN";

    private static readonly string RootDirectory = LocateRoot();

    /// <summary>Where the committed baselines live.</summary>
    public static string ReferenceDirectory => Path.Combine(RootDirectory, "References");

    private static string ArtifactDirectory => Path.Combine(RootDirectory, "Artifacts");

    private static bool IsUpdating =>
        Environment.GetEnvironmentVariable(UpdateVariable) is "1" or "true" or "TRUE";

    /// <summary>
    /// Compares the render target's finished image against the baseline named
    /// <paramref name="name"/>, and fails the test if they have drifted apart.
    /// </summary>
    public static void Verify(string name, FrameBuffer surface, GoldenTolerance? tolerance = null)
    {
        ArgumentNullException.ThrowIfNull(surface, nameof(surface));

        Verify(name, surface.Screen, surface.Width, surface.Height, tolerance);
    }

    /// <inheritdoc cref="Verify(string, FrameBuffer, GoldenTolerance?)"/>
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
            // Written, so the author can look at what the renderer produced and decide whether
            // it is the picture they meant — and still failed, so that a baseline can never
            // come into existence as a side effect of a test run that reported success.
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

    /// <summary>
    /// Compares two frames rendered in this same process, with no baseline involved.
    ///
    /// <para>
    /// The claim it exists for is an optimization's: that a pass which decides what *not* to
    /// draw has not changed what *is* drawn. That is a stronger statement than matching a
    /// recording, and it can be tested more strictly — both frames came out of one process on
    /// one machine, so the floating-point drift a baseline has to tolerate cannot occur, and
    /// any difference at all is a real one.
    /// </para>
    /// </summary>
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

    /// <summary>
    /// The frame with alpha forced opaque.
    ///
    /// <para>
    /// <see cref="FrameBuffer.Clear"/> zeroes the target, so a pixel no triangle reached keeps
    /// an alpha of zero — correct for a surface that may be composited, and useless in a file
    /// meant to be looked at, where it makes the whole background of an unfilled frame vanish
    /// into whatever the viewer happens to sit on. The comparison ignores alpha for the same
    /// reason, so nothing is being hidden by flattening it here.
    /// </para>
    /// </summary>
    private static int[] Opaque(ReadOnlySpan<int> pixels, int width, int height)
    {
        var opaque = new int[width * height];

        for (var i = 0; i < opaque.Length; i++)
        {
            opaque[i] = pixels[i] | unchecked((int)0xFF000000);
        }

        return opaque;
    }

    /// <summary>
    /// The directory this file was compiled from, which is where the baselines live.
    ///
    /// <para>
    /// A test runs from its output directory, and copying baselines there would make the
    /// files the run compares against copies — so re-recording would update the copy and leave
    /// the committed image untouched, which is the one failure mode a golden-image harness
    /// must not have. The compiler knows the source path; asking it is the whole trick.
    /// </para>
    /// </summary>
    private static string LocateRoot([CallerFilePath] string? path = null) =>
        Path.GetDirectoryName(path) ?? AppContext.BaseDirectory;
}
