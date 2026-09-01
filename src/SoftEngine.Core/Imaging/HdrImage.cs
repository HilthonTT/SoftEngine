using SoftEngine.Core.Geometry;
using SoftEngine.Core.Shading;
using SoftEngine.Core.Textures;

namespace SoftEngine.Core.Imaging;

public sealed class HdrImage
{
    private readonly float[] _pixels;

    public HdrImage(int width, int height, float[] pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width, nameof(width));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height, nameof(height));
        ArgumentNullException.ThrowIfNull(pixels, nameof(pixels));

        if (pixels.Length != width * height * 3)
        {
            throw new ArgumentException(
                $"Expected {width * height * 3} floats for {width}×{height} RGB; got {pixels.Length}.",
                nameof(pixels));
        }

        Width = width;
        Height = height;
        _pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    public float[] Pixels => _pixels;

    public LinearColor this[int x, int y]
    {
        get
        {
            var i = (x + y * Width) * 3;
            return new LinearColor(_pixels[i], _pixels[i + 1], _pixels[i + 2]);
        }
    }

    public LinearColor Sample(float u, float v)
    {
        var fx = u * Width - 0.5f;
        var fy = v * Height - 0.5f;

        var x0 = (int)MathF.Floor(fx);
        var y0 = (int)MathF.Floor(fy);

        var tx = fx - x0;
        var ty = fy - y0;

        var xa = Wrap(x0, Width);
        var xb = Wrap(x0 + 1, Width);
        var ya = System.Math.Clamp(y0, 0, Height - 1);
        var yb = System.Math.Clamp(y0 + 1, 0, Height - 1);

        var top = LinearColor.Lerp(this[xa, ya], this[xb, ya], tx);
        var bottom = LinearColor.Lerp(this[xa, yb], this[xb, yb], tx);

        return LinearColor.Lerp(top, bottom, ty);
    }

    public float MaxLuminance
    {
        get
        {
            var max = 0f;

            for (var i = 0; i < _pixels.Length; i += 3)
            {
                var luminance = 0.2126f * _pixels[i] + 0.7152f * _pixels[i + 1] + 0.0722f * _pixels[i + 2];

                if (luminance > max)
                {
                    max = luminance;
                }
            }

            return max;
        }
    }

    public Texture ToTexture(float exposure = 1f)
    {
        var pixels = new int[Width * Height];

        for (var i = 0; i < pixels.Length; i++)
        {
            var f = i * 3;
            var color = new LinearColor(
                exposure * _pixels[f],
                exposure * _pixels[f + 1],
                exposure * _pixels[f + 2]);

            pixels[i] = color.ToColorRGB().Color;
        }

        return new Texture(Width, Height, pixels);
    }

    private static int Wrap(int x, int width)
    {
        x %= width;
        return x < 0 ? x + width : x;
    }
}
