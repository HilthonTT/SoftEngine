using SoftEngine.Core.Geometry;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Shading;

/// <summary>
/// The ambient term as six directional averages instead of one number — the light a
/// surface receives from everything that is not a light.
///
/// A single ambient constant says that a ceiling and a floor in the same room receive the
/// same light from their surroundings, which is never true: one faces the sky and the
/// other faces the ground. Six averages, one per axis, is the cheapest correction that
/// says otherwise, and it is what an environment map can be reduced to almost for free —
/// average each face, and the answer for a normal is the cosine-squared blend of the
/// three faces it points toward.
///
/// It is not a spherical-harmonic irradiance probe and does not pretend to be; what it
/// buys over a constant is that surfaces facing different ways are lit differently, which
/// is most of the visible difference.
/// </summary>
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

    /// <summary>The same light from every direction — a flat ambient constant.</summary>
    public AmbientCube(LinearColor uniform)
        : this(uniform, uniform, uniform, uniform, uniform, uniform)
    {
    }

    /// <summary>The same light from every direction, at the given intensity in each channel.</summary>
    public AmbientCube(float intensity)
        : this(new LinearColor(intensity, intensity, intensity))
    {
    }

    /// <summary>
    /// Reduces an environment map to its six face averages, scaled by
    /// <paramref name="intensity"/>.
    ///
    /// Averaging a whole face is a crude stand-in for integrating the cosine-weighted
    /// hemisphere around each axis, but it is the same integral over a coarser domain, and
    /// the result — a bright sky above, a dark ground below — is the part that shows.
    /// </summary>
    public static AmbientCube FromEnvironment(CubeMap environment, float intensity = 1f)
    {
        ArgumentNullException.ThrowIfNull(environment, nameof(environment));

        return new AmbientCube(
            Average(environment[CubeFace.PositiveX], intensity),
            Average(environment[CubeFace.NegativeX], intensity),
            Average(environment[CubeFace.PositiveY], intensity),
            Average(environment[CubeFace.NegativeY], intensity),
            Average(environment[CubeFace.PositiveZ], intensity),
            Average(environment[CubeFace.NegativeZ], intensity));
    }

    /// <summary>
    /// One face's average, in the order <see cref="CubeFace"/> names them.
    ///
    /// Exposed so a backend that evaluates this somewhere other than here — the GPU's
    /// fragment shader, which needs the six values as uniforms — can be handed the same cube
    /// rather than reducing the environment a second time and getting a slightly different
    /// answer.
    /// </summary>
    public LinearColor this[CubeFace face] => face switch
    {
        CubeFace.PositiveX => _positiveX,
        CubeFace.NegativeX => _negativeX,
        CubeFace.PositiveY => _positiveY,
        CubeFace.NegativeY => _negativeY,
        CubeFace.PositiveZ => _positiveZ,
        _ => _negativeZ,
    };

    /// <summary>
    /// The ambient light reaching a surface with the given normal. Weights are the squared
    /// components of the normal, which sum to 1 for a unit vector — so a uniform cube
    /// evaluates to exactly its constant, whichever way the surface faces.
    /// </summary>
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

    private static LinearColor Average(Texture face, float intensity)
    {
        var pixels = face.Pixels;

        float r = 0f, g = 0f, b = 0f;

        foreach (var packed in pixels)
        {
            // Averaged in linear light: this is a sum of light, not of encoded bytes.
            LinearColor texel = Diagnostics.ColorRGB.FromPacked(packed);

            r += texel.R;
            g += texel.G;
            b += texel.B;
        }

        var scale = intensity / pixels.Length;

        return new LinearColor(r * scale, g * scale, b * scale);
    }
}
