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
    }

    public Vector3 ToScreen3(Vector4 vector)
    {
        // Using width - 1 to prevent overflow by -1 and 1 NDC coordinates
        float x = _widthMinus1By2 * (vector.X / vector.W + 1);

        // Using height - 1 to prevent overflow by -1 and 1 NDC coordinates
        float y = -_heightMinus1By2 * (vector.Y / vector.W - 1);

        // Normalized device depth from the near/far planes, quantized to the buffer resolution.
        float z = DepthResolution * (_depthScale - _depthBias / vector.W);

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

    #region Pixel probe

    // What is currently drawing, for the pixel history. Thread-static because the paint
    // phase runs in parallel: each worker owns a disjoint set of screen rows, so the one
    // worker that owns the probed pixel's row is also the one that sets this context, and
    // the writes it appends stay in draw order.
    [ThreadStatic]
    private static ProbeContext _probeContext;

    private int _probeIndex = -1;
    private PixelHistory? _probeHistory;

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
