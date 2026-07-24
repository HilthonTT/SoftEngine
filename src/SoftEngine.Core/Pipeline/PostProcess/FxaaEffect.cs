namespace SoftEngine.Core.Pipeline.PostProcess;

/// <summary>
/// Fast approximate anti-aliasing: find the pixels that sit on a luminance edge, work out
/// which way that edge runs, and blur across it — never along it. Nothing about it knows
/// where the triangles were, which is exactly the point: the rasterizer keeps sampling one
/// point per pixel, and the stair-stepping is smoothed afterwards, for a fixed
/// screen-sized cost instead of a multiple of the whole render.
///
/// This is FXAA's edge detection and directional blend without its edge-walking refinement
/// pass, which trades a good deal of complexity for sharper near-horizontal lines.
/// </summary>
public sealed class FxaaEffect : IPostEffect
{
    public string Name => "FXAA";

    public bool Enabled { get; set; }

    /// <summary>
    /// Local contrast, as a fraction of the neighbourhood's brightest pixel, below which a
    /// pixel is left alone. Too low and flat gradients get smeared; too high and shallow
    /// edges keep their steps.
    /// </summary>
    public float EdgeThreshold { get; set; } = 0.125f;

    /// <summary>Absolute contrast floor, so near-black areas aren't filtered on sensor-level differences.</summary>
    public float EdgeThresholdMin { get; set; } = 0.0312f;

    /// <summary>Caps how far a pixel may be pulled toward its neighbours; 1 allows a full blend.</summary>
    public float Strength { get; set; } = 0.75f;

    public void Apply(PostProcessTarget target)
    {
        var width = target.Width;
        var height = target.Height;

        if (width < 3 || height < 3)
        {
            return;
        }

        // Every pixel reads its 3×3 neighbourhood, so the source has to be a copy — filtered
        // neighbours would feed back into the pixels after them.
        target.SnapshotToScratch();

        var source = target.Scratch;
        var destination = target.Color;

        var edgeThreshold = MathF.Max(0f, EdgeThreshold);
        var edgeThresholdMin = MathF.Max(0f, EdgeThresholdMin);
        var strength = System.Math.Clamp(Strength, 0f, 1f);

        Parallel.For(0, height, y =>
        {
            var north = System.Math.Max(y - 1, 0) * width;
            var middle = y * width;
            var south = System.Math.Min(y + 1, height - 1) * width;

            for (var x = 0; x < width; x++)
            {
                var west = System.Math.Max(x - 1, 0);
                var east = System.Math.Min(x + 1, width - 1);

                var lumaM = Luma(source, (middle + x) * 3);
                var lumaN = Luma(source, (north + x) * 3);
                var lumaS = Luma(source, (south + x) * 3);
                var lumaW = Luma(source, (middle + west) * 3);
                var lumaE = Luma(source, (middle + east) * 3);

                var lumaMin = MathF.Min(lumaM, MathF.Min(MathF.Min(lumaN, lumaS), MathF.Min(lumaW, lumaE)));
                var lumaMax = MathF.Max(lumaM, MathF.Max(MathF.Max(lumaN, lumaS), MathF.Max(lumaW, lumaE)));
                var range = lumaMax - lumaMin;

                if (range < MathF.Max(edgeThresholdMin, lumaMax * edgeThreshold))
                {
                    continue;
                }

                var lumaNW = Luma(source, (north + west) * 3);
                var lumaNE = Luma(source, (north + east) * 3);
                var lumaSW = Luma(source, (south + west) * 3);
                var lumaSE = Luma(source, (south + east) * 3);

                // Second derivatives measured along each axis. A vertical edge changes fast
                // as you walk across it in X and not at all in Y, so the axis with the
                // larger sum is the one to blur along — across the edge, never along it.
                var contrastAlongX =
                    MathF.Abs(lumaNW + lumaNE - 2f * lumaN) +
                    MathF.Abs(lumaW + lumaE - 2f * lumaM) * 2f +
                    MathF.Abs(lumaSW + lumaSE - 2f * lumaS);

                var contrastAlongY =
                    MathF.Abs(lumaNW + lumaSW - 2f * lumaW) +
                    MathF.Abs(lumaN + lumaS - 2f * lumaM) * 2f +
                    MathF.Abs(lumaNE + lumaSE - 2f * lumaE);

                // How far this pixel sits from its neighbourhood's mean — a lone pixel on a
                // step blends fully, one on a smooth ramp barely moves.
                var average = (2f * (lumaN + lumaS + lumaW + lumaE) + lumaNW + lumaNE + lumaSW + lumaSE) / 12f;
                var subPixel = System.Math.Clamp(MathF.Abs(average - lumaM) / range, 0f, 1f);
                var blend = subPixel * subPixel * strength;

                if (blend <= 0f)
                {
                    continue;
                }

                var blurAlongX = contrastAlongX >= contrastAlongY;

                var a = blurAlongX ? (middle + west) * 3 : (north + x) * 3;
                var b = blurAlongX ? (middle + east) * 3 : (south + x) * 3;
                var centre = (middle + x) * 3;

                for (var channel = 0; channel < 3; channel++)
                {
                    var neighbours = (source[a + channel] + source[b + channel]) * 0.5f;
                    destination[centre + channel] = source[centre + channel] + (neighbours - source[centre + channel]) * blend;
                }
            }
        });
    }

    /// <summary>
    /// Perceptual luminance. The buffer is linear, and edge detection has to match what the
    /// eye calls a step, so the square root stands in for the sRGB encode curve.
    /// </summary>
    private static float Luma(float[] color, int index) =>
        MathF.Sqrt(MathF.Max(0f, 0.2126f * color[index] + 0.7152f * color[index + 1] + 0.0722f * color[index + 2]));
}
