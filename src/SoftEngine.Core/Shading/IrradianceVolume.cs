using System.Numerics;

namespace SoftEngine.Core.Shading;

/// <summary>
/// The light arriving from everything that is not a light, measured at a grid of points and looked
/// up by where a surface is as well as which way it faces.
///
/// <para>
/// An <see cref="AmbientCube"/> answers "which way does this surface face" and nothing else, so
/// every surface in the world receiving the same answer is built into it. That is wrong in the way
/// that matters most: the underside of a table and the tabletop beside it face opposite directions
/// but are lit by the same room, and — the part a cube cannot express at all — the corner behind the
/// door and the middle of the floor face the <em>same</em> direction and are not. Bounce light,
/// shadowed ambient and colour bleeding are all differences between <em>places</em>.
/// </para>
///
/// <para>
/// So this is a cube per place: a regular grid of probes over the world, each holding what
/// <see cref="Baking.IrradianceBaker"/> found by tracing rays out of that point, and a lookup that
/// blends the eight probes around a position. Nothing about how a shader asks changes — it still
/// hands over a normal and gets light back — which is why a volume can be dropped into a scene
/// without a painter knowing it exists.
/// </para>
///
/// <para>
/// <b>Probes inside geometry are the trap.</b> A probe in the middle of a wall sees the inside of
/// the wall in every direction and bakes black; blended into the floor beside it, that black is a
/// dark smear along the bottom of every wall in the scene. Such probes are marked invalid at bake
/// time and lend no weight here, and the remaining weights are renormalized — so a position with
/// one usable neighbour is lit by that neighbour rather than by a seventh of it.
/// </para>
/// </summary>
public sealed class IrradianceVolume
{
    private readonly AmbientCube[] _probes;
    private readonly bool[] _valid;

    private readonly Vector3 _min;
    private readonly Vector3 _step;

    /// <summary>One over <see cref="_step"/>, or zero on an axis with no thickness to divide by.</summary>
    private readonly Vector3 _scale;

    private readonly int _countX;
    private readonly int _countY;
    private readonly int _countZ;

    /// <summary>
    /// Builds a volume over <paramref name="min"/>…<paramref name="max"/>. Probes sit on the corners
    /// of the grid, so the first and last on each axis are exactly on the boundary and a position
    /// anywhere inside the box has eight of them around it.
    /// </summary>
    public IrradianceVolume(
        Vector3 min,
        Vector3 max,
        int countX,
        int countY,
        int countZ,
        AmbientCube[] probes,
        bool[] valid,
        AmbientCube average)
    {
        ArgumentNullException.ThrowIfNull(probes, nameof(probes));
        ArgumentNullException.ThrowIfNull(valid, nameof(valid));

        ArgumentOutOfRangeException.ThrowIfLessThan(countX, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(countY, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(countZ, 1);

        var count = countX * countY * countZ;

        if (probes.Length != count || valid.Length != count)
        {
            throw new ArgumentException(
                $"A {countX}×{countY}×{countZ} volume needs {count} probes and {count} validity flags, " +
                $"got {probes.Length} and {valid.Length}.",
                nameof(probes));
        }

        _probes = probes;
        _valid = valid;

        _min = min;
        _countX = countX;
        _countY = countY;
        _countZ = countZ;

        var size = max - min;

        _step = new Vector3(
            countX > 1 ? size.X / (countX - 1) : 0f,
            countY > 1 ? size.Y / (countY - 1) : 0f,
            countZ > 1 ? size.Z / (countZ - 1) : 0f);

        // A zero step is a real case — a single probe on an axis, or a scene as flat as a floor —
        // and its reciprocal is an infinity that turns the first probe's own position into a NaN
        // grid coordinate. Zero instead pins every position to index 0, which is where the only
        // probe on that axis is.
        _scale = new Vector3(
            _step.X > 1e-20f ? 1f / _step.X : 0f,
            _step.Y > 1e-20f ? 1f / _step.Y : 0f,
            _step.Z > 1e-20f ? 1f / _step.Z : 0f);

        Min = min;
        Max = max;
        Average = average;

        var usable = 0;

        foreach (var flag in valid)
        {
            if (flag)
            {
                usable++;
            }
        }

        ValidCount = usable;
    }

    public Vector3 Min { get; }

    public Vector3 Max { get; }

    public int CountX => _countX;

    public int CountY => _countY;

    public int CountZ => _countZ;

    /// <summary>Probes in the grid, valid or not.</summary>
    public int Count => _probes.Length;

    /// <summary>How many of them were outside geometry and so carry light worth blending.</summary>
    public int ValidCount { get; }

    /// <summary>
    /// The mean of the valid probes — what a lookup falls back to when every probe around a position
    /// is buried. It is a poor answer, but it is the scene's own average rather than black, and a
    /// surface sealed inside geometry is not visible anyway.
    /// </summary>
    public AmbientCube Average { get; }

    public AmbientCube Probe(int index) => _probes[index];

    public bool IsValid(int index) => _valid[index];

    public int IndexOf(int x, int y, int z) => x + _countX * (y + _countY * z);

    /// <summary>Where a probe sits in the world — what the baker traced from, and what a debug view draws.</summary>
    public Vector3 ProbePosition(int x, int y, int z) =>
        _min + new Vector3(_step.X * x, _step.Y * y, _step.Z * z);

    public Vector3 ProbePosition(int index)
    {
        var x = index % _countX;
        var y = index / _countX % _countY;
        var z = index / (_countX * _countY);

        return ProbePosition(x, y, z);
    }

    /// <summary>
    /// The ambient light reaching a surface at <paramref name="position"/> facing
    /// <paramref name="normal"/>: the eight probes around it, blended trilinearly, each evaluated
    /// the way a single <see cref="AmbientCube"/> would have been.
    ///
    /// Positions outside the grid clamp to its edge rather than falling back to a constant. The
    /// volume covers the geometry it was baked over with a margin, so a point outside it is either
    /// something added since the bake or the far side of a surface on the boundary, and the nearest
    /// probe is a better answer for both than a flat grey.
    /// </summary>
    public LinearColor Evaluate(Vector3 position, Vector3 normal)
    {
        var local = (position - _min) * _scale;

        var (x0, x1, tx) = Axis(local.X, _countX);
        var (y0, y1, ty) = Axis(local.Y, _countY);
        var (z0, z1, tz) = Axis(local.Z, _countZ);

        var r = 0f;
        var g = 0f;
        var b = 0f;
        var total = 0f;

        for (var corner = 0; corner < 8; corner++)
        {
            var x = (corner & 1) == 0 ? x0 : x1;
            var y = (corner & 2) == 0 ? y0 : y1;
            var z = (corner & 4) == 0 ? z0 : z1;

            var index = IndexOf(x, y, z);

            if (!_valid[index])
            {
                continue;
            }

            var weight =
                ((corner & 1) == 0 ? 1f - tx : tx) *
                ((corner & 2) == 0 ? 1f - ty : ty) *
                ((corner & 4) == 0 ? 1f - tz : tz);

            if (weight <= 0f)
            {
                continue;
            }

            var light = _probes[index].Evaluate(normal);

            r += light.R * weight;
            g += light.G * weight;
            b += light.B * weight;

            total += weight;
        }

        if (total <= 1e-6f)
        {
            return Average.Evaluate(normal);
        }

        // With every corner valid this divides by one. It is what makes a partly buried
        // neighbourhood come out at the brightness of the probes that are usable instead of a
        // fraction of it, which is the difference between a dark seam along a wall and none.
        var scale = 1f / total;

        return new LinearColor(r * scale, g * scale, b * scale);
    }

    /// <summary>The two probe indices either side of a grid coordinate, and how far between them it lies.</summary>
    private static (int Low, int High, float Fraction) Axis(float coordinate, int count)
    {
        if (coordinate <= 0f || count == 1)
        {
            return (0, 0, 0f);
        }

        var last = count - 1;

        if (coordinate >= last)
        {
            return (last, last, 0f);
        }

        var low = (int)coordinate;

        return (low, low + 1, coordinate - low);
    }
}
