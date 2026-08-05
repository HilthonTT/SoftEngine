namespace SoftEngine.Core.Pipeline.PostProcess;

/// <summary>
/// Bleeds light out of the brightest parts of the image. The bright pass runs at a
/// fraction of the resolution — a wide blur is what sells the effect, and a wide blur on a
/// quarter-size buffer costs a sixteenth of the samples for a result nobody can tell apart
/// once it is added back over the full-resolution image.
///
/// Both the threshold and the blur are in linear light, which is the only space where
/// summing light is meaningful.
/// </summary>
public sealed class BloomEffect : IPostEffect
{
    private float[] _bright = [];
    private float[] _blurred = [];
    private float[] _kernel = [];
    private int _kernelRadius = -1;
    private int _width;
    private int _height;

    public string Name => "Bloom";

    public bool Enabled { get; set; }

    /// <summary>
    /// Linear luminance a pixel must exceed to bloom. An 8-bit render target tops out at 1,
    /// so a threshold near it leaves only the few pixels that clipped; an
    /// <see cref="Buffers.FrameBuffer.IsHighDynamicRange">HDR</see> one carries the real
    /// values, and the threshold then means what it says.
    /// </summary>
    public float Threshold { get; set; } = 0.65f;

    /// <summary>How much of the blurred result is added back over the image.</summary>
    public float Intensity { get; set; } = 0.55f;

    /// <summary>Size reduction of the bright buffer; 4 means a quarter of the width and height.</summary>
    public int Downsample { get; set; } = 4;

    /// <summary>Half-width of the blur kernel, in bright-buffer pixels.</summary>
    public int Radius { get; set; } = 5;

    /// <summary>Repeats of the separable blur. Two passes widen the tail without a wider kernel.</summary>
    public int Passes { get; set; } = 2;

    public void Apply(PostProcessTarget target)
    {
        var downsample = System.Math.Clamp(Downsample, 1, 16);

        var width = System.Math.Max(1, target.Width / downsample);
        var height = System.Math.Max(1, target.Height / downsample);

        EnsureBuffers(width, height);
        EnsureKernel(System.Math.Clamp(Radius, 1, 32));

        BrightPass(target, downsample);

        for (var pass = 0; pass < System.Math.Max(1, Passes); pass++)
        {
            Blur(horizontal: true);
            Blur(horizontal: false);
        }

        Composite(target, downsample);
    }

    private void EnsureBuffers(int width, int height)
    {
        _width = width;
        _height = height;

        var length = width * height * 3;
        if (_bright.Length >= length)
        {
            return;
        }

        _bright = new float[length];
        _blurred = new float[length];
    }

    private void EnsureKernel(int radius)
    {
        if (_kernelRadius == radius)
        {
            return;
        }

        // sigma = radius / 2 puts the kernel's useful support just inside its width, so
        // the truncated tail is small enough not to show as a hard edge.
        var sigma = radius * 0.5f;
        var weights = new float[radius + 1];
        var sum = 0f;

        for (var i = 0; i <= radius; i++)
        {
            weights[i] = MathF.Exp(-(i * i) / (2f * sigma * sigma));
            sum += i == 0 ? weights[i] : weights[i] * 2f;
        }

        for (var i = 0; i <= radius; i++)
        {
            weights[i] /= sum;
        }

        _kernel = weights;
        _kernelRadius = radius;
    }

    /// <summary>
    /// Box-averages each <c>Downsample × Downsample</c> block of the source and keeps only
    /// the light above the threshold. The colour is scaled rather than shifted, so a bright
    /// red stays red instead of drifting toward white as the threshold is subtracted.
    /// </summary>
    private void BrightPass(PostProcessTarget target, int downsample)
    {
        var source = target.Color;
        var sourceWidth = target.Width;
        var sourceHeight = target.Height;

        var bright = _bright;
        var width = _width;
        var threshold = MathF.Max(0f, Threshold);

        var blockArea = 1f / (downsample * (float)downsample);

        Parallel.For(0, _height, y =>
        {
            var i = y * width * 3;

            for (var x = 0; x < width; x++, i += 3)
            {
                float r = 0f, g = 0f, b = 0f;

                for (var by = 0; by < downsample; by++)
                {
                    var sy = System.Math.Min(y * downsample + by, sourceHeight - 1);
                    var row = sy * sourceWidth;

                    for (var bx = 0; bx < downsample; bx++)
                    {
                        var sx = System.Math.Min(x * downsample + bx, sourceWidth - 1);
                        var s = (row + sx) * 3;

                        r += source[s];
                        g += source[s + 1];
                        b += source[s + 2];
                    }
                }

                r *= blockArea;
                g *= blockArea;
                b *= blockArea;

                var luminance = 0.2126f * r + 0.7152f * g + 0.0722f * b;
                var scale = luminance > threshold ? (luminance - threshold) / luminance : 0f;

                bright[i] = r * scale;
                bright[i + 1] = g * scale;
                bright[i + 2] = b * scale;
            }
        });
    }

    /// <summary>One axis of the separable gaussian, from <see cref="_bright"/> back into itself via <see cref="_blurred"/>.</summary>
    private void Blur(bool horizontal)
    {
        var source = _bright;
        var destination = _blurred;
        var kernel = _kernel;
        var radius = _kernelRadius;
        var width = _width;
        var height = _height;

        Parallel.For(0, height, y =>
        {
            var i = y * width * 3;

            for (var x = 0; x < width; x++, i += 3)
            {
                var centre = kernel[0];
                var r = source[i] * centre;
                var g = source[i + 1] * centre;
                var b = source[i + 2] * centre;

                for (var k = 1; k <= radius; k++)
                {
                    var weight = kernel[k];

                    // Clamp addressing: the frame has no wrap-around, and clamping keeps the
                    // border from darkening the way a zero-padded kernel would.
                    var lowIndex = horizontal
                        ? (y * width + System.Math.Max(x - k, 0)) * 3
                        : (System.Math.Max(y - k, 0) * width + x) * 3;

                    var highIndex = horizontal
                        ? (y * width + System.Math.Min(x + k, width - 1)) * 3
                        : (System.Math.Min(y + k, height - 1) * width + x) * 3;

                    r += (source[lowIndex] + source[highIndex]) * weight;
                    g += (source[lowIndex + 1] + source[highIndex + 1]) * weight;
                    b += (source[lowIndex + 2] + source[highIndex + 2]) * weight;
                }

                destination[i] = r;
                destination[i + 1] = g;
                destination[i + 2] = b;
            }
        });

        (_bright, _blurred) = (_blurred, _bright);
    }

    /// <summary>Adds the blurred highlights back, bilinearly upsampled to the full resolution.</summary>
    private void Composite(PostProcessTarget target, int downsample)
    {
        var color = target.Color;
        var bright = _bright;

        var width = target.Width;
        var brightWidth = _width;
        var brightHeight = _height;
        var intensity = MathF.Max(0f, Intensity);
        var inverse = 1f / downsample;

        Parallel.For(0, target.Height, y =>
        {
            // Bright-buffer texel centres sit at (i + 0.5) * downsample in source pixels.
            var fy = (y + 0.5f) * inverse - 0.5f;
            var y0 = (int)MathF.Floor(fy);
            var ty = fy - y0;

            var row0 = System.Math.Clamp(y0, 0, brightHeight - 1) * brightWidth;
            var row1 = System.Math.Clamp(y0 + 1, 0, brightHeight - 1) * brightWidth;

            var i = y * width * 3;

            for (var x = 0; x < width; x++, i += 3)
            {
                var fx = (x + 0.5f) * inverse - 0.5f;
                var x0 = (int)MathF.Floor(fx);
                var tx = fx - x0;

                var column0 = System.Math.Clamp(x0, 0, brightWidth - 1);
                var column1 = System.Math.Clamp(x0 + 1, 0, brightWidth - 1);

                var i00 = (row0 + column0) * 3;
                var i10 = (row0 + column1) * 3;
                var i01 = (row1 + column0) * 3;
                var i11 = (row1 + column1) * 3;

                var w00 = (1f - tx) * (1f - ty);
                var w10 = tx * (1f - ty);
                var w01 = (1f - tx) * ty;
                var w11 = tx * ty;

                for (var channel = 0; channel < 3; channel++)
                {
                    color[i + channel] += intensity * (
                        bright[i00 + channel] * w00 +
                        bright[i10 + channel] * w10 +
                        bright[i01 + channel] * w01 +
                        bright[i11 + channel] * w11);
                }
            }
        });
    }
}
