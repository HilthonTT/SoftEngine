using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Rasterization;
using System.Numerics;

namespace SoftEngine.Core.Tests;

public class TextureFilteringTests
{
    private static readonly ColorRGB Black = new(0, 0, 0);

    /// <summary>2×2 texture: white top-left, black top-right, black bottom-left, white bottom-right.</summary>
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

        // (0.25, 0.75) is the center of the top-left texel (V grows upward).
        var texel = ShadeBilinear(texture, 0.25f, 0.75f);

        Assert.Equal(ColorRGB.White.Color, texel.Color);
    }

    [Fact]
    public void Bilinear_BetweenAllFourTexels_AveragesThem()
    {
        var texture = MakeQuad();

        var center = ShadeBilinear(texture, 0.5f, 0.5f);

        // Two white and two black texels at equal weight: 255 / 2, rounded up.
        Assert.Equal(128, center.R);
        Assert.Equal(128, center.G);
        Assert.Equal(128, center.B);
    }

    [Fact]
    public void Bilinear_AtUVOrigin_WrapsAcrossBothEdges()
    {
        var texture = MakeQuad();

        // (0, 0) sits between all four texels through wrap addressing, so the
        // result is the same four-way average as the middle of the texture.
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

    /// <summary>
    /// A triangle whose texel footprint is 8× its screen area — exactly 1.5 levels up, so the
    /// two selections have something to disagree about.
    /// </summary>
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
        // Level 0 is single-texel white and black; every level-1 texel averages a 2×2 block of
        // it, so it is a uniform 128. Half of each is the midpoint between the two.
        var texture = Texture.Checkerboard(4, 4, ColorRGB.White, Black);
        texture.EnsureMipMaps();

        var sampler = new TextureSampler(texture, new MipSelection(0, 0.5f), TextureFiltering.Trilinear);

        // (0.125, 0.875) is the center of the top-left texel — white at level 0.
        var texel = sampler.Sample(0.125f, 0.875f);

        Assert.Equal(192, texel.R); // (255 + 128) / 2, rounded
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

        // GetMip clamps past the end of the chain, so asking the last level to blend upward
        // would blend it with itself — a second tap that cannot change the answer.
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

    /// <summary>
    /// The seam this exists to remove, in the two triangles that make it. Levels are chosen per
    /// triangle, so two neighbours whose footprints sit either side of a rounding boundary —
    /// 1.49 and 1.51 levels — are drawn a whole level apart and their shared edge is visible as
    /// a change in sharpness. Blended, they are drawn 0.02 of a level apart, which is what
    /// their depths actually differ by.
    /// </summary>
    [Fact]
    public void Trilinear_KeepsNeighbouringTrianglesFromSteppingApart()
    {
        // Two-texel cells: level 1 is still a checkerboard, one texel to a cell, and level 2 has
        // averaged it into flat grey. So the two levels do not merely differ, they disagree
        // about the colour of the surface — which is what a seam between them looks like.
        var texture = Texture.Checkerboard(64, 32, ColorRGB.White, Black);
        texture.EnsureMipMaps();

        // The centre of a level-1 texel, so the sample is that texel rather than a blend of it
        // with its neighbours — the test is about levels, not about position within one.
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

    /// <summary>
    /// What a triangle whose footprint calls for <paramref name="exact"/> levels resolves to —
    /// built by handing the selector a triangle of the right shape rather than by repeating its
    /// arithmetic here, which would test this test.
    /// </summary>
    private static MipSelection LevelFor(Texture texture, TextureFiltering filtering, float exact)
    {
        // Screen area 50 against a texel footprint of 50 · 4^exact, since a level is a factor
        // of four in area. The UV triangle carries the whole ratio in its width.
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

        Assert.Equal(3, texture.MipCount); // 4×4, 2×2, 1×1
        Assert.Equal(2, texture.GetMip(1).Width);
        Assert.Equal(1, texture.GetMip(2).Width);
    }

    [Fact]
    public void EnsureMipMaps_HalvedLevelAveragesEachBlock()
    {
        // A single-texel checkerboard: every 2×2 block holds two whites and two
        // blacks, so every level-1 texel is the same mid gray.
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
