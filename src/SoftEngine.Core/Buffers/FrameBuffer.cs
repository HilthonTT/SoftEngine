using SoftEngine.Core.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Buffers;

public sealed class FrameBuffer(int width, int height)
{
    // Number of quantization steps used to store normalized device depth (0 at the near plane,
    // 1 at the far plane) across the full positive int range.
    private const int DepthResolution = int.MaxValue;

    private readonly int[] _zBuffer = new int[width * height];
    private readonly float _widthMinus1By2 = (width - 1) / 2;
    private readonly float _heightMinus1By2 = (height - 1) / 2;

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PutPixel(int x, int y, int z, ColorRGB color)
    {
#if DEBUG
        if (x > Width - 1 || x < 0 || y > Height - 1 || y < 0)
        {
            throw new OverflowException($"PutPixel X={x}/{Width}: Y={y}/{Height}, Depth={z}");
        }
#endif

        int index = x + y * Width;
        if (z > _zBuffer[index])
        {
            if (Stats is not null)
            {
                Stats.BehindZPixelCount++;
            }
            return;
        }

        if (Stats is not null)
        {
            Stats.DrawPixelCount++;
        }

        _zBuffer[index] = z;
        Screen[index] = color.Color;
    }

    public void DrawLine(Vector3 p0, Vector3 p1, ColorRGB color)
    {
        int x0 = (int)p0.X;
        int y0 = (int)p0.Y;
        int z0 = (int)p0.Z;
        int x1 = (int)p1.X;
        int y1 = (int)p1.Y;
        int z1 = (int)p1.Z;

        int dx = System.Math.Abs(x1 - x0);
        int dy = System.Math.Abs(y1 - y0);
        int dz = System.Math.Abs(z1 - z0);

        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int sz = z0 < z1 ? 1 : -1;

        int ex = 0;
        int ey = 0;
        int ez = 0;

        int dmax = System.Math.Max(dx, dy);

        int i = 0;
        while (i++ < dmax)
        {
            ex += dx; 
            if (ex >= dmax) 
            {
                ex -= dmax; x0 += sx;
                PutPixel(x0, y0, z0, color);
            }
            ey += dy; 
            if (ey >= dmax) 
            { 
                ey -= dmax; y0 += sy;
                PutPixel(x0, y0, z0, color); 
            }

            ez += dz; 
            if (ez >= dmax) 
            { 
                ez -= dmax; z0 += sz;
                PutPixel(x0, y0, z0, color); 
            }
        }
    }
}
