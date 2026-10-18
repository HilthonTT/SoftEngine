namespace SoftEngine.Core.Pipeline.PostProcess;

public sealed class ToneMapEffect : IPostEffect
{
    public string Name => "Tone map";

    public bool Enabled { get; set; }

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
