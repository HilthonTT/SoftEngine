using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Rasterization;
using SoftEngine.Core.Rasterization.Shaders;
using SoftEngine.Core.Rasterization.Varyings;
using SoftEngine.Core.Textures;
using System.Numerics;

namespace SoftEngine.Core.Tests.Textures;

public class TextureFilteringTests
{
    private static readonly ColorRGB Black = new(0, 0, 0);

    private static Texture MakeQuad() => new(2, 2,
    [
        ColorRGB.White.Color, Black.Color,
        Black.Color, ColorRGB.White.Color,
    ]);

    private static ColorRGB ShadeBilinear(Texture texture, float u, float v) =>
        new TexturedShader(texture, 0, TextureFiltering.Bilinear, false)
            .Shade(new TextureVarying(new Vector2(u, v), 1f))
            .ToColorRGB();

    [Fact]
    public void Bilinear_AtTexelCenter_ReturnsThatTexel()
    {
        var texture = MakeQuad();

        var texel = ShadeBilinear(texture, 0.25f, 0.75f);

        Assert.Equal(ColorRGB.White.Color, texel.Color);
    }

    [Fact]
    public void Bilinear_BetweenAllFourTexels_AveragesThem()
    {
        var texture = MakeQuad();

        var center = ShadeBilinear(texture, 0.5f, 0.5f);

        Assert.Equal(128, center.R);
        Assert.Equal(128, center.G);
        Assert.Equal(128, center.B);
    }

    [Fact]
    public void Bilinear_AtUVOrigin_WrapsAcrossBothEdges()
    {
        var texture = MakeQuad();

        var corner = ShadeBilinear(texture, 0f, 0f);

        Assert.Equal(128, corner.R);
    }

    [Fact]
    public void NearestFiltering_MatchesTextureSample()
    {
        var texture = Texture.Checkerboard(8, 4, ColorRGB.White, Black);
        var shader = new TexturedShader(texture, 0, TextureFiltering.Nearest, false);

        for (var i = 0; i < 8; i++)
        {
            var u = (i + 0.5f) / 8f;
            Assert.Equal(texture.Sample(u, 0.3f).Color, shader.Shade(new TextureVarying(new Vector2(u, 0.3f), 1f)).ToColorRGB().Color);
        }
    }

    private static MipSelection SelectFor(Texture texture, TextureFiltering filtering) =>
        MipSelector.SelectBlended(
            texture,
            filtering,
            new Vector3(0, 0, 0), new Vector3(10, 0, 0), new Vector3(0, 10, 0),
            new Vector2(0, 0), new Vector2(12.5f, 0), new Vector2(0, 1));

    [Fact]
    public void SelectBlended_WithoutTrilinear_RoundsToOneLevelAndBlendsNothing()
    {
        var texture = Texture.Checkerboard(8, 4, ColorRGB.White, Black);
        texture.EnsureMipMaps();

        var mip = SelectFor(texture, TextureFiltering.Bilinear);

        Assert.Equal(2, mip.Level);
        Assert.Equal(0f, mip.Blend);
    }

    [Fact]
    public void SelectBlended_ForTrilinear_TakesTheLowerLevelAndKeepsTheFraction()
    {
        var texture = Texture.Checkerboard(8, 4, ColorRGB.White, Black);
        texture.EnsureMipMaps();

        var mip = SelectFor(texture, TextureFiltering.Trilinear);

        Assert.Equal(1, mip.Level);
        Assert.Equal(0.5f, mip.Blend, 5);
    }

    [Fact]
    public void Trilinear_HalfwayBetweenLevels_MixesBothOfThem()
    {
        var texture = Texture.Checkerboard(4, 4, ColorRGB.White, Black);
        texture.EnsureMipMaps();

        var sampler = new TextureSampler(texture, new MipSelection(0, 0.5f), TextureFiltering.Trilinear);

        var texel = sampler.Sample(0.125f, 0.875f);

        Assert.Equal(192, texel.R);
        Assert.Equal(192, texel.G);
        Assert.Equal(192, texel.B);
    }

    [Fact]
    public void Trilinear_WithNothingToBlend_IsBilinearToTheBit()
    {
        var texture = Texture.Checkerboard(8, 2, ColorRGB.White, Black);
        texture.EnsureMipMaps();

        var bilinear = new TextureSampler(texture, 1, TextureFiltering.Bilinear);
        var trilinear = new TextureSampler(texture, new MipSelection(1, 0f), TextureFiltering.Trilinear);

        for (var i = 0; i < 16; i++)
        {
            var u = i / 16f;
            var v = 1f - u;

            Assert.Equal(bilinear.Sample(u, v).Color, trilinear.Sample(u, v).Color);
            Assert.Equal(bilinear.SampleAlpha(u, v), trilinear.SampleAlpha(u, v));
        }
    }

    [Fact]
    public void Trilinear_AtTheLastLevel_HasNothingCoarserToBlendWith()
    {
        var texture = Texture.Checkerboard(4, 1, ColorRGB.White, Black);
        texture.EnsureMipMaps();

        var last = texture.MipCount - 1;

        var blended = new TextureSampler(texture, new MipSelection(last, 0.75f), TextureFiltering.Trilinear);
        var plain = new TextureSampler(texture, last, TextureFiltering.Bilinear);

        Assert.Equal(plain.Sample(0.3f, 0.6f).Color, blended.Sample(0.3f, 0.6f).Color);
    }

    [Fact]
    public void Trilinear_OnATextureWithNoChain_SelectsLevelZeroAndNoBlend()
    {
        var texture = Texture.Checkerboard(8, 4, ColorRGB.White, Black);

        Assert.Equal(1, texture.MipCount);

        var mip = SelectFor(texture, TextureFiltering.Trilinear);

        Assert.Equal(0, mip.Level);
        Assert.Equal(0f, mip.Blend);
    }

    [Fact]
    public void Trilinear_KeepsNeighbouringTrianglesFromSteppingApart()
    {
        var texture = Texture.Checkerboard(64, 32, ColorRGB.White, Black);
        texture.EnsureMipMaps();

        const float u = 5.5f / 32f;
        const float v = 1f - 5.5f / 32f;

        var steppedGap = Gap(TextureFiltering.Bilinear);
        var blendedGap = Gap(TextureFiltering.Trilinear);

        Assert.True(steppedGap > 40, $"the two levels should look nothing alike; the gap was {steppedGap}");
        Assert.True(blendedGap < 4, $"blended, the two triangles should agree; the gap was {blendedGap}");

        int Gap(TextureFiltering filtering)
        {
            var near = new TextureSampler(texture, LevelFor(texture, filtering, 1.49f), filtering);
            var far = new TextureSampler(texture, LevelFor(texture, filtering, 1.51f), filtering);

            return System.Math.Abs(near.Sample(u, v).R - far.Sample(u, v).R);
        }
    }

    private static MipSelection LevelFor(Texture texture, TextureFiltering filtering, float exact)
    {
        const float screenArea = 50f;

        var texelArea = screenArea * MathF.Pow(4f, exact);
        var uvWidth = texelArea * 2f / (texture.Width * texture.Height);

        return MipSelector.SelectBlended(
            texture,
            filtering,
            new Vector3(0, 0, 0), new Vector3(10, 0, 0), new Vector3(0, 10, 0),
            new Vector2(0, 0), new Vector2(uvWidth, 0), new Vector2(0, 1));
    }

    [Fact]
    public void EnsureMipMaps_BuildsChainDownToOnePixel()
    {
        var texture = Texture.Checkerboard(4, 4, ColorRGB.White, Black);

        Assert.Equal(1, texture.MipCount);

        texture.EnsureMipMaps();

        Assert.Equal(3, texture.MipCount);
        Assert.Equal(2, texture.GetMip(1).Width);
        Assert.Equal(1, texture.GetMip(2).Width);
    }

    [Fact]
    public void EnsureMipMaps_HalvedLevelAveragesEachBlock()
    {
        var texture = Texture.Checkerboard(4, 4, ColorRGB.White, Black);
        texture.EnsureMipMaps();

        var mip = texture.GetMip(1);

        foreach (var pixel in mip.Pixels)
        {
            var color = ColorRGB.FromPacked(pixel);
            Assert.Equal(128, color.R);
            Assert.Equal(128, color.G);
            Assert.Equal(128, color.B);
        }
    }

    [Fact]
    public void GetMip_ClampsPastTheLastLevel()
    {
        var texture = Texture.Checkerboard(4, 4, ColorRGB.White, Black);
        texture.EnsureMipMaps();

        var last = texture.GetMip(99);

        Assert.Equal(1, last.Width);
        Assert.Equal(1, last.Height);
    }

    [Fact]
    public void EnsureMipMaps_HandlesNonSquareTextures()
    {
        var texture = new Texture(3, 2, new int[6]);
        texture.EnsureMipMaps();

        var mip = texture.GetMip(1);
        Assert.Equal(1, mip.Width);
        Assert.Equal(1, mip.Height);
    }
}
