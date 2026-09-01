using SoftEngine.Core.Geometry;
using SoftEngine.Core.Textures;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Shading;

public readonly struct AmbientCube
{
    private readonly LinearColor _positiveX;
    private readonly LinearColor _negativeX;
    private readonly LinearColor _positiveY;
    private readonly LinearColor _negativeY;
    private readonly LinearColor _positiveZ;
    private readonly LinearColor _negativeZ;

    public AmbientCube(
        LinearColor positiveX, LinearColor negativeX,
        LinearColor positiveY, LinearColor negativeY,
        LinearColor positiveZ, LinearColor negativeZ)
    {
        _positiveX = positiveX;
        _negativeX = negativeX;
        _positiveY = positiveY;
        _negativeY = negativeY;
        _positiveZ = positiveZ;
        _negativeZ = negativeZ;
    }

    public AmbientCube(LinearColor uniform)
        : this(uniform, uniform, uniform, uniform, uniform, uniform)
    {
    }

    public AmbientCube(float intensity)
        : this(new LinearColor(intensity, intensity, intensity))
    {
    }

    public static AmbientCube FromEnvironment(CubeMap environment, float intensity = 1f)
    {
        ArgumentNullException.ThrowIfNull(environment, nameof(environment));

        return new AmbientCube(
            Average(environment, CubeFace.PositiveX, intensity),
            Average(environment, CubeFace.NegativeX, intensity),
            Average(environment, CubeFace.PositiveY, intensity),
            Average(environment, CubeFace.NegativeY, intensity),
            Average(environment, CubeFace.PositiveZ, intensity),
            Average(environment, CubeFace.NegativeZ, intensity));
    }

    public LinearColor this[CubeFace face] => face switch
    {
        CubeFace.PositiveX => _positiveX,
        CubeFace.NegativeX => _negativeX,
        CubeFace.PositiveY => _positiveY,
        CubeFace.NegativeY => _negativeY,
        CubeFace.PositiveZ => _positiveZ,
        _ => _negativeZ,
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LinearColor Evaluate(Vector3 normal)
    {
        var x = normal.X * normal.X;
        var y = normal.Y * normal.Y;
        var z = normal.Z * normal.Z;

        var alongX = normal.X >= 0f ? _positiveX : _negativeX;
        var alongY = normal.Y >= 0f ? _positiveY : _negativeY;
        var alongZ = normal.Z >= 0f ? _positiveZ : _negativeZ;

        return x * alongX + y * alongY + z * alongZ;
    }

    private static LinearColor Average(CubeMap environment, CubeFace face, float intensity)
    {
        float r = 0f, g = 0f, b = 0f;
        int count;

        if (environment.Radiance(face) is { } radiance)
        {
            count = radiance.Length / 3;

            for (var i = 0; i < radiance.Length; i += 3)
            {
                r += radiance[i];
                g += radiance[i + 1];
                b += radiance[i + 2];
            }
        }
        else
        {
            var pixels = environment[face].Pixels;
            count = pixels.Length;

            foreach (var packed in pixels)
            {
                LinearColor texel = Diagnostics.ColorRGB.FromPacked(packed);

                r += texel.R;
                g += texel.G;
                b += texel.B;
            }
        }

        var scale = intensity / count;

        return new LinearColor(r * scale, g * scale, b * scale);
    }
}
