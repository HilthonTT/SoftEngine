using SoftEngine.Core.Buffers;
using SoftEngine.Core.Shading;
using System.Numerics;

namespace SoftEngine.Core.Pipeline.Temporal;

public sealed class TemporalResolver
{
    private float[] _history = [];

    private int _width;
    private int _height;

    public float Blend { get; set; } = 0.1f;

    public float ClampSlack { get; set; } = 0.25f;

    public bool HasHistory { get; private set; }

    public void Reset() => HasHistory = false;

    public void Resolve(FrameBuffer surface, VelocityBuffer velocity)
    {
        ArgumentNullException.ThrowIfNull(surface, nameof(surface));
        ArgumentNullException.ThrowIfNull(velocity, nameof(velocity));

        var width = surface.Width;
        var height = surface.Height;

        if (width <= 0 || height <= 0)
        {
            return;
        }

        if (_width != width || _height != height || _history.Length < width * height * 3)
        {
            _history = new float[width * height * 3];

            _width = width;
            _height = height;

            HasHistory = false;
        }

        var reprojects = HasHistory && velocity.IsFilled && velocity.Width == width && velocity.Height == height;

        var current = new float[width * height * 3];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var color = Read(surface, x, y);
                var i = (x + y * width) * 3;

                current[i] = color.R;
                current[i + 1] = color.G;
                current[i + 2] = color.B;
            }
        }

        if (!reprojects)
        {
            Array.Copy(current, _history, width * height * 3);
            HasHistory = true;

            return;
        }

        var blend = System.Math.Clamp(Blend, 0.01f, 1f);

        Parallel.For(0, height, y =>
        {
            for (var x = 0; x < width; x++)
            {
                var i = (x + y * width) * 3;

                var color = new LinearColor(current[i], current[i + 1], current[i + 2]);

                var motion = velocity.IsCovered(x, y) ? velocity.At(x, y) : Vector2.Zero;

                var previousX = x + 0.5f - motion.X;
                var previousY = y + 0.5f - motion.Y;

                if (previousX < 0f || previousY < 0f || previousX >= width || previousY >= height)
                {
                    Write(surface, x, y, color);
                    continue;
                }

                var history = Sample(_history, width, height, previousX - 0.5f, previousY - 0.5f);

                history = Clamp(current, width, height, x, y, history, ClampSlack);

                Write(surface, x, y, LinearColor.Lerp(history, color, blend));
            }
        });

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var color = Read(surface, x, y);
                var i = (x + y * width) * 3;

                _history[i] = color.R;
                _history[i + 1] = color.G;
                _history[i + 2] = color.B;
            }
        }

        HasHistory = true;
    }

    private static LinearColor Clamp(float[] frame, int width, int height, int x, int y, LinearColor color, float slack)
    {
        var minR = float.MaxValue;
        var minG = float.MaxValue;
        var minB = float.MaxValue;

        var maxR = float.MinValue;
        var maxG = float.MinValue;
        var maxB = float.MinValue;

        var x0 = System.Math.Max(x - 1, 0);
        var x1 = System.Math.Min(x + 1, width - 1);
        var y0 = System.Math.Max(y - 1, 0);
        var y1 = System.Math.Min(y + 1, height - 1);

        for (var ny = y0; ny <= y1; ny++)
        {
            for (var nx = x0; nx <= x1; nx++)
            {
                var i = (nx + ny * width) * 3;

                minR = MathF.Min(minR, frame[i]);
                maxR = MathF.Max(maxR, frame[i]);

                minG = MathF.Min(minG, frame[i + 1]);
                maxG = MathF.Max(maxG, frame[i + 1]);

                minB = MathF.Min(minB, frame[i + 2]);
                maxB = MathF.Max(maxB, frame[i + 2]);
            }
        }

        return new LinearColor(
            ClampChannel(color.R, minR, maxR, slack),
            ClampChannel(color.G, minG, maxG, slack),
            ClampChannel(color.B, minB, maxB, slack));
    }

    private static float ClampChannel(float value, float min, float max, float slack)
    {
        var margin = (max - min) * slack;

        return System.Math.Clamp(value, min - margin, max + margin);
    }

    private static LinearColor Sample(float[] frame, int width, int height, float x, float y)
    {
        var x0 = (int)MathF.Floor(x);
        var y0 = (int)MathF.Floor(y);

        var tx = x - x0;
        var ty = y - y0;

        var xa = System.Math.Clamp(x0, 0, width - 1);
        var xb = System.Math.Clamp(x0 + 1, 0, width - 1);
        var ya = System.Math.Clamp(y0, 0, height - 1);
        var yb = System.Math.Clamp(y0 + 1, 0, height - 1);

        var top = LinearColor.Lerp(At(frame, width, xa, ya), At(frame, width, xb, ya), tx);
        var bottom = LinearColor.Lerp(At(frame, width, xa, yb), At(frame, width, xb, yb), tx);

        return LinearColor.Lerp(top, bottom, ty);
    }

    private static LinearColor At(float[] frame, int width, int x, int y)
    {
        var i = (x + y * width) * 3;
        return new LinearColor(frame[i], frame[i + 1], frame[i + 2]);
    }

    internal static LinearColor Read(FrameBuffer surface, int x, int y)
    {
        if (!surface.IsHighDynamicRange)
        {
            return Diagnostics.ColorRGB.FromPacked(surface.GetColor(x, y));
        }

        var i = (x + y * surface.Width) * 3;
        var hdr = surface.HdrColor;

        return new LinearColor(hdr[i], hdr[i + 1], hdr[i + 2]);
    }

    internal static void Write(FrameBuffer surface, int x, int y, LinearColor color) =>
        surface.PutBackground(x, y, color);
}
