using SoftEngine.Core.Buffers;
using SoftEngine.Core.Shading;

namespace SoftEngine.Core.Pipeline.Temporal;

public sealed class MotionBlur
{
    public float ShutterFraction { get; set; } = 0.5f;

    public int Samples { get; set; } = 8;

    public float MaxLength { get; set; } = 48f;

    public void Apply(FrameBuffer surface, VelocityBuffer velocity)
    {
        ArgumentNullException.ThrowIfNull(surface, nameof(surface));
        ArgumentNullException.ThrowIfNull(velocity, nameof(velocity));

        var width = surface.Width;
        var height = surface.Height;

        if (!velocity.IsFilled ||
            velocity.Width != width ||
            velocity.Height != height ||
            width <= 0 || height <= 0)
        {
            return;
        }

        var shutter = MathF.Max(0f, ShutterFraction);
        var samples = System.Math.Max(2, Samples);

        if (shutter <= 0f || velocity.MaxSpeed() * shutter < 1f)
        {
            return;
        }

        var source = new float[width * height * 3];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var color = TemporalResolver.Read(surface, x, y);
                var i = (x + y * width) * 3;

                source[i] = color.R;
                source[i + 1] = color.G;
                source[i + 2] = color.B;
            }
        }

        Parallel.For(0, height, y =>
        {
            for (var x = 0; x < width; x++)
            {
                if (!velocity.IsCovered(x, y))
                {
                    continue;
                }

                var motion = velocity.At(x, y) * shutter;
                var length = motion.Length();

                if (length < 1f)
                {
                    continue;
                }

                if (length > MaxLength)
                {
                    motion *= MaxLength / length;
                }

                float r = 0f, g = 0f, b = 0f;
                var taken = 0;

                for (var s = 0; s < samples; s++)
                {
                    var t = s / (float)(samples - 1) - 0.5f;

                    var sx = (int)MathF.Round(x - motion.X * t);
                    var sy = (int)MathF.Round(y - motion.Y * t);

                    if (sx < 0 || sy < 0 || sx >= width || sy >= height)
                    {
                        continue;
                    }

                    var i = (sx + sy * width) * 3;

                    r += source[i];
                    g += source[i + 1];
                    b += source[i + 2];

                    taken++;
                }

                if (taken == 0)
                {
                    continue;
                }

                var scale = 1f / taken;

                TemporalResolver.Write(surface, x, y, new LinearColor(r * scale, g * scale, b * scale));
            }
        });
    }
}
