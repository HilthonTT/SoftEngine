using System.Numerics;

namespace SoftEngine.Core.Scenes.Lights;

/// <summary>
/// The light a scene falls back on when its world declares none.
///
/// It has to be shared rather than created per painter: the shadow pass and the shading
/// pass both need to agree on where the light is, or a scene would be lit from one
/// direction and shadowed from another.
/// </summary>
public static class SceneLights
{
    /// <summary>A point light above and behind the origin — enough to make an unlit world legible.</summary>
    public static ILight Default { get; } = new PointLight { Position = new Vector3(0, 10, 10) };

    /// <summary>The world's first light, or <see cref="Default"/> when it has none.</summary>
    public static ILight Resolve(IWorld world, ILight? preferred = null)
    {
        ArgumentNullException.ThrowIfNull(world);

        return world.Lights.Count > 0 ? world.Lights[0] : preferred ?? Default;
    }
}
