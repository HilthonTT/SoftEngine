namespace SoftEngine.Core.Pipeline.PostProcess;

public sealed class VignetteEffect : IPostEffect
{
    public string Name => "Vignette";

    public bool Enabled { get; set; }

    public float Intensity { get; set; } = 0.45f;

    public float Radius { get; set; } = 0.55f;

    public float Softness { get; set; } = 0.45f;

    public void Apply(PostProcessTarget target)
    {
        var color = target.Color;
        var width = target.Width;
        var height = target.Height;

        var intensity = System.Math.Clamp(Intensity, 0f, 1f);
        var radius = MathF.Max(0f, Radius);
        var softness = MathF.Max(1e-4f, Softness);

        var halfWidth = width * 0.5f;
        var halfHeight = height * 0.5f;
        var corner = MathF.Sqrt(2f);

        Parallel.For(0, height, y =>
        {
            var dy = (y + 0.5f - halfHeight) / halfHeight;
            var i = y * width * 3;

            for (var x = 0; x < width; x++, i += 3)
            {
                var dx = (x + 0.5f - halfWidth) / halfWidth;
                var distance = MathF.Sqrt(dx * dx + dy * dy) / corner;

                var t = System.Math.Clamp((distance - radius) / softness, 0f, 1f);
                var falloff = 1f - intensity * t * t * (3f - 2f * t);

                color[i] *= falloff;
                color[i + 1] *= falloff;
                color[i + 2] *= falloff;
            }
        });
    }
}
