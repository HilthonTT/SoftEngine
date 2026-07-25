using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Buffers;

public sealed class FrameBuffer(int width, int height)
{
    // Number of quantization steps used to store normalized device depth (0 at the near plane,
    // 1 at the far plane) across the full positive int range.
    public const int DepthResolution = int.MaxValue;

    private readonly int[] _zBuffer = new int[width * height];
    private readonly float _widthMinus1By2 = (width - 1) / 2f;
    private readonly float _heightMinus1By2 = (height - 1) / 2f;

    // Device depth as a function of the view-space distance w: depth = _depthScale - _depthBias / w.
    // Derived from the active projection's clip planes via SetDepthRange, so the buffer is defined
    // by the near/far planes rather than a fixed range. Overwritten before the first pixel is drawn.
    private float _depthScale = 1f;
    private float _depthBias;

    // Set by SetLinearDepthRange for parallel projections, where w is 1 everywhere and the
    // formula above would collapse to a constant: the projected z is the device depth already.
    private bool _linearDepth;

    public RenderStats? Stats { get; set; }

    public int[] Screen { get; set; } = new int[width * height];

    public int Width { get; set; } = width;

    public int Height { get; set; } = height;

    /// <summary>
    /// Defines the depth mapping from the active projection's clip planes. Device depth is 0 at
    /// <paramref name="zNear"/> and 1 at <paramref name="zFar"/>, and stays linear in 1/w so it
    /// interpolates correctly in screen space. Call once per frame before rasterizing.
    /// </summary>
    public void SetDepthRange(float zNear, float zFar)
    {
        _depthScale = zFar / (zFar - zNear);
        _depthBias = zFar * zNear / (zFar - zNear);
        _linearDepth = false;
    }

    /// <summary>
    /// Depth mapping for a parallel projection: the projection matrix has already mapped
    /// the near plane to z = 0 and the far plane to z = 1, so the buffer takes the
    /// projected z as-is. Call instead of <see cref="SetDepthRange"/> when the scene's
    /// projection reports <c>IsOrthographic</c>.
    /// </summary>
    public void SetLinearDepthRange()
    {
        _depthScale = 1f;
        _depthBias = 0f;
        _linearDepth = true;
    }

    public Vector3 ToScreen3(Vector4 vector)
    {
        // Using width - 1 to prevent overflow by -1 and 1 NDC coordinates
        float x = _widthMinus1By2 * (vector.X / vector.W + 1);

        // Using height - 1 to prevent overflow by -1 and 1 NDC coordinates
        float y = -_heightMinus1By2 * (vector.Y / vector.W - 1);

        // Normalized device depth from the near/far planes, quantized to the buffer resolution.
        float z = DepthResolution * (_linearDepth ? vector.Z / vector.W : _depthScale - _depthBias / vector.W);

        return new Vector3(x, y, z);
    }

    public void Clear()
    {
        Array.Fill(Screen, 0);
        Array.Fill(_zBuffer, DepthResolution);
    }

    /// <summary>Reads back one pixel of the render target, packed ARGB.</summary>
    public int GetColor(int x, int y) => Screen[x + y * Width];

    /// <summary>Reads back one pixel of the z-buffer, in raw depth units.</summary>
    public int GetDepth(int x, int y) => _zBuffer[x + y * Width];

    /// <summary>
    /// Whether the incoming depth would pass the depth test at (x, y). Lets the
    /// rasterizer reject occluded pixels before paying for interpolation and shading;
    /// <see cref="PutPixel"/> re-runs the test, so passing here is a hint, not a claim.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool DepthTest(int x, int y, int z) => z <= _zBuffer[x + y * Width];

    /// <summary>
    /// Whether not one of the <see cref="Vector{T}.Count"/> pixels starting at (x, y) can
    /// pass the depth test against <paramref name="depths"/>. A run that is entirely behind
    /// what is already drawn can be skipped whole, without interpolating or shading any of
    /// it. The caller must keep the run inside one row: <c>x + Vector&lt;int&gt;.Count ≤ Width</c>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool NoDepthPasses(int x, int y, in Vector<int> depths) =>
        Vector.GreaterThanAll(depths, new Vector<int>(_zBuffer.AsSpan(x + y * Width, Vector<int>.Count)));

    /// <summary>
    /// The farthest depth currently stored anywhere in the given rectangle. Nothing behind
    /// it can be seen there, so a triangle whose nearest point is farther still can be
    /// dropped without rasterizing it at all.
    ///
    /// The scan stops at the first row holding a pixel nothing has written yet: the clear
    /// value is the largest depth the buffer can hold, so the answer is already known, and
    /// a rectangle that is still partly background — the case where the bound would buy
    /// nothing — is left after a handful of reads instead of a full sweep.
    /// </summary>
    internal int MaxDepthIn(int xFrom, int yFrom, int xTo, int yTo)
    {
        var max = 0;
        var lanes = Vector<int>.Count;

        for (var y = yFrom; y < yTo; y++)
        {
            var row = _zBuffer.AsSpan(y * Width + xFrom, xTo - xFrom);
            var i = 0;

            if (Vector.IsHardwareAccelerated && row.Length >= lanes)
            {
                var accumulator = new Vector<int>(row);

                for (i = lanes; i <= row.Length - lanes; i += lanes)
                {
                    accumulator = Vector.Max(accumulator, new Vector<int>(row[i..]));
                }

                for (var lane = 0; lane < lanes; lane++)
                {
                    max = System.Math.Max(max, accumulator[lane]);
                }
            }

            for (; i < row.Length; i++)
            {
                max = System.Math.Max(max, row[i]);
            }

            if (max >= DepthResolution)
            {
                return DepthResolution;
            }
        }

        return max;
    }

    /// <summary>
    /// Depth-tests and writes one pixel. Returns true when the pixel was drawn, false
    /// when it was behind the z-buffer — callers batch these into stats themselves, so
    /// parallel rasterization doesn't contend on shared counters per pixel.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool PutPixel(int x, int y, int z, ColorRGB color)
    {
#if DEBUG
        if (x > Width - 1 || x < 0 || y > Height - 1 || y < 0)
        {
            throw new OverflowException($"PutPixel X={x}/{Width}: Y={y}/{Height}, Depth={z}");
        }
#endif

        int index = x + y * Width;
        int previousDepth = _zBuffer[index];
        bool passed = z <= previousDepth;

        // One int compare against a field that is -1 unless a pixel is being probed:
        // predictable enough not to show up next to the depth test itself.
        if (index == _probeIndex)
        {
            RecordProbe(index, z, color, previousDepth, passed);
        }

        if (!passed)
        {
            return false;
        }

        _zBuffer[index] = z;
        Screen[index] = color.Color;
        return true;
    }

    /// <summary>
    /// Depth-tests and alpha-blends one pixel over the current contents. The depth
    /// buffer is read but never written: transparent surfaces must not occlude what
    /// is drawn after them, only sit behind opaque geometry. Callers are responsible
    /// for drawing transparent geometry back-to-front.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool PutPixelBlend(int x, int y, int z, ColorRGB color, float alpha)
    {
#if DEBUG
        if (x > Width - 1 || x < 0 || y > Height - 1 || y < 0)
        {
            throw new OverflowException($"PutPixelBlend X={x}/{Width}: Y={y}/{Height}, Depth={z}");
        }
#endif

        int index = x + y * Width;
        int previousDepth = _zBuffer[index];
        bool passed = z <= previousDepth;

        if (passed || index == _probeIndex)
        {
            var blended = ColorRGB.Lerp(ColorRGB.FromPacked(Screen[index]), color, alpha);

            if (index == _probeIndex)
            {
                RecordProbe(index, z, blended, previousDepth, passed);
            }

            if (passed)
            {
                Screen[index] = blended.Color;
                return true;
            }
        }

        return false;
    }

    #region Pixel probe

    // What is currently drawing, for the pixel history. Thread-static because the paint
    // phase runs in parallel: each worker owns a disjoint set of screen rows, so the one
    // worker that owns the probed pixel's row is also the one that sets this context, and
    // the writes it appends stay in draw order.
    [ThreadStatic]
    private static ProbeContext _probeContext;

    private int _probeIndex = -1;
    private PixelHistory? _probeHistory;

    /// <summary>Whether a pixel probe is recording this frame (see <see cref="BeginProbe"/>).</summary>
    public bool IsProbing => _probeIndex >= 0;

    /// <summary>Starts recording every write attempt at <see cref="PixelHistory.X"/>, <see cref="PixelHistory.Y"/>.</summary>
    public void BeginProbe(PixelHistory history)
    {
        ArgumentNullException.ThrowIfNull(history);

        _probeHistory = history;
        _probeIndex = history.X + history.Y * Width;
    }

    public void EndProbe()
    {
        _probeIndex = -1;
        _probeHistory = null;
    }

    /// <summary>
    /// Tags the writes that follow on this thread with the object drawing them. The vertex
    /// buffer is only referenced, never copied: a probed pixel is hit by a handful of the
    /// thousands of triangles that call this, so vertices are snapshotted on a hit instead.
    /// </summary>
    internal static void SetProbeContext(int eventIndex, PixelWriteSource source, int objectId, int triangleIndex, VertexBuffer? vertexBuffer) =>
        _probeContext = new ProbeContext(eventIndex, source, objectId, triangleIndex, vertexBuffer);

    /// <summary>Appends a write the pipeline made outside <see cref="PutPixel"/> (a buffer clear).</summary>
    internal void RecordProbeClear(int eventIndex)
    {
        var history = _probeHistory;
        if (history is null)
        {
            return;
        }

        history.Writes.Add(new PixelWrite
        {
            EventIndex = eventIndex,
            Source = PixelWriteSource.Clear,
            ObjectId = SceneObjectIds.RenderTarget,
            TriangleIndex = -1,
            Color = 0,
            PreviousColor = Screen[_probeIndex],
            Depth = DepthResolution,
            PreviousDepth = _zBuffer[_probeIndex],
            Passed = true,
        });
    }

    /// <summary>The current colour of the probed pixel; 0 when nothing is being probed.</summary>
    internal int GetProbedColor() => _probeIndex >= 0 ? Screen[_probeIndex] : 0;

    /// <summary>
    /// Appends a write for a stage that rewrote the probed pixel outside the rasterizer — a
    /// full-screen post-process pass, which has already replaced the colour by the time it
    /// is recorded, hence the caller-supplied <paramref name="previousColor"/>.
    /// </summary>
    internal void RecordProbeOverwrite(int eventIndex, PixelWriteSource source, int objectId, int previousColor)
    {
        var history = _probeHistory;
        if (history is null)
        {
            return;
        }

        history.Writes.Add(new PixelWrite
        {
            EventIndex = eventIndex,
            Source = source,
            ObjectId = objectId,
            TriangleIndex = -1,
            Color = Screen[_probeIndex],
            PreviousColor = previousColor,
            Depth = _zBuffer[_probeIndex],
            PreviousDepth = _zBuffer[_probeIndex],
            Passed = true,
        });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void RecordProbe(int index, int z, ColorRGB color, int previousDepth, bool passed)
    {
        var history = _probeHistory;
        if (history is null)
        {
            return;
        }

        var context = _probeContext;

        var write = new PixelWrite
        {
            EventIndex = context.EventIndex,
            Source = context.Source,
            ObjectId = context.ObjectId,
            TriangleIndex = context.TriangleIndex,
            Color = color.Color,
            PreviousColor = Screen[index],
            Depth = z,
            PreviousDepth = previousDepth,
            Passed = passed,
            Vertices = SnapshotTriangle(context),
        };

        lock (history)
        {
            history.Writes.Add(write);
        }
    }

    private static ProbeVertex[]? SnapshotTriangle(in ProbeContext context)
    {
        var buffer = context.VertexBuffer;
        var mesh = buffer?.Mesh;

        if (buffer is null || mesh is null || (uint)context.TriangleIndex >= (uint)mesh.Triangles.Length)
        {
            return null;
        }

        var triangle = mesh.Triangles[context.TriangleIndex];

        return
        [
            SnapshotVertex(buffer, mesh, triangle.I0),
            SnapshotVertex(buffer, mesh, triangle.I1),
            SnapshotVertex(buffer, mesh, triangle.I2),
        ];
    }

    private static ProbeVertex SnapshotVertex(VertexBuffer buffer, IMesh mesh, int index)
    {
        var vertex = buffer.Vertices[index];
        return new ProbeVertex(mesh.Vertices[index], vertex.World, vertex.View, vertex.Proj, vertex.Norm);
    }

    private readonly record struct ProbeContext(
        int EventIndex,
        PixelWriteSource Source,
        int ObjectId,
        int TriangleIndex,
        VertexBuffer? VertexBuffer);

    #endregion

    public void DrawLine(Vector3 p0, Vector3 p1, ColorRGB color)
    {
        int x0 = (int)p0.X;
        int y0 = (int)p0.Y;
        int x1 = (int)p1.X;
        int y1 = (int)p1.Y;

        int dx = System.Math.Abs(x1 - x0);
        int dy = System.Math.Abs(y1 - y0);

        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;

        int dmax = System.Math.Max(dx, dy);

        // Depth spans the full quantized range — millions of units over a line of at most
        // a few thousand pixels — so it cannot be stepped with an integer error term like
        // x and y; it is interpolated over the dominant screen axis instead. Double keeps
        // the cast back to int exact at the extremes of the depth range.
        double z = p0.Z;
        double zStep = dmax > 0 ? (p1.Z - (double)p0.Z) / dmax : 0d;

        int ex = 0;
        int ey = 0;

        var drawn = 0;
        var behindZ = 0;

        if (PutPixel(x0, y0, ClampDepth(z), color)) { drawn++; } else { behindZ++; }

        int i = 0;
        while (i++ < dmax)
        {
            ex += dx;
            if (ex >= dmax)
            {
                ex -= dmax; x0 += sx;
            }
            ey += dy;
            if (ey >= dmax)
            {
                ey -= dmax; y0 += sy;
            }

            z += zStep;
            if (PutPixel(x0, y0, ClampDepth(z), color)) { drawn++; } else { behindZ++; }
        }

        Stats?.AddPixelCounts(drawn, behindZ);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ClampDepth(double z) =>
        (int)System.Math.Clamp(z, 0d, DepthResolution);
}
