using SoftEngine.Gpu;

namespace SoftEngine.Core.Tests.Gpu;

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

    [Fact]
    public void TryParse_UnknownName_Fails()
    {
        Assert.False(GpuPreferences.TryParse("fastest", out var preference));
        Assert.Equal(GpuPreference.Automatic, preference);
    }

    [Theory]
    [InlineData(GpuPreference.Automatic)]
    [InlineData(GpuPreference.HighPerformance)]
    [InlineData(GpuPreference.PowerSaving)]
    public void Name_RoundTripsThroughTryParse(GpuPreference preference)
    {
        Assert.True(GpuPreferences.TryParse(GpuPreferences.Name(preference), out var parsed));
        Assert.Equal(preference, parsed);
    }

    [Fact]
    public void TryApply_Automatic_SucceedsOnAnyPlatform()
    {
        if (GpuPreferences.IsSupported)
        {
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

    [Fact]
    public void Describe_AlwaysNamesThePreference()
    {
        Assert.StartsWith("Automatic", GpuPreferences.Describe(GpuPreference.Automatic), StringComparison.Ordinal);
        Assert.StartsWith("High performance", GpuPreferences.Describe(GpuPreference.HighPerformance), StringComparison.Ordinal);
        Assert.StartsWith("Power saving", GpuPreferences.Describe(GpuPreference.PowerSaving), StringComparison.Ordinal);
    }

    [Fact]
    public void For_Automatic_NamesNoDevice()
    {
        Assert.Null(GpuDevices.For(GpuPreference.Automatic));
    }

    [Theory]
    [InlineData("NVIDIA GeForce RTX 5060 Laptop GPU", GpuAdapterKind.Discrete)]
    [InlineData("Intel(R) UHD Graphics 770", GpuAdapterKind.Integrated)]
    [InlineData("AMD Radeon RX 7900 XTX", GpuAdapterKind.Discrete)]
    public void KindOf_MatchesWhatAContextWouldConclude(string name, GpuAdapterKind expected)
    {
        Assert.Equal(expected, GpuAdapter.KindOf(string.Empty, name));

        Assert.Equal(expected, new GpuAdapter(string.Empty, name, "4.6.0", "4.60").Kind);
    }

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
