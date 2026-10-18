using SoftEngine.Core.Buffers;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Scenes;
using System.Numerics;

namespace SoftEngine.Core.Pipeline.Temporal;

public sealed class VelocityPass
{
    private const int BandRows = 32;

    private readonly record struct Entry(int Slot, Triangle[] Triangles);

    private readonly List<Entry> _entries = [];

    private readonly List<Vector4[]> _current = [];
    private readonly List<Vector4[]> _previous = [];

    public void Render(
        IWorld world,
        VelocityBuffer buffer,
        in Matrix4x4 viewProjection,
        MotionState state)
    {
        ArgumentNullException.ThrowIfNull(world, nameof(world));
        ArgumentNullException.ThrowIfNull(buffer, nameof(buffer));
        ArgumentNullException.ThrowIfNull(state, nameof(state));

        buffer.Clear();

        if (!state.HasHistory || buffer.Width <= 0 || buffer.Height <= 0)
        {
            return;
        }

        var previousViewProjection = state.PreviousViewProjection;

        var halfWidth = (buffer.Width - 1) * 0.5f;
        var halfHeight = (buffer.Height - 1) * 0.5f;

        _entries.Clear();

        foreach (var mesh in world.Meshes)
        {
            if (!mesh.Visible || mesh.Opacity <= 0f || mesh.Triangles.Length == 0)
            {
                continue;
            }

            var vertices = mesh.Vertices;
            var slot = _entries.Count;

            var current = Reserve(_current, slot, vertices.Length);
            var previous = Reserve(_previous, slot, vertices.Length);

            var worldMatrix = mesh.WorldMatrix;
            var previousWorldMatrix = state.PreviousWorldMatrix(mesh, worldMatrix);

            var toClip = worldMatrix * viewProjection;
            var toPreviousClip = previousWorldMatrix * previousViewProjection;

            for (var v = 0; v < vertices.Length; v++)
            {
                var vertex = new Vector4(vertices[v], 1f);

                current[v] = Vector4.Transform(vertex, toClip);
                previous[v] = Vector4.Transform(vertex, toPreviousClip);
            }

            _entries.Add(new Entry(slot, mesh.Triangles));
        }

        if (_entries.Count == 0)
        {
            return;
        }

        var bands = (buffer.Height + BandRows - 1) / BandRows;

        Parallel.For(0, bands, band =>
        {
            var rowFrom = band * BandRows;
            var rowTo = System.Math.Min(rowFrom + BandRows, buffer.Height);

            foreach (var entry in _entries)
            {
                var current = _current[entry.Slot];
                var previous = _previous[entry.Slot];

                foreach (var triangle in entry.Triangles)
                {
                    Fill(
                        buffer,
                        current[triangle.I0], current[triangle.I1], current[triangle.I2],
                        previous[triangle.I0], previous[triangle.I1], previous[triangle.I2],
                        halfWidth, halfHeight, rowFrom, rowTo);
                }
            }
        });

        buffer.IsFilled = true;
    }

    public void Reset()
    {
        _entries.Clear();
        _current.Clear();
        _previous.Clear();
    }

    private static Vector4[] Reserve(List<Vector4[]> pool, int slot, int length)
    {
        while (pool.Count <= slot)
        {
            pool.Add([]);
        }

        if (pool[slot].Length < length)
        {
            pool[slot] = new Vector4[length];
        }

        return pool[slot];
    }

    private static void Fill(
        VelocityBuffer buffer,
        Vector4 c0, Vector4 c1, Vector4 c2,
        Vector4 p0, Vector4 p1, Vector4 p2,
        float halfWidth, float halfHeight,
        int rowFrom, int rowTo)
    {
        if (c0.W <= 1e-6f || c1.W <= 1e-6f || c2.W <= 1e-6f)
        {
            return;
        }

        var s0 = Screen(c0, halfWidth, halfHeight);
        var s1 = Screen(c1, halfWidth, halfHeight);
        var s2 = Screen(c2, halfWidth, halfHeight);

        var minX = System.Math.Max((int)MathF.Ceiling(MathF.Min(s0.X, MathF.Min(s1.X, s2.X)) - 0.5f), 0);
        var maxX = System.Math.Min((int)MathF.Floor(MathF.Max(s0.X, MathF.Max(s1.X, s2.X)) - 0.5f), buffer.Width - 1);

        if (minX > maxX)
        {
            return;
        }

        var minY = System.Math.Max((int)MathF.Ceiling(MathF.Min(s0.Y, MathF.Min(s1.Y, s2.Y)) - 0.5f), rowFrom);
        var maxY = System.Math.Min((int)MathF.Floor(MathF.Max(s0.Y, MathF.Max(s1.Y, s2.Y)) - 0.5f), rowTo - 1);

        if (minY > maxY)
        {
            return;
        }

        var area = Edge(s0, s1, s2.X, s2.Y);

        if (MathF.Abs(area) < 1e-9f)
        {
            return;
        }

        var invArea = 1f / area;

        var dw0 = (s1.Y - s2.Y) * invArea;
        var dw1 = (s2.Y - s0.Y) * invArea;

        var invW0 = 1f / c0.W;
        var invW1 = 1f / c1.W;
        var invW2 = 1f / c2.W;

        var q0 = p0 * invW0;
        var q1 = p1 * invW1;
        var q2 = p2 * invW2;

        for (var y = minY; y <= maxY; y++)
        {
            var py = y + 0.5f;
            var px = minX + 0.5f;

            var w0 = Edge(s1, s2, px, py) * invArea;
            var w1 = Edge(s2, s0, px, py) * invArea;

            for (var x = minX; x <= maxX; x++, w0 += dw0, w1 += dw1)
            {
                var w2 = 1f - w0 - w1;

                if (w0 < 0f || w1 < 0f || w2 < 0f)
                {
                    continue;
                }

                var depth = w0 * s0.Z + w1 * s1.Z + w2 * s2.Z;

                if (depth is < 0f or > 1f)
                {
                    continue;
                }

                var perspective = w0 * invW0 + w1 * invW1 + w2 * invW2;

                if (perspective <= 1e-12f)
                {
                    continue;
                }

                var previousClip = (q0 * w0 + q1 * w1 + q2 * w2) * (1f / perspective);

                if (previousClip.W <= 1e-6f)
                {
                    continue;
                }

                var previousX = halfWidth * (previousClip.X / previousClip.W + 1f);
                var previousY = -halfHeight * (previousClip.Y / previousClip.W - 1f);

                buffer.Write(x, y, depth, x + 0.5f - previousX, y + 0.5f - previousY);
            }
        }
    }

    private static Vector3 Screen(Vector4 clip, float halfWidth, float halfHeight)
    {
        var invW = 1f / clip.W;

        return new Vector3(
            halfWidth * (clip.X * invW + 1f),
            -halfHeight * (clip.Y * invW - 1f),
            clip.Z * invW);
    }

    private static float Edge(in Vector3 a, in Vector3 b, float x, float y) =>
        (b.X - a.X) * (y - a.Y) - (b.Y - a.Y) * (x - a.X);
}
