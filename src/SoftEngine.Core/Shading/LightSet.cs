using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Lights;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Shading;

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

    public static LightSet Of(params ILight[] lights)
    {
        ArgumentNullException.ThrowIfNull(lights, nameof(lights));

        var flattened = new ShaderLight[lights.Length];

        for (var i = 0; i < lights.Length; i++)
        {
            flattened[i] = ShaderLight.From(lights[i], castsShadow: i == 0);
        }

        return new LightSet(flattened, lights.Length);
    }

    public static LightSet Build(IWorld world, ILight? fallback, ref ShaderLight[] storage)
    {
        ArgumentNullException.ThrowIfNull(world, nameof(world));

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
