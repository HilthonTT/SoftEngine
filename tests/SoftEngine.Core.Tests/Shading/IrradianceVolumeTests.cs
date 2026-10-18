using SoftEngine.Core.Shading;
using System.Numerics;

namespace SoftEngine.Core.Tests.Shading;

public class IrradianceVolumeTests
{
    private static AmbientCube Grey(float level) => new(new LinearColor(level, level, level));

    private static IrradianceVolume Row(AmbientCube[] probes, bool[]? valid = null, AmbientCube average = default) =>
        new(Vector3.Zero, Vector3.UnitX, probes.Length, 1, 1, probes, valid ?? [.. probes.Select(_ => true)], average);

    [Fact]
    public void Evaluate_BlendsBetweenTheProbesEitherSide()
    {
        var volume = Row([Grey(0f), Grey(1f)]);

        Assert.Equal(0f, volume.Evaluate(Vector3.Zero, Vector3.UnitY).R, 5);
        Assert.Equal(0.5f, volume.Evaluate(new Vector3(0.5f, 0, 0), Vector3.UnitY).R, 5);
        Assert.Equal(0.25f, volume.Evaluate(new Vector3(0.25f, 0, 0), Vector3.UnitY).R, 5);
        Assert.Equal(1f, volume.Evaluate(Vector3.UnitX, Vector3.UnitY).R, 5);
    }

    [Fact]
    public void Evaluate_ClampsToTheEdgeRatherThanFallingOffIt()
    {
        var volume = Row([Grey(0.25f), Grey(1f)]);

        Assert.Equal(0.25f, volume.Evaluate(new Vector3(-50f, 0, 0), Vector3.UnitY).R, 5);
        Assert.Equal(1f, volume.Evaluate(new Vector3(50f, 0, 0), Vector3.UnitY).R, 5);
    }

    [Fact]
    public void Evaluate_KeepsTheDirectionalityOfEachProbe()
    {
        var sky = new AmbientCube(
            LinearColor.Black, LinearColor.Black,
            LinearColor.White, LinearColor.Black,
            LinearColor.Black, LinearColor.Black);

        var volume = Row([sky, sky]);
        var middle = new Vector3(0.5f, 0, 0);

        Assert.Equal(1f, volume.Evaluate(middle, Vector3.UnitY).R, 5);
        Assert.Equal(0f, volume.Evaluate(middle, -Vector3.UnitY).R, 5);
    }

    [Fact]
    public void Evaluate_DropsBuriedProbesAndRenormalizes()
    {
        var volume = Row([Grey(1f), Grey(0f)], valid: [true, false]);

        Assert.Equal(1f, volume.Evaluate(new Vector3(0.5f, 0, 0), Vector3.UnitY).R, 5);
        Assert.Equal(1f, volume.Evaluate(new Vector3(0.9f, 0, 0), Vector3.UnitY).R, 5);
        Assert.Equal(1, volume.ValidCount);
    }

    [Fact]
    public void Evaluate_FallsBackToTheAverageWhenEveryNeighbourIsBuried()
    {
        var volume = Row([Grey(1f), Grey(1f)], valid: [false, false], average: Grey(0.3f));

        Assert.Equal(0.3f, volume.Evaluate(new Vector3(0.5f, 0, 0), Vector3.UnitY).R, 5);
        Assert.Equal(0, volume.ValidCount);
    }

    [Fact]
    public void ProbePosition_LandsOnTheCornersOfTheGrid()
    {
        var probes = new AmbientCube[8];
        var volume = new IrradianceVolume(
            new Vector3(-2f, 0f, 1f),
            new Vector3(2f, 4f, 5f),
            2, 2, 2,
            probes,
            new bool[8],
            default);

        Assert.Equal(new Vector3(-2f, 0f, 1f), volume.ProbePosition(0, 0, 0));
        Assert.Equal(new Vector3(2f, 4f, 5f), volume.ProbePosition(1, 1, 1));

        Assert.Equal(volume.ProbePosition(1, 0, 1), volume.ProbePosition(volume.IndexOf(1, 0, 1)));
    }

    [Fact]
    public void Evaluate_SurvivesAnAxisWithNoThicknessToInterpolateThrough()
    {
        var volume = new IrradianceVolume(
            Vector3.Zero,
            new Vector3(1f, 0f, 1f),
            2, 2, 2,
            [.. Enumerable.Repeat(Grey(0.5f), 8)],
            [.. Enumerable.Repeat(true, 8)],
            default);

        var light = volume.Evaluate(new Vector3(0.5f, 0f, 0.5f), Vector3.UnitY);

        Assert.False(float.IsNaN(light.R), "a flat volume must not produce NaN light");
        Assert.Equal(0.5f, light.R, 5);
    }
}
