using System.Numerics;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Shading;

public readonly struct AmbientField
{
    private readonly AmbientCube _cube;
    private readonly IrradianceVolume? _volume;

    public AmbientField(AmbientCube cube)
    {
        _cube = cube;
        _volume = null;
    }

    public AmbientField(IrradianceVolume volume)
    {
        ArgumentNullException.ThrowIfNull(volume, nameof(volume));

        _volume = volume;
        _cube = volume.Average;
    }

    public static implicit operator AmbientField(AmbientCube cube) => new(cube);

    public bool IsBaked => _volume is not null;

    public IrradianceVolume? Volume => _volume;

    public AmbientCube Cube => _cube;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LinearColor Evaluate(Vector3 position, Vector3 normal) =>
        _volume is { } volume ? volume.Evaluate(position, normal) : _cube.Evaluate(normal);
}
