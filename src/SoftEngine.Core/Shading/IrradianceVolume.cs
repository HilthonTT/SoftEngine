using System.Numerics;

namespace SoftEngine.Core.Shading;

public sealed class IrradianceVolume
{
    private readonly AmbientCube[] _probes;
    private readonly bool[] _valid;

    private readonly Vector3 _min;
    private readonly Vector3 _step;

    private readonly Vector3 _scale;

    private readonly int _countX;
    private readonly int _countY;
    private readonly int _countZ;

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

    public int Count => _probes.Length;

    public int ValidCount { get; }

    public AmbientCube Average { get; }

    public AmbientCube Probe(int index) => _probes[index];

    public bool IsValid(int index) => _valid[index];

    public int IndexOf(int x, int y, int z) => x + _countX * (y + _countY * z);

    public Vector3 ProbePosition(int x, int y, int z) =>
        _min + new Vector3(_step.X * x, _step.Y * y, _step.Z * z);

    public Vector3 ProbePosition(int index)
    {
        var x = index % _countX;
        var y = index / _countX % _countY;
        var z = index / (_countX * _countY);

        return ProbePosition(x, y, z);
    }

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

        var scale = 1f / total;

        return new LinearColor(r * scale, g * scale, b * scale);
    }

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
