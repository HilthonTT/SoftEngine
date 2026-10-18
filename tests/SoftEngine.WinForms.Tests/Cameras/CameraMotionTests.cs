using SoftEngine.WinForms.Cameras;
using System.Numerics;

namespace SoftEngine.WinForms.Tests.Cameras;

public class CameraMotionTests
{
    [Fact]
    public void DollyDistance_OneNotchInClosesByTheZoomFactor()
    {
        Assert.Equal(10f / CameraMotion.ZoomPerNotch, CameraMotion.DollyDistance(10f, 1f), 1e-5f);
        Assert.Equal(10f * CameraMotion.ZoomPerNotch, CameraMotion.DollyDistance(10f, -1f), 1e-5f);
    }

    [Fact]
    public void DollyDistance_NeverReachesThePivot()
    {
        Assert.Equal(CameraMotion.MinCameraDistance, CameraMotion.DollyDistance(0.001f, 100f));
    }

    [Fact]
    public void ClampToPivot_StopsACameraOnTheNegativeSideCrossingOver()
    {
        var clamped = CameraMotion.ClampToPivot(new Vector3(1, 2, 3), new Vector3(1, 2, -5));

        Assert.Equal(new Vector3(1, 2, -CameraMotion.MinCameraDistance), clamped);
    }

    [Fact]
    public void ClampToPivot_StopsACameraOnThePositiveSideCrossingOver()
    {
        var clamped = CameraMotion.ClampToPivot(new Vector3(0, 0, -3), new Vector3(0, 0, 5));

        Assert.Equal(new Vector3(0, 0, CameraMotion.MinCameraDistance), clamped);
    }

    [Fact]
    public void ClampToPivot_LeavesAMoveOnTheSameSideAlone()
    {
        var position = new Vector3(4, 0, -2);

        Assert.Equal(position, CameraMotion.ClampToPivot(position, new Vector3(0, 0, -5)));
    }

    [Theory]
    [InlineData(false, false, 1f)]
    [InlineData(true, false, 4f)]
    [InlineData(false, true, 0.25f)]
    [InlineData(true, true, 1f)]
    public void SpeedModifier_ShiftIsCoarseAndControlIsFine(bool shift, bool control, float expected)
    {
        Assert.Equal(expected, CameraMotion.SpeedModifier(shift, control));
    }

    [Fact]
    public void FlyDirection_CombinesHeldKeys()
    {
        var direction = CameraMotion.FlyDirection(new HashSet<Keys> { Keys.W, Keys.A, Keys.Q });

        Assert.Equal(new Vector3(1, 1, 1), direction);
    }

    [Fact]
    public void FlyDirection_ArrowsAndLettersDoNotStack()
    {
        Assert.Equal(new Vector3(0, 0, 1), CameraMotion.FlyDirection(new HashSet<Keys> { Keys.W, Keys.Up }));
    }

    [Fact]
    public void FlyDirection_OpposingKeysCancel()
    {
        Assert.Equal(Vector3.Zero, CameraMotion.FlyDirection(new HashSet<Keys> { Keys.W, Keys.S, Keys.A, Keys.D }));
    }

    [Theory]
    [InlineData(Keys.W, true)]
    [InlineData(Keys.Left, true)]
    [InlineData(Keys.E, true)]
    [InlineData(Keys.G, false)]
    [InlineData(Keys.X, false)]
    public void IsMovementKey_OnlyClaimsTheFlyKeys(Keys key, bool expected)
    {
        Assert.Equal(expected, CameraMotion.IsMovementKey(key));
    }

    [Fact]
    public void FlySpeed_ScalesWithDistanceButKeepsAFloor()
    {
        Assert.Equal(1.5f, CameraMotion.FlySpeed(100f, 1f), 1e-5f);
        Assert.Equal(0.02f, CameraMotion.FlySpeed(0.1f, 1f), 1e-6f);
        Assert.Equal(6f, CameraMotion.FlySpeed(100f, 4f), 1e-5f);
    }
}
