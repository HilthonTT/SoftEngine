using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Shading;
using System.Numerics;

namespace SoftEngine.Core.Geometry;

/// <summary>
/// Builds environment cube maps without an asset to load, so a scene can have a sky — and
/// therefore ambient light with a direction to it — from nothing but a few colours.
/// </summary>
public static class SkyBox
{
    /// <summary>
    /// A gradient sky: one colour at the zenith, another at the horizon, a third below it,
    /// and a sun disc with a soft edge.
    ///
    /// The horizon band is what makes it read as a sky rather than as a coloured box. Real
    /// skies are brightest just above the horizon, where the line of sight passes through
    /// the most atmosphere, so the gradient is biased toward it rather than being linear
    /// in height.
    /// </summary>
    /// <param name="sunDirection">The direction the sunlight travels in — the same vector a <see cref="Scenes.Lights.DirectionalLight"/> takes, so the two can be given the same value and agree.</param>
    public static CubeMap Gradient(
        Vector3 sunDirection,
        ColorRGB? zenith = null,
        ColorRGB? horizon = null,
        ColorRGB? ground = null,
        ColorRGB? sun = null,
        float sunAngularSize = 0.04f,
        int resolution = 64)
    {
        LinearColor top = zenith ?? new ColorRGB(60, 108, 190);
        LinearColor middle = horizon ?? new ColorRGB(186, 210, 235);
        LinearColor below = ground ?? new ColorRGB(58, 54, 48);
        LinearColor disc = sun ?? new ColorRGB(255, 250, 232);

        var toSun = sunDirection.LengthSquared() > 1e-12f
            ? Vector3.Normalize(-sunDirection)
            : Vector3.UnitY;

        var cosSun = MathF.Cos(System.Math.Clamp(sunAngularSize, 1e-3f, 1.5f));

        return CubeMap.Generate(resolution, direction =>
        {
            var height = direction.Y;

            LinearColor color;

            if (height >= 0f)
            {
                // Square-rooted so the transition is quick near the horizon and slow
                // approaching the zenith, which is the shape the real gradient has.
                color = LinearColor.Lerp(middle, top, MathF.Sqrt(height));

                var cosToSun = Vector3.Dot(direction, toSun);
                if (cosToSun > cosSun)
                {
                    // Squared falloff across the disc: a hard edge would alias against the
                    // cube map's own texels long before it looked like a sun.
                    var t = (cosToSun - cosSun) / (1f - cosSun);
                    color = LinearColor.Lerp(color, disc, t * t);
                }
            }
            else
            {
                // A short ramp below the horizon rather than a hard line, so a reflection
                // or an ambient lookup that straddles it does not step.
                color = LinearColor.Lerp(middle, below, MathF.Min(1f, -height * 6f));
            }

            return color.ToColorRGB();
        });
    }

    /// <summary>A flat, colourless environment — useful as a control when comparing against a real one.</summary>
    public static CubeMap Uniform(ColorRGB color) => CubeMap.Generate(1, _ => color);
}
