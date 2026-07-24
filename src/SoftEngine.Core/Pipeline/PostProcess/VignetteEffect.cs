namespace SoftEngine.Core.Pipeline.PostProcess;

/// <summary>
/// Darkens the frame toward its corners. Purely a look, but a cheap one: the falloff is a
/// function of the pixel's distance from the centre, normalized so it behaves the same at
/// any aspect ratio.
/// </summary>
public sealed class VignetteEffect : IPostEffect
{
    public string Name => "Vignette";

    public bool Enabled { get; set; }

    /// <summary>How dark the corners go: 0 is no effect, 1 is black.</summary>
    public float Intensity { get; set; } = 0.45f;

    /// <summary>Fraction of the way to the corner where the darkening starts.</summary>
    public float Radius { get; set; } = 0.55f;

    /// <summary>Width of the ramp from untouched to fully darkened, as a fraction of the same distance.</summary>
    public float Softness { get; set; } = 0.45f;

    public void Apply(PostProcessTarget target)
    {
        var color = target.Color;
        var width = target.Width;
        var height = target.Height;

        var intensity = System.Math.Clamp(Intensity, 0f, 1f);
        var radius = MathF.Max(0f, Radius);
        var softness = MathF.Max(1e-4f, Softness);

        // Distances are measured in units of half the frame, then divided by the corner's
        // distance — so "radius 1" is the corner whatever the window's shape.
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
                var falloff = 1f - intensity * t * t * (3f - 2f * t); // smoothstep

                color[i] *= falloff;
                color[i + 1] *= falloff;
                color[i + 2] *= falloff;
            }
        });
    }
}
