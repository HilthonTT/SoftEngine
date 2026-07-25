using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Lights;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Shading;

/// <summary>
/// The scene's lights, flattened for a frame's worth of shading.
///
/// A struct wrapping an array, so a painter can hand the whole set to a shader — which is
/// itself a struct, copied per triangle — without copying the lights with it and without
/// allocating. The array is rebuilt only when the world's lights change shape, so a static
/// scene reuses the same one frame after frame.
/// </summary>
public readonly struct LightSet
{
    private readonly ShaderLight[] _lights;

    internal LightSet(ShaderLight[] lights, int count)
    {
        _lights = lights;
        Count = count;
    }

    public int Count { get; }

    public ref readonly ShaderLight this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ref _lights[index];
    }

    /// <summary>
    /// A set from a fixed list of lights, allocating its own storage. For callers outside
    /// the frame loop — a test, a one-off render — where reusing an array buys nothing.
    /// The first light is the shadow caster, as in <see cref="Build"/>.
    /// </summary>
    public static LightSet Of(params ILight[] lights)
    {
        ArgumentNullException.ThrowIfNull(lights);

        var flattened = new ShaderLight[lights.Length];

        for (var i = 0; i < lights.Length; i++)
        {
            flattened[i] = ShaderLight.From(lights[i], castsShadow: i == 0);
        }

        return new LightSet(flattened, lights.Length);
    }

    /// <summary>
    /// Builds the set from a world, into <paramref name="storage"/> when it is big enough
    /// and a fresh array otherwise. A world with no lights of its own gets
    /// <paramref name="fallback"/>, so a scene is never rendered pitch black by omission.
    ///
    /// The first light is marked as the shadow caster, matching
    /// <see cref="SceneLights.Resolve"/> — the light the renderer built the shadow map from.
    /// </summary>
    public static LightSet Build(IWorld world, ILight? fallback, ref ShaderLight[] storage)
    {
        ArgumentNullException.ThrowIfNull(world);

        var lights = world.Lights;
        var count = lights.Count;

        if (count == 0)
        {
            var single = fallback ?? SceneLights.Default;

            if (storage.Length < 1)
            {
                storage = new ShaderLight[4];
            }

            storage[0] = ShaderLight.From(single, castsShadow: true);
            return new LightSet(storage, 1);
        }

        if (storage.Length < count)
        {
            storage = new ShaderLight[System.Math.Max(count, storage.Length * 2)];
        }

        for (var i = 0; i < count; i++)
        {
            storage[i] = ShaderLight.From(lights[i], castsShadow: i == 0);
        }

        return new LightSet(storage, count);
    }
}
