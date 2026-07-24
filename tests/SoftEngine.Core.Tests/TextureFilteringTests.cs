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
            .Shade(new TextureVarying(new Vector2(u, v), 1f));

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
            Assert.Equal(texture.Sample(u, 0.3f).Color, shader.Shade(new TextureVarying(new Vector2(u, 0.3f), 1f)).Color);
        }
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
