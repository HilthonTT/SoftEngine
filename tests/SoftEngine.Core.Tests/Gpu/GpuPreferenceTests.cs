using SoftEngine.Gpu;

namespace SoftEngine.Core.Tests.Gpu;

/// <summary>
/// Choosing which adapter a GPU render is given, on a machine that has more than one.
///
/// <para>
/// The part that can be tested anywhere is the part that decides what to ask for: the names a
/// command line and a settings file use, what a preference is called on a machine whose
/// adapters are known, and which device each preference points at. Nothing here writes the
/// machine's actual setting or creates a context — the first would change the host running the
/// suite and the second needs a GPU, and neither is what these decisions are.
/// </para>
/// </summary>
public class GpuPreferenceTests
{
    [Theory]
    [InlineData("auto", GpuPreference.Automatic)]
    [InlineData("Automatic", GpuPreference.Automatic)]
    [InlineData("default", GpuPreference.Automatic)]
    [InlineData("", GpuPreference.Automatic)]
    [InlineData(null, GpuPreference.Automatic)]
    [InlineData("high", GpuPreference.HighPerformance)]
    [InlineData("HIGH-PERFORMANCE", GpuPreference.HighPerformance)]
    [InlineData("discrete", GpuPreference.HighPerformance)]
    [InlineData("dedicated", GpuPreference.HighPerformance)]
    [InlineData("low", GpuPreference.PowerSaving)]
    [InlineData("  Integrated  ", GpuPreference.PowerSaving)]
    [InlineData("power-saving", GpuPreference.PowerSaving)]
    public void TryParse_KnownName_Resolves(string? name, GpuPreference expected)
    {
        Assert.True(GpuPreferences.TryParse(name, out var preference));
        Assert.Equal(expected, preference);
    }

    /// <summary>
    /// An unrecognized name fails rather than falling back, so a typed <c>--adapter hihg</c> is
    /// answered with a message instead of a render on whichever adapter the default would have
    /// picked — which is the exact confusion this option exists to end.
    /// </summary>
    [Fact]
    public void TryParse_UnknownName_Fails()
    {
        Assert.False(GpuPreferences.TryParse("fastest", out var preference));
        Assert.Equal(GpuPreference.Automatic, preference);
    }

    /// <summary>Every preference survives being written to a settings file and read back.</summary>
    [Theory]
    [InlineData(GpuPreference.Automatic)]
    [InlineData(GpuPreference.HighPerformance)]
    [InlineData(GpuPreference.PowerSaving)]
    public void Name_RoundTripsThroughTryParse(GpuPreference preference)
    {
        Assert.True(GpuPreferences.TryParse(GpuPreferences.Name(preference), out var parsed));
        Assert.Equal(preference, parsed);
    }

    /// <summary>
    /// Asking for the default is satisfiable everywhere, including on a platform that has no
    /// such setting to write — it is already what that platform does. Only a real choice has
    /// anything to fail at, which is what keeps a headless Linux render from being refused for
    /// declining to express a preference.
    /// </summary>
    [Fact]
    public void TryApply_Automatic_SucceedsOnAnyPlatform()
    {
        if (GpuPreferences.IsSupported)
        {
            // Writing the host's real setting is not this suite's business; on Windows the
            // claim above is about the other platforms.
            return;
        }

        Assert.True(GpuPreferences.TryApply(GpuPreference.Automatic, out var error));
        Assert.Null(error);
    }

    [Fact]
    public void TryApply_AChoice_ExplainsItselfWhereItCannotBeMade()
    {
        if (GpuPreferences.IsSupported)
        {
            return;
        }

        Assert.False(GpuPreferences.TryApply(GpuPreference.HighPerformance, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    /// <summary>
    /// The description falls back to the bare label when the device cannot be named, and never
    /// to a guess. A menu naming the wrong adapter is worse than one naming none, because the
    /// first is believed.
    /// </summary>
    [Fact]
    public void Describe_AlwaysNamesThePreference()
    {
        Assert.StartsWith("Automatic", GpuPreferences.Describe(GpuPreference.Automatic), StringComparison.Ordinal);
        Assert.StartsWith("High performance", GpuPreferences.Describe(GpuPreference.HighPerformance), StringComparison.Ordinal);
        Assert.StartsWith("Power saving", GpuPreferences.Describe(GpuPreference.PowerSaving), StringComparison.Ordinal);
    }

    /// <summary>Automatic is the driver's business, so no device can be promised for it.</summary>
    [Fact]
    public void For_Automatic_NamesNoDevice()
    {
        Assert.Null(GpuDevices.For(GpuPreference.Automatic));
    }

    /// <summary>
    /// The enumerated devices classify by the same rules a live context does. Shared rather than
    /// duplicated because the strings are: a menu calling an adapter integrated while the status
    /// bar called the same part discrete would be a bug nobody could explain.
    /// </summary>
    [Theory]
    [InlineData("NVIDIA GeForce RTX 5060 Laptop GPU", GpuAdapterKind.Discrete)]
    [InlineData("Intel(R) UHD Graphics 770", GpuAdapterKind.Integrated)]
    [InlineData("AMD Radeon RX 7900 XTX", GpuAdapterKind.Discrete)]
    public void KindOf_MatchesWhatAContextWouldConclude(string name, GpuAdapterKind expected)
    {
        Assert.Equal(expected, GpuAdapter.KindOf(string.Empty, name));

        // The same name arriving as GL_RENDERER rather than as a driver description.
        Assert.Equal(expected, new GpuAdapter(string.Empty, name, "4.6.0", "4.60").Kind);
    }

    /// <summary>
    /// Whatever this machine has, the list and the preference agree with each other: a machine
    /// with a choice can name a device for both halves of it, and one without names neither.
    /// Written as an invariant rather than as an expected list, because the suite has to pass on
    /// a build server with one adapter and on a laptop with two.
    /// </summary>
    [Fact]
    public void Installed_AgreesWithWhatEachPreferenceWouldSelect()
    {
        var discrete = GpuDevices.For(GpuPreference.HighPerformance);
        var integrated = GpuDevices.For(GpuPreference.PowerSaving);

        if (GpuDevices.HasChoice)
        {
            Assert.Contains(GpuDevices.Installed, device => device.Kind == GpuAdapterKind.Discrete);
            Assert.Contains(GpuDevices.Installed, device => device.Kind == GpuAdapterKind.Integrated);
        }

        if (discrete is { } d)
        {
            Assert.Equal(GpuAdapterKind.Discrete, d.Kind);
            Assert.Contains(d, GpuDevices.Installed);
        }

        if (integrated is { } i)
        {
            Assert.Equal(GpuAdapterKind.Integrated, i.Kind);
            Assert.Contains(i, GpuDevices.Installed);
        }
    }
}
