namespace SoftEngine.Core.Pipeline.PostProcess;

/// <summary>
/// Scales the image by an exposure and rolls the result back into [0, 1] with a curve
/// instead of a clamp. Clamping flattens everything above white into a single flat patch;
/// a tone-map curve keeps the ordering of those values, so bright regions stay shaped.
/// </summary>
public sealed class ToneMapEffect : IPostEffect
{
    public string Name => "Tone map";

    public bool Enabled { get; set; }

    /// <summary>Linear multiplier applied before the curve. 1 leaves the image's brightness alone.</summary>
    public float Exposure { get; set; } = 1.4f;

    public ToneMapOperator Operator { get; set; } = ToneMapOperator.Aces;

    public void Apply(PostProcessTarget target)
    {
        var color = target.Color;
        var width = target.Width;
        var exposure = MathF.Max(0f, Exposure);
        var aces = Operator == ToneMapOperator.Aces;

        Parallel.For(0, target.Height, y =>
        {
            var from = y * width * 3;
            var to = from + width * 3;

            for (var i = from; i < to; i++)
            {
                var value = color[i] * exposure;
                color[i] = aces ? Aces(value) : value / (1f + value);
            }
        });
    }

    /// <summary>
    /// Narkowicz's fit of the ACES filmic curve — the usual stand-in for the real thing,
    /// which is a chain of matrices and splines far too heavy to run per channel per pixel.
    /// </summary>
    private static float Aces(float x)
    {
        const float a = 2.51f;
        const float b = 0.03f;
        const float c = 2.43f;
        const float d = 0.59f;
        const float e = 0.14f;

        return System.Math.Clamp(x * (a * x + b) / (x * (c * x + d) + e), 0f, 1f);
    }
}
