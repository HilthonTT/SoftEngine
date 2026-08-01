using SoftEngine.Core.Geometry;
using SoftEngine.Core.Shading;

namespace SoftEngine.Core.Imaging;

/// <summary>
/// An image in linear light with no upper bound: three floats per pixel, row 0 at the top.
///
/// <para>
/// This is what an environment has to be loaded into. A <see cref="Texture"/> holds packed sRGB
/// bytes, which is the right storage for a surface's albedo — paper white is the brightest thing
/// a painted surface can be — and the wrong storage for the sky, where the sun is four orders of
/// magnitude brighter than the cloud beside it. Clamp that to white on the way in and the
/// split-sum integral in <see cref="PrefilteredEnvironment"/> is convolving an image whose
/// dynamic range has already been thrown away: every reflection comes back flat, and no amount
/// of tone mapping afterwards can find a highlight that was quantized to 255 before shading
/// started.
/// </para>
///
/// <para>
/// Rows run downward from the top because that is the order Radiance's <c>-Y</c> scanlines
/// arrive in, and the order <see cref="CubeMap"/> already addresses a face's V in — so a
/// latitude maps straight to a row with nothing to flip.
/// </para>
/// </summary>
public sealed class HdrImage
{
    /// <summary>Linear RGB, three floats per pixel, in row-major order from the top row.</summary>
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

    /// <summary>The backing floats — three per pixel, red first — for a caller that wants the bulk.</summary>
    public float[] Pixels => _pixels;

    /// <summary>The pixel at a row and column, without filtering or bounds wrapping.</summary>
    public LinearColor this[int x, int y]
    {
        get
        {
            var i = (x + y * Width) * 3;
            return new LinearColor(_pixels[i], _pixels[i + 1], _pixels[i + 2]);
        }
    }

    /// <summary>
    /// Bilinear sample. U wraps and V clamps, which is what a panorama needs: longitude joins
    /// up with itself at the seam behind the viewer, latitude does not join the pole to the
    /// opposite pole.
    /// </summary>
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

    /// <summary>The brightest luminance in the image — how much range there was to preserve.</summary>
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

    /// <summary>
    /// The image as sRGB bytes, scaled by <paramref name="exposure"/> and clipped at white.
    ///
    /// Lossy on purpose and only for the consumers that cannot be anything else: showing the
    /// panorama in a picker, handing it to a GPU texture, drawing it on a surface. The shading
    /// path reads the floats.
    /// </summary>
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
