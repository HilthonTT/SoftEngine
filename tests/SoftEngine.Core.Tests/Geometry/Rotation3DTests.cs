using SoftEngine.Core.Math;

namespace SoftEngine.Core.Tests.Geometry;

public class Rotation3DTests
{
    [Fact]
    public void Equality_ComparesComponents()
    {
        Assert.True(new Rotation3D(1, 2, 3) == new Rotation3D(1, 2, 3));
        Assert.False(new Rotation3D(1, 2, 3) == new Rotation3D(1, 2, 4));
        Assert.Equal(new Rotation3D(1, 2, 3).GetHashCode(), new Rotation3D(1, 2, 3).GetHashCode());
    }

    [Fact]
    public void Equality_IsReflexiveEvenForNaN()
    {
        // Equals has to be reflexive for a rotation to be found again in a set or dictionary.
        var rotation = new Rotation3D(float.NaN, 0, 0);

        Assert.True(rotation.Equals(rotation));
        Assert.True(rotation == new Rotation3D(float.NaN, 0, 0));
    }
}
