using SoftEngine.Core.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Buffers;

public sealed class FrameBuffer(int width, int height)
{
    private const int Depth = int.MaxValue;

    private readonly int[] _zBuffer = new int[width * height];
    private readonly float _widthMinus1By2 = (width - 1) / 2;
    private readonly float _heightMinus1By2 = (height - 1) / 2;

    public RenderStats? Stats { get; set; }

    public int[] Screen { get; set; } = new int[width * height];

    public int Width { get; set; } = width;

    public int Height { get; set; } = height;

    public Vector3 ToScreen3(Vector4 vector)
    {
        // Using width - 1 to prevent overflow by -1 and 1 NDC coordinates
        float x = _widthMinus1By2 * (vector.X / vector.W + 1);

        // Using height - 1 to prevent overflow by -1 and 1 NDC coordinates
        float y = -_heightMinus1By2 * (vector.Y / vector.W - 1);

        float z = Depth * vector.Z / vector.W;

        return new Vector3(x, y, z);
    }

    public void Clear()
    {
        Array.Fill(Screen, 0);
        Array.Fill(_zBuffer, Depth);
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

        var index = x + y * Width;
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
        var x0 = (int)p0.X;
        var y0 = (int)p0.Y;
        var z0 = (int)p0.Z;
        var x1 = (int)p1.X;
        var y1 = (int)p1.Y; 
        var z1 = (int)p1.Z;

        var dx = System.Math.Abs(x1 - x0); 
        var dy = System.Math.Abs(y1 - y0); 
        var dz = System.Math.Abs(z1 - z0);

        var sx = x0 < x1 ? 1 : -1; 
        var sy = y0 < y1 ? 1 : -1; 
        var sz = z0 < z1 ? 1 : -1;

        var ex = 0; 
        var ey = 0; 
        var ez = 0;

        var dmax = System.Math.Max(dx, dy);

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
