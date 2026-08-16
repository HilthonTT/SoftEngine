using SoftEngine.Core.Shading;
using System.Numerics;

namespace SoftEngine.Core.Tests.Shading;

public class IrradianceVolumeTests
{
    private static AmbientCube Grey(float level) => new(new LinearColor(level, level, level));

    /// <summary>A row of probes along x, from (0,0,0) to (1,0,0).</summary>
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

        // Outside the grid on either side. The volume covers the geometry it was baked over with a
        // margin, so a point beyond it is close to the edge, and the edge probe is a better answer
        // for it than any constant.
        Assert.Equal(0.25f, volume.Evaluate(new Vector3(-50f, 0, 0), Vector3.UnitY).R, 5);
        Assert.Equal(1f, volume.Evaluate(new Vector3(50f, 0, 0), Vector3.UnitY).R, 5);
    }

    [Fact]
    public void Evaluate_KeepsTheDirectionalityOfEachProbe()
    {
        // Both probes light a surface facing up and neither lights one facing down: the blend is
        // between cubes, not between the numbers one direction happens to pull out of them.
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
        // The probe on the right is inside a wall and carries nothing. Weighting it in anyway is
        // what puts a dark seam along the bottom of every wall in a scene, so it lends no weight
        // and the light comes out at the brightness of the probe that is usable.
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

        // Index order and (x, y, z) order have to agree, or the bake fills probes at points other
        // than the ones the lookup reads them back from.
        Assert.Equal(volume.ProbePosition(1, 0, 1), volume.ProbePosition(volume.IndexOf(1, 0, 1)));
    }

    [Fact]
    public void Evaluate_SurvivesAnAxisWithNoThicknessToInterpolateThrough()
    {
        // A scene as flat as a floor gives the grid a zero extent on one axis. The reciprocal of
        // that step is an infinity, and multiplying it by a position exactly on the plane is a NaN
        // — which would spread through every channel of every pixel the probe touched.
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
