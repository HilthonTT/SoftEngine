using System.Numerics;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Shading;

/// <summary>
/// The ambient term as a shader holds it: six directional averages for the whole scene, or a baked
/// <see cref="IrradianceVolume"/> that has an answer per place as well as per direction.
///
/// <para>
/// Both are asked the same question — light arriving at a point facing a way — so this exists to let
/// a shader ask it without knowing which one it got. The alternative was a second field and a second
/// code path in each of the three lit shaders, which is three chances for the two to drift apart.
/// </para>
///
/// <para>
/// The choice is a null test on a reference, not a virtual call: one predictable branch per shaded
/// pixel, taken the same way for every pixel of a frame. A scene with no volume runs the same
/// arithmetic it ran before this type existed, which is what keeps the golden images bit-identical.
/// </para>
/// </summary>
public readonly struct AmbientField
{
    private readonly AmbientCube _cube;
    private readonly IrradianceVolume? _volume;

    public AmbientField(AmbientCube cube)
    {
        _cube = cube;
        _volume = null;
    }

    /// <summary>
    /// A baked volume. Its <see cref="IrradianceVolume.Average"/> becomes the cube, so a caller that
    /// can only take six numbers — the GPU backend's uniforms — still gets the bake's own light
    /// rather than the environment it replaced.
    /// </summary>
    public AmbientField(IrradianceVolume volume)
    {
        ArgumentNullException.ThrowIfNull(volume, nameof(volume));

        _volume = volume;
        _cube = volume.Average;
    }

    public static implicit operator AmbientField(AmbientCube cube) => new(cube);

    /// <summary>Whether the light here came from a bake rather than from the environment or a constant.</summary>
    public bool IsBaked => _volume is not null;

    /// <summary>The volume, or null when this is a plain cube.</summary>
    public IrradianceVolume? Volume => _volume;

    /// <summary>
    /// The six directional averages: the scene's own when there is no volume, and the volume's mean
    /// when there is. For consumers that cannot evaluate per position.
    /// </summary>
    public AmbientCube Cube => _cube;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LinearColor Evaluate(Vector3 position, Vector3 normal) =>
        _volume is { } volume ? volume.Evaluate(position, normal) : _cube.Evaluate(normal);
}
