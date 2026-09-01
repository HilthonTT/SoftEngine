using System.Numerics;

namespace SoftEngine.Core.Scenes.Lights;

public static class SceneLights
{
    public static ILight Default { get; } = new PointLight { Position = new Vector3(0, 10, 10) };

    public static ILight Resolve(IWorld world, ILight? preferred = null)
    {
        ArgumentNullException.ThrowIfNull(world, nameof(world));

        return world.Lights.Count > 0 ? world.Lights[0] : preferred ?? Default;
    }
}
