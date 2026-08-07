using SoftEngine.Core.Buffers;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Scenes;
using System.Numerics;

namespace SoftEngine.Core.Pipeline.Temporal;

/// <summary>
/// Fills a <see cref="VelocityBuffer"/>: a second pass over the world that projects every vertex
/// twice — once with this frame's transforms and once with the previous frame's — and writes the
/// difference.
///
/// <para>
/// A separate pass rather than a varying carried by the main one, and that is a deliberate trade. A
/// varying would be free of a second traversal but would have to be threaded through every
/// <see cref="Rasterization.IVarying{T}"/>, every painter and every shader, whether or not anything
/// temporal is switched on — nine painters paying for a feature two of them can use. A pass costs a
/// second transform and fill of the frame's geometry, and costs it only when something asks. The
/// shadow pass makes the same trade for the same reason.
/// </para>
///
/// <para>
/// It has no near-plane clipping. A triangle straddling the eye is dropped rather than split, which
/// leaves a hole in the buffer where it would have been — and holes are already handled, because
/// every consumer has to cope with a pixel whose history is off screen anyway. Doing it properly
/// would mean lifting <see cref="Clipping.NearPlaneClipper"/> into a pass that produces two clip
/// positions per vertex instead of one, for a case where the surface is a hand's breadth from the
/// lens.
/// </para>
/// </summary>
public sealed class VelocityPass
{
    /// <summary>Rows a worker takes at a time, matching the way the shadow pass bands its fill.</summary>
    private const int BandRows = 32;

    /// <summary>One mesh's projected vertices, indexed the way its triangles index them.</summary>
    private readonly record struct Entry(int Slot, Triangle[] Triangles);

    private readonly List<Entry> _entries = [];

    // Kept across frames and grown as needed: a pass that allocates per mesh per frame is an
    // allocation per mesh per frame, which at thousands of meshes is the whole cost of it.
    private readonly List<Vector4[]> _current = [];
    private readonly List<Vector4[]> _previous = [];

    /// <summary>
    /// Projects the world twice and writes the per-pixel motion between the two.
    /// </summary>
    /// <param name="viewProjection">This frame's camera and projection, composed and <em>unjittered</em>.</param>
    /// <param name="state">Where everything was last frame; also what says whether there is a last frame.</param>
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
            // Nothing to compare against. Every velocity stays zero and every pixel stays
            // uncovered, which is what tells a consumer to fall back to this frame alone.
            return;
        }

        var previousViewProjection = state.PreviousViewProjection;

        // NDC ±1 onto pixel 0 and pixel n − 1, exactly as FrameBuffer.ToScreen3 maps it. A velocity
        // measured against a different mapping would be off by half a pixel at the frame's edges,
        // which is precisely the scale everything here works at.
        var halfWidth = (buffer.Width - 1) * 0.5f;
        var halfHeight = (buffer.Height - 1) * 0.5f;

        _entries.Clear();

        // Phase 1, sequential: every drawn mesh's vertices, in both frames' clip space.
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

        // Phase 2, parallel: one worker per band of rows. Bands rather than tiles because a
        // velocity is written by a depth compare against the pixel it lands on — so two workers must
        // never own the same pixel, and rows are the cheapest partition that guarantees it.
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

    /// <summary>Forgets the pooled arrays. For a caller that has finished with the pass for now.</summary>
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

    /// <summary>
    /// Fills one triangle, interpolating where its surface was.
    ///
    /// The previous clip position is interpolated <em>perspective-correctly</em> — divided by this
    /// frame's w before the blend and multiplied back after — because it is an attribute of the
    /// surface being rasterized now, not a position in the frame being rasterized now. Interpolating
    /// it linearly across the screen would bend the motion of any surface at an angle to the camera,
    /// which is most of a floor.
    /// </summary>
    private static void Fill(
        VelocityBuffer buffer,
        Vector4 c0, Vector4 c1, Vector4 c2,
        Vector4 p0, Vector4 p1, Vector4 p2,
        float halfWidth, float halfHeight,
        int rowFrom, int rowTo)
    {
        // Behind the eye, or on it. No near clipping here — see the class summary.
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

        // Winding normalized away: a back face still moved, and this pass has no shading for which
        // side it is to matter to.
        var invArea = 1f / area;

        var dw0 = (s1.Y - s2.Y) * invArea;
        var dw1 = (s2.Y - s0.Y) * invArea;

        var invW0 = 1f / c0.W;
        var invW1 = 1f / c1.W;
        var invW2 = 1f / c2.W;

        // The previous positions, premultiplied by this frame's reciprocal w.
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
                    // The surface was behind the eye last frame, so it has no previous pixel. Left
                    // uncovered rather than written with a wild number.
                    continue;
                }

                var previousX = halfWidth * (previousClip.X / previousClip.W + 1f);
                var previousY = -halfHeight * (previousClip.Y / previousClip.W - 1f);

                buffer.Write(x, y, depth, x + 0.5f - previousX, y + 0.5f - previousY);
            }
        }
    }

    /// <summary>Clip space to pixels, with normalized depth in z. The mapping <see cref="FrameBuffer.ToScreen3"/> uses.</summary>
    private static Vector3 Screen(Vector4 clip, float halfWidth, float halfHeight)
    {
        var invW = 1f / clip.W;

        return new Vector3(
            halfWidth * (clip.X * invW + 1f),
            -halfHeight * (clip.Y * invW - 1f),
            clip.Z * invW);
    }

    /// <summary>Twice the signed area of (a, b, point); its sign says which side the point is on.</summary>
    private static float Edge(in Vector3 a, in Vector3 b, float x, float y) =>
        (b.X - a.X) * (y - a.Y) - (b.Y - a.Y) * (x - a.X);
}
