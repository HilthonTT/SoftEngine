using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Shading;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Buffers;

public sealed class FrameBuffer(int width, int height)
{
    public const int DepthResolution = int.MaxValue;

    private readonly int[] _zBuffer = new int[width * height];
    private readonly float _widthMinus1By2 = (width - 1) / 2f;
    private readonly float _heightMinus1By2 = (height - 1) / 2f;

    private float _depthScale = 1f;
    private float _depthBias;

    private bool _linearDepth;

    private float[] _hdr = [];
    private bool _hdrEnabled;

    private int[] _overdraw = [];
    private bool _countOverdraw;

    private sbyte[] _mipLevels = [];
    private bool _recordMips;

    private uint[] _reflectance = [];
    private bool _recordReflectance;

    public RenderStats? Stats { get; set; }

    public int[] Screen { get; set; } = new int[width * height];

    public int Width { get; set; } = width;

    public int Height { get; set; } = height;

    public bool IsHighDynamicRange => _hdrEnabled;

    public float[] HdrColor => _hdr;

    public void SetHighDynamicRange(bool enabled)
    {
        _hdrEnabled = enabled;

        if (!enabled)
        {
            return;
        }

        var length = Width * Height * 3;

        if (_hdr.Length < length)
        {
            _hdr = new float[length];
        }
    }

    public void SetOverdrawCounting(bool enabled)
    {
        _countOverdraw = enabled;

        if (!enabled)
        {
            return;
        }

        var count = Width * Height;

        if (_overdraw.Length < count)
        {
            _overdraw = new int[count];
        }
    }

    public bool IsCountingOverdraw => _countOverdraw;

    public void WriteOverdraw(ReadOnlySpan<int> counts)
    {
        if (!_countOverdraw)
        {
            return;
        }

        var length = Width * Height;

        if (counts.Length < length)
        {
            throw new ArgumentException(
                $"Expected {length} counts, got {counts.Length}.", nameof(counts));
        }

        counts[..length].CopyTo(_overdraw.AsSpan(0, length));
    }

    public ReadOnlySpan<int> Overdraw =>
        _countOverdraw ? _overdraw.AsSpan(0, Width * Height) : ReadOnlySpan<int>.Empty;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CountWrite(int index)
    {
        if (_countOverdraw)
        {
            _overdraw[index]++;
        }
    }

    public void SetMipLevelRecording(bool enabled)
    {
        _recordMips = enabled;

        if (!enabled)
        {
            return;
        }

        var count = Width * Height;

        if (_mipLevels.Length < count)
        {
            _mipLevels = new sbyte[count];
        }
    }

    public bool IsRecordingMipLevels => _recordMips;

    public ReadOnlySpan<sbyte> MipLevels =>
        _recordMips ? _mipLevels.AsSpan(0, Width * Height) : ReadOnlySpan<sbyte>.Empty;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordMipLevel(int x, int y, int level)
    {
        if (_recordMips)
        {
            _mipLevels[x + y * Width] = (sbyte)System.Math.Clamp(level, -1, sbyte.MaxValue);
        }
    }

    public void SetReflectanceRecording(bool enabled)
    {
        _recordReflectance = enabled;

        if (!enabled)
        {
            return;
        }

        var count = Width * Height;

        if (_reflectance.Length < count)
        {
            _reflectance = new uint[count];
        }
    }

    public bool IsRecordingReflectance => _recordReflectance;

    public ReadOnlySpan<uint> Reflectance =>
        _recordReflectance ? _reflectance.AsSpan(0, Width * Height) : ReadOnlySpan<uint>.Empty;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordReflectance(int x, int y, uint packed)
    {
        if (_recordReflectance)
        {
            _reflectance[x + y * Width] = packed;
        }
    }

    public void ReadReflectance(uint[] destination)
    {
        ArgumentNullException.ThrowIfNull(destination, nameof(destination));

        var count = Width * Height;

        if (destination.Length < count)
        {
            throw new ArgumentException($"Expected room for {count} pixels, got {destination.Length}.", nameof(destination));
        }

        if (_recordReflectance)
        {
            _reflectance.AsSpan(0, count).CopyTo(destination);
        }
        else
        {
            destination.AsSpan(0, count).Clear();
        }
    }

    public void SetDepthRange(float zNear, float zFar)
    {
        _depthScale = zFar / (zFar - zNear);
        _depthBias = zFar * zNear / (zFar - zNear);
        _linearDepth = false;
    }

    public bool HasRecoverableDepth => !_linearDepth && _depthBias > 0f;

    public void ReadViewDepth(float[] destination)
    {
        ArgumentNullException.ThrowIfNull(destination, nameof(destination));

        var count = Width * Height;
        if (destination.Length < count)
        {
            throw new ArgumentException($"Expected room for {count} pixels, got {destination.Length}.", nameof(destination));
        }

        if (!HasRecoverableDepth)
        {
            Array.Fill(destination, float.PositiveInfinity, 0, count);
            return;
        }

        var zBuffer = _zBuffer;
        var scale = _depthScale;
        var bias = _depthBias;
        var width = Width;

        const float toNormalized = 1f / DepthResolution;

        Parallel.For(0, Height, y =>
        {
            var i = y * width;

            for (var x = 0; x < width; x++, i++)
            {
                var stored = zBuffer[i];

                if (stored >= DepthResolution)
                {
                    destination[i] = float.PositiveInfinity;
                    continue;
                }

                var denominator = scale - stored * toNormalized;

                destination[i] = denominator > 1e-9f ? bias / denominator : float.PositiveInfinity;
            }
        });
    }

    public void SetLinearDepthRange()
    {
        _depthScale = 1f;
        _depthBias = 0f;
        _linearDepth = true;
    }

    public Vector3 ToScreen3(Vector4 vector)
    {
        float x = _widthMinus1By2 * (vector.X / vector.W + 1);

        float y = -_heightMinus1By2 * (vector.Y / vector.W - 1);

        float z = DepthResolution * (_linearDepth ? vector.Z / vector.W : _depthScale - _depthBias / vector.W);

        return new Vector3(x, y, z);
    }

    private const int ClearBandRows = 32;

    public void Clear()
    {
        var pixels = Width * Height;

        if (_hdrEnabled)
        {
            var length = pixels * 3;
            if (_hdr.Length < length)
            {
                _hdr = new float[length];
            }
        }

        if (_countOverdraw)
        {
            if (_overdraw.Length < pixels)
            {
                _overdraw = new int[pixels];
            }
        }

        if (_recordMips)
        {
            if (_mipLevels.Length < pixels)
            {
                _mipLevels = new sbyte[pixels];
            }
        }

        if (_recordReflectance)
        {
            if (_reflectance.Length < pixels)
            {
                _reflectance = new uint[pixels];
            }
        }

        var bands = (Height + ClearBandRows - 1) / ClearBandRows;

        if (bands <= 1 || Environment.ProcessorCount <= 1)
        {
            ClearBand(0, Height);
            return;
        }

        Parallel.For(0, bands, band =>
        {
            var from = band * ClearBandRows;
            ClearBand(from, System.Math.Min(from + ClearBandRows, Height));
        });
    }

    public void ClearDepth() => _zBuffer.AsSpan(0, Width * Height).Fill(DepthResolution);

    private void ClearBand(int rowFrom, int rowTo)
    {
        var width = Width;
        var from = rowFrom * width;
        var count = (rowTo - rowFrom) * width;

        if (count <= 0)
        {
            return;
        }

        if (_hdrEnabled)
        {
            _hdr.AsSpan(from * 3, count * 3).Clear();
        }
        else
        {
            Screen.AsSpan(from, count).Clear();
        }

        if (_countOverdraw)
        {
            _overdraw.AsSpan(from, count).Clear();
        }

        if (_recordMips)
        {
            _mipLevels.AsSpan(from, count).Fill(-1);
        }

        if (_recordReflectance)
        {
            _reflectance.AsSpan(from, count).Clear();
        }

        _zBuffer.AsSpan(from, count).Fill(DepthResolution);
    }

    public void ResolveToScreen()
    {
        if (!_hdrEnabled)
        {
            return;
        }

        var hdr = _hdr;
        var screen = Screen;
        var width = Width;

        Parallel.For(0, Height, y =>
        {
            var pixel = y * width;
            var i = pixel * 3;

            for (var x = 0; x < width; x++, pixel++, i += 3)
            {
                screen[pixel] = unchecked((int)0xFF000000)
                    | (ColorSpace.ToSrgb(hdr[i]) << 16)
                    | (ColorSpace.ToSrgb(hdr[i + 1]) << 8)
                    | ColorSpace.ToSrgb(hdr[i + 2]);
            }
        });
    }

    public int GetColor(int x, int y) => Screen[x + y * Width];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int PackedAt(int index)
    {
        if (!_hdrEnabled)
        {
            return Screen[index];
        }

        var i = index * 3;
        return unchecked((int)0xFF000000)
            | (ColorSpace.ToSrgb(_hdr[i]) << 16)
            | (ColorSpace.ToSrgb(_hdr[i + 1]) << 8)
            | ColorSpace.ToSrgb(_hdr[i + 2]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private LinearColor LoadAt(int index)
    {
        if (!_hdrEnabled)
        {
            return ColorRGB.FromPacked(Screen[index]);
        }

        var i = index * 3;
        return new LinearColor(_hdr[i], _hdr[i + 1], _hdr[i + 2]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void StoreAt(int index, LinearColor color)
    {
        if (!_hdrEnabled)
        {
            Screen[index] = color.ToColorRGB().Color;
            return;
        }

        var i = index * 3;
        _hdr[i] = color.R;
        _hdr[i + 1] = color.G;
        _hdr[i + 2] = color.B;
    }

    public int GetDepth(int x, int y) => _zBuffer[x + y * Width];

    public void WriteNormalizedDepth(ReadOnlySpan<float> normalized)
    {
        var count = Width * Height;

        if (normalized.Length < count)
        {
            throw new ArgumentException(
                $"Expected {count} depth values, got {normalized.Length}.", nameof(normalized));
        }

        var zBuffer = _zBuffer;
        var width = Width;

        var source = normalized[..count];

        for (var y = 0; y < Height; y++)
        {
            var row = source.Slice(y * width, width);
            var target = zBuffer.AsSpan(y * width, width);

            for (var x = 0; x < width; x++)
            {
                var depth = row[x];

                target[x] = depth >= 1f || float.IsNaN(depth)
                    ? DepthResolution
                    : (int)(System.Math.Max(depth, 0f) * DepthResolution);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsBackground(int x, int y) => _zBuffer[x + y * Width] >= DepthResolution;

    public void PutBackground(int x, int y, LinearColor color)
    {
        var index = x + y * Width;

        CountWrite(index);

        if (index == _probeIndex)
        {
            RecordProbe(index, DepthResolution, color, DepthResolution, true);
        }

        StoreAt(index, color);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool DepthTest(int x, int y, int z) => z <= _zBuffer[x + y * Width];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Vector<int> DepthPassMask(int x, int y, in Vector<int> depths) =>
        Vector.LessThanOrEqual(depths, new Vector<int>(_zBuffer.AsSpan(x + y * Width, Vector<int>.Count)));

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

    public bool PutPixelOnTop(int x, int y, LinearColor color)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            return false;
        }

        var index = x + y * Width;

        CountWrite(index);

        if (index == _probeIndex)
        {
            RecordProbe(index, _zBuffer[index], color, _zBuffer[index], passed: true);
        }

        StoreAt(index, color);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool PutPixel(int x, int y, int z, LinearColor color)
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

        CountWrite(index);

        if (index == _probeIndex)
        {
            RecordProbe(index, z, color, previousDepth, passed);
        }

        if (!passed)
        {
            return false;
        }

        _zBuffer[index] = z;
        StoreAt(index, color);
        return true;
    }

    [ThreadStatic]
    private static FragmentBuffer.Arena? _fragmentArena;

    internal static void SetFragmentArena(FragmentBuffer.Arena? arena) => _fragmentArena = arena;

    internal void BlendStoredFragment(int index, int depth, LinearColor color, float alpha, in ProbeContext context)
    {
        var blended = LinearColor.Lerp(LoadAt(index), color, alpha);

        if (index == _probeIndex)
        {
            RecordProbeWith(
                index,
                depth,
                blended,
                _zBuffer[index],
                passed: true,
                context with { Source = PixelWriteSource.TransparentFragment });
        }

        StoreAt(index, blended);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool PutPixelBlend(int x, int y, int z, LinearColor color, float alpha)
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

        CountWrite(index);

        if (_fragmentArena is { } arena)
        {
            if (!passed)
            {
                if (index == _probeIndex)
                {
                    RecordProbe(index, z, color, previousDepth, passed: false);
                }

                return false;
            }

            arena.Add(x, y, z, color, alpha, index == _probeIndex ? _probeContext : default);
            return true;
        }

        if (passed || index == _probeIndex)
        {
            var blended = LinearColor.Lerp(LoadAt(index), color, alpha);

            if (index == _probeIndex)
            {
                RecordProbe(index, z, blended, previousDepth, passed);
            }

            if (passed)
            {
                StoreAt(index, blended);
                return true;
            }
        }

        return false;
    }

    #region Pixel probe

    [ThreadStatic]
    private static ProbeContext _probeContext;

    private int _probeIndex = -1;
    private PixelHistory? _probeHistory;

    public bool IsProbing => _probeIndex >= 0;

    public void BeginProbe(PixelHistory history)
    {
        ArgumentNullException.ThrowIfNull(history, nameof(history));

        _probeHistory = history;
        _probeIndex = history.X + history.Y * Width;
    }

    public void EndProbe()
    {
        _probeIndex = -1;
        _probeHistory = null;
    }

    internal static void SetProbeContext(int eventIndex, PixelWriteSource source, int objectId, int triangleIndex, VertexBuffer? vertexBuffer) =>
        _probeContext = new ProbeContext(eventIndex, source, objectId, triangleIndex, vertexBuffer);

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
            PreviousColor = PackedAt(_probeIndex),
            Depth = DepthResolution,
            PreviousDepth = _zBuffer[_probeIndex],
            Passed = true,
        });
    }

    internal int GetProbedColor() => _probeIndex >= 0 ? PackedAt(_probeIndex) : 0;

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
    private void RecordProbe(int index, int z, LinearColor color, int previousDepth, bool passed) =>
        RecordProbeWith(index, z, color, previousDepth, passed, _probeContext);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void RecordProbeWith(int index, int z, LinearColor color, int previousDepth, bool passed, in ProbeContext context)
    {
        var history = _probeHistory;
        if (history is null)
        {
            return;
        }

        var write = new PixelWrite
        {
            EventIndex = context.EventIndex,
            Source = context.Source,
            ObjectId = context.ObjectId,
            TriangleIndex = context.TriangleIndex,
            Color = color.ToColorRGB().Color,
            PreviousColor = PackedAt(index),
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

    internal readonly record struct ProbeContext(
        int EventIndex,
        PixelWriteSource Source,
        int ObjectId,
        int TriangleIndex,
        VertexBuffer? VertexBuffer);

    #endregion

    public void DrawLineOnTop(Vector3 p0, Vector3 p1, ColorRGB color)
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

        int ex = 0;
        int ey = 0;

        var drawn = 0;

        if (PutPixelOnTop(x0, y0, color)) { drawn++; }

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

            if (PutPixelOnTop(x0, y0, color)) { drawn++; }
        }

        Stats?.AddPixelCounts(drawn, 0);
    }

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
