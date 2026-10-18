using SoftEngine.WinForms.Worlds;

namespace SoftEngine.WinForms.Tests.Worlds;

public class WorldScaleTests
{
    [Theory]
    [InlineData(5f, 0.1f)]
    [InlineData(60f, 1f)]
    [InlineData(500f, 10f)]
    [InlineData(1500f, 10f)]
    [InlineData(0.01f, 0.001f)]
    public void SnapStep_IsARoundNumberNearAFiftiethOfTheFraming(float reference, float expected)
    {
        Assert.Equal(expected, WorldScale.SnapStep(reference)!.Value, 1e-6f);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-3f)]
    public void SnapStep_IsUndefinedWithoutAFraming(float reference)
    {
        Assert.Null(WorldScale.SnapStep(reference));
    }

    [Fact]
    public void FogRange_ScalesWithTheFraming()
    {
        Assert.Equal((5f, 40f), WorldScale.FogRange(10f));
    }

    [Theory]
    [InlineData(1280, 1024)]
    [InlineData(1281, 2048)]
    public void ShadowResolution_DoublesOnWideViewports(int width, int expected)
    {
        Assert.Equal(expected, WorldScale.ShadowResolution(width));
    }

    [Fact]
    public void AmbientOcclusion_ScalesWithTheFramingAndFallsBackWithoutOne()
    {
        var (radius, bias) = WorldScale.AmbientOcclusion(100f);

        Assert.Equal(2f, radius, 1e-6f);
        Assert.Equal(0.08f, bias, 1e-6f);
        Assert.Equal(0.5f, WorldScale.AmbientOcclusion(0f).Radius);
    }

    [Fact]
    public void Reflections_ScaleWithTheFramingAndFallBackWithoutOne()
    {
        var (maxDistance, thickness) = WorldScale.Reflections(50f);

        Assert.Equal(50f, maxDistance);
        Assert.Equal(2f, thickness, 1e-6f);
        Assert.Equal(40f, WorldScale.Reflections(0f).MaxDistance);
    }
}
