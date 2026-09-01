using System.Numerics;

namespace SoftEngine.Core.Buffers;

public sealed class VelocityBuffer
{
    private float[] _velocity = [];

    private float[] _depth = [];

    public int Width { get; private set; }

    public int Height { get; private set; }

    public bool IsFilled { get; internal set; }

    public float[] Velocities => _velocity;

    public void Resize(int width, int height)
    {
        Width = System.Math.Max(0, width);
        Height = System.Math.Max(0, height);

        var pixels = Width * Height;

        if (_depth.Length >= pixels && _velocity.Length >= pixels * 2)
        {
            return;
        }

        _velocity = new float[pixels * 2];
        _depth = new float[pixels];
    }

    public void Clear()
    {
        var pixels = Width * Height;

        _velocity.AsSpan(0, pixels * 2).Clear();
        _depth.AsSpan(0, pixels).Fill(1f);

        IsFilled = false;
    }

    public Vector2 At(int x, int y)
    {
        var i = (x + y * Width) * 2;
        return new Vector2(_velocity[i], _velocity[i + 1]);
    }

    public bool IsCovered(int x, int y) => _depth[x + y * Width] < 1f;

    public float MaxSpeed()
    {
        var pixels = Width * Height;
        var max = 0f;

        for (var i = 0; i < pixels; i++)
        {
            var speed = MathF.Abs(_velocity[i * 2]) + MathF.Abs(_velocity[i * 2 + 1]);

            if (speed > max)
            {
                max = speed;
            }
        }

        return max;
    }

    internal void Write(int x, int y, float depth, float dx, float dy)
    {
        var pixel = x + y * Width;

        if (depth >= _depth[pixel])
        {
            return;
        }

        _depth[pixel] = depth;

        _velocity[pixel * 2] = dx;
        _velocity[pixel * 2 + 1] = dy;
    }
}
