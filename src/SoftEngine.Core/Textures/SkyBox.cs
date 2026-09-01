using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Shading;
using System.Numerics;

namespace SoftEngine.Core.Textures;

public static class SkyBox
{
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
                color = LinearColor.Lerp(middle, top, MathF.Sqrt(height));

                var cosToSun = Vector3.Dot(direction, toSun);
                if (cosToSun > cosSun)
                {
                    var t = (cosToSun - cosSun) / (1f - cosSun);
                    color = LinearColor.Lerp(color, disc, t * t);
                }
            }
            else
            {
                color = LinearColor.Lerp(middle, below, MathF.Min(1f, -height * 6f));
            }

            return color.ToColorRGB();
        });
    }

    public static CubeMap HighDynamicRangeGradient(
        Vector3 sunDirection,
        ColorRGB? zenith = null,
        ColorRGB? horizon = null,
        ColorRGB? ground = null,
        ColorRGB? sun = null,
        float sunIntensity = 600f,
        float skyIntensity = 1f,
        float sunAngularSize = 0.04f,
        int resolution = 64)
    {
        LinearColor top = zenith ?? new ColorRGB(60, 108, 190);
        LinearColor middle = horizon ?? new ColorRGB(186, 210, 235);
        LinearColor below = ground ?? new ColorRGB(58, 54, 48);
        LinearColor disc = sun ?? new ColorRGB(255, 250, 232);

        disc = MathF.Max(0f, sunIntensity) * disc;

        var toSun = sunDirection.LengthSquared() > 1e-12f
            ? Vector3.Normalize(-sunDirection)
            : Vector3.UnitY;

        var cosSun = MathF.Cos(System.Math.Clamp(sunAngularSize, 1e-3f, 1.5f));
        var sky = MathF.Max(0f, skyIntensity);

        return CubeMap.GenerateRadiance(resolution, direction =>
        {
            var height = direction.Y;

            if (height < 0f)
            {
                return sky * LinearColor.Lerp(middle, below, MathF.Min(1f, -height * 6f));
            }

            var color = sky * LinearColor.Lerp(middle, top, MathF.Sqrt(height));

            var cosToSun = Vector3.Dot(direction, toSun);

            if (cosToSun > cosSun)
            {
                var t = (cosToSun - cosSun) / (1f - cosSun);
                color = LinearColor.Lerp(color, disc, t * t);
            }

            return color;
        });
    }

    public static CubeMap Uniform(ColorRGB color) => CubeMap.Generate(1, _ => color);
}
