using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Rasterization;
using SoftEngine.Core.Rasterization.Shaders;
using SoftEngine.Core.Rasterization.Varyings;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Shading;
using System.Numerics;

namespace SoftEngine.Core.Tests.Rasterization;

public class MultipleLightsTests
{
    private static PhongVarying Surface() => new(Vector3.Zero, Vector3.UnitZ);

    private static BlinnPhongShader Shader(LightSet lights, float specularStrength = 0f) =>
        new(ColorRGB.White, lights, new Vector3(0, 0, 5f),
            new AmbientCube(0.1f), specularStrength, 32f, gammaCorrect: true);

    private static DirectionalLight Facing(ColorRGB color, float intensity = 1f) =>
        new() { Direction = -Vector3.UnitZ, Color = color, Intensity = intensity };

    [Fact]
    public void TwoLights_DeliverMoreLightThanOne()
    {
        var one = Shader(LightSet.Of(Facing(ColorRGB.White))).Shade(Surface());
        var two = Shader(LightSet.Of(Facing(ColorRGB.White), Facing(ColorRGB.White))).Shade(Surface());

        Assert.True(two.R > one.R);

        Assert.Equal(one.R * 2f - 0.1f, two.R, 4);
    }

    [Fact]
    public void LightColors_MixPerChannel()
    {
        var shaded = Shader(LightSet.Of(Facing(ColorRGB.Red), Facing(ColorRGB.Green))).Shade(Surface());

        Assert.True(shaded.R > 1f);
        Assert.True(shaded.G > 1f);
        Assert.Equal(0.1f, shaded.B, 4);
    }

    [Fact]
    public void ALightFacingAway_ContributesNothing()
    {
        var away = new DirectionalLight { Direction = Vector3.UnitZ };

        var lit = Shader(LightSet.Of(Facing(ColorRGB.White))).Shade(Surface());
        var withExtra = Shader(LightSet.Of(Facing(ColorRGB.White), away)).Shade(Surface());

        Assert.Equal(lit.R, withExtra.R, 5);
    }

    [Fact]
    public void NoLights_StillLeavesAmbient()
    {
        var shaded = Shader(LightSet.Of()).Shade(Surface());

        Assert.Equal(0.1f, shaded.R, 4);
    }

    [Fact]
    public void PointLight_WithoutARange_DoesNotFallOff()
    {
        var light = new PointLight { Position = new Vector3(0, 0, 10f) };

        Assert.Equal(1f, light.AttenuationAt(Vector3.Zero));
        Assert.Equal(1f, light.AttenuationAt(new Vector3(0, 0, -1000f)));
    }

    [Fact]
    public void PointLight_WithARange_FallsOffToNothingAtIt()
    {
        var light = new PointLight { Position = Vector3.Zero, Range = 10f };

        Assert.Equal(1f, light.AttenuationAt(Vector3.Zero), 4);

        var near = light.AttenuationAt(new Vector3(2f, 0, 0));
        var mid = light.AttenuationAt(new Vector3(5f, 0, 0));

        Assert.True(near > mid);
        Assert.True(mid > 0f);

        Assert.Equal(0f, light.AttenuationAt(new Vector3(10f, 0, 0)));
        Assert.Equal(0f, light.AttenuationAt(new Vector3(25f, 0, 0)));
    }

    [Fact]
    public void SpotLight_LightsInsideItsConeAndNotOutside()
    {
        var spot = new SpotLight
        {
            Position = new Vector3(0, 10f, 0),
            Direction = -Vector3.UnitY,
            InnerAngle = 0.1f,
            OuterAngle = 0.3f,
        };

        Assert.Equal(1f, spot.AttenuationAt(Vector3.Zero), 4);
        Assert.Equal(0f, spot.AttenuationAt(new Vector3(20f, 0, 0)));

        var edge = spot.AttenuationAt(new Vector3(10f * MathF.Tan(0.2f), 0, 0));
        Assert.InRange(edge, 0.01f, 0.99f);
    }

    [Fact]
    public void SpotLight_BehindItself_IsDark()
    {
        var spot = new SpotLight
        {
            Position = Vector3.Zero,
            Direction = -Vector3.UnitY,
            OuterAngle = 0.5f,
        };

        Assert.Equal(0f, spot.AttenuationAt(new Vector3(0, 10f, 0)));
    }

    [Fact]
    public void ShaderLight_AgreesWithTheLightItFlattened()
    {
        var point = new PointLight { Position = new Vector3(3f, 4f, 0f), Range = 20f };
        var flattened = ShaderLight.From(point);

        var probe = new Vector3(0, 0, 5f);

        Assert.True(flattened.Sample(probe, out var toLight, out var attenuation));
        Assert.Equal(point.AttenuationAt(probe), attenuation, 5);
        Assert.Equal(point.DirectionFrom(probe).X, toLight.X, 5);
    }

    [Fact]
    public void ShaderLight_MarksOnlyTheFirstLightAsTheShadowCaster()
    {
        var lights = LightSet.Of(Facing(ColorRGB.White), Facing(ColorRGB.White), Facing(ColorRGB.White));

        Assert.True(lights[0].CastsShadow);
        Assert.False(lights[1].CastsShadow);
        Assert.False(lights[2].CastsShadow);
    }

    [Fact]
    public void Specular_TakesTheColorOfTheLightNotTheSurface()
    {
        var shaded = Shader(LightSet.Of(Facing(ColorRGB.Red)), specularStrength: 2f).Shade(Surface());

        Assert.True(shaded.R > shaded.B);
        Assert.Equal(0.1f, shaded.B, 4);
    }
}
