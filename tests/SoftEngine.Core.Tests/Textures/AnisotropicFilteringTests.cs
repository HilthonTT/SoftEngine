using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Rasterization;
using SoftEngine.Core.Textures;
using System.Numerics;

namespace SoftEngine.Core.Tests.Textures;

public class AnisotropicFilteringTests
{
    private static readonly ColorRGB Black = new(0, 0, 0);

    /// <summary>
    /// A right triangle covering 10x10 pixels whose texture is stretched 12.5x along screen x and
    /// barely at all along y — the glancing-angle floor case anisotropic filtering exists for.
    /// </summary>
    private static MipSelection SelectStretched(Texture texture, TextureFiltering filtering) =>
        MipSelector.SelectBlended(
            texture,
            filtering,
            new Vector3(0, 0, 0), new Vector3(10, 0, 0), new Vector3(0, 10, 0),
            new Vector2(0, 0), new Vector2(12.5f, 0), new Vector2(0, 1));

    /// <summary>The same triangle with the texture scaled the same amount on both axes.</summary>
    private static MipSelection SelectSquare(Texture texture, TextureFiltering filtering) =>
        MipSelector.SelectBlended(
            texture,
            filtering,
            new Vector3(0, 0, 0), new Vector3(10, 0, 0), new Vector3(0, 10, 0),
            new Vector2(0, 0), new Vector2(4, 0), new Vector2(0, 4));

    [Fact]
    public void Anisotropic_OnAStretchedFootprint_KeepsASharperLevelThanIsotropicDoes()
    {
        var texture = Texture.Checkerboard(8, 4, ColorRGB.White, Black);
        texture.EnsureMipMaps();

        var isotropic = SelectStretched(texture, TextureFiltering.Trilinear);
        var anisotropic = SelectStretched(texture, TextureFiltering.Anisotropic);

        // An area ratio cannot tell this footprint from a square one, so it blurs both axes to
        // suit the longer. Measuring the minor axis alone keeps the texture near-unfiltered on it.
        Assert.True(
            anisotropic.Level + anisotropic.Blend < isotropic.Level + isotropic.Blend,
            "anisotropic filtering should choose a finer level than the area ratio does");
    }

    [Fact]
    public void Anisotropic_OnAStretchedFootprint_SpreadsTapsAlongTheStretchedAxis()
    {
        var texture = Texture.Checkerboard(8, 4, ColorRGB.White, Black);
        texture.EnsureMipMaps();

        var mip = SelectStretched(texture, TextureFiltering.Anisotropic);

        Assert.True(mip.Taps > 1, "a stretched footprint needs more than one tap");

        // The stretch is purely along u, so the taps must walk u and leave v alone.
        Assert.True(MathF.Abs(mip.Step.X) > 0f);
        Assert.Equal(0f, mip.Step.Y, 6);

        // The taps together have to span the footprint's long axis, which is 12.5 texture widths
        // over 10 pixels, i.e. 1.25 in UV per pixel.
        Assert.Equal(1.25f, MathF.Abs(mip.Step.X) * mip.Taps, 4);
    }

    [Fact]
    public void Anisotropic_OnASquareFootprint_FallsBackToASingleTap()
    {
        var texture = Texture.Checkerboard(8, 4, ColorRGB.White, Black);
        texture.EnsureMipMaps();

        var mip = SelectSquare(texture, TextureFiltering.Anisotropic);

        Assert.Equal(1, mip.Taps);

        // With nothing to spread across, it should land where trilinear does.
        var trilinear = SelectSquare(texture, TextureFiltering.Trilinear);

        Assert.Equal(trilinear.Level, mip.Level);
        Assert.Equal(trilinear.Blend, mip.Blend, 4);
    }

    [Fact]
    public void Anisotropic_CapsItsTapsAtMaxAnisotropy()
    {
        var texture = Texture.Checkerboard(8, 4, ColorRGB.White, Black);
        texture.EnsureMipMaps();

        var previous = MipSelector.MaxAnisotropy;

        try
        {
            MipSelector.MaxAnisotropy = 2;
            var capped = SelectStretched(texture, TextureFiltering.Anisotropic);

            MipSelector.MaxAnisotropy = 16;
            var uncapped = SelectStretched(texture, TextureFiltering.Anisotropic);

            Assert.Equal(2, capped.Taps);
            Assert.True(uncapped.Taps > capped.Taps);

            // The taps a cap takes away have to be paid for with a coarser level, or the residue
            // of the long axis aliases instead of filtering.
            Assert.True(
                capped.Level + capped.Blend > uncapped.Level + uncapped.Blend,
                "a tighter cap should raise the mip level to cover the axis it can no longer walk");
        }
        finally
        {
            MipSelector.MaxAnisotropy = previous;
        }
    }

    [Fact]
    public void Anisotropic_OnATextureWithNoMipChain_SelectsLevelZeroAndNoTaps()
    {
        var texture = Texture.Checkerboard(8, 4, ColorRGB.White, Black);

        var mip = SelectStretched(texture, TextureFiltering.Anisotropic);

        Assert.Equal(0, mip.Level);
        Assert.Equal(0f, mip.Blend);
    }

    [Fact]
    public void Anisotropic_OnADegenerateTriangle_SelectsLevelZero()
    {
        var texture = Texture.Checkerboard(8, 4, ColorRGB.White, Black);
        texture.EnsureMipMaps();

        var mip = MipSelector.SelectBlended(
            texture,
            TextureFiltering.Anisotropic,
            new Vector3(0, 0, 0), new Vector3(10, 0, 0), new Vector3(20, 0, 0),
            new Vector2(0, 0), new Vector2(1, 0), new Vector2(2, 0));

        Assert.Equal(0, mip.Level);
        Assert.Equal(0f, mip.Blend);
    }

    [Fact]
    public void Anisotropic_WithASingleTap_IsTrilinearToTheBit()
    {
        var texture = Texture.Checkerboard(8, 2, ColorRGB.White, Black);
        texture.EnsureMipMaps();

        var trilinear = new TextureSampler(texture, new MipSelection(1, 0.25f), TextureFiltering.Trilinear);
        var anisotropic = new TextureSampler(texture, new MipSelection(1, 0.25f), TextureFiltering.Anisotropic);

        for (var i = 0; i < 16; i++)
        {
            var u = i / 16f;
            var v = 1f - u;

            Assert.Equal(trilinear.Sample(u, v).Color, anisotropic.Sample(u, v).Color);
            Assert.Equal(trilinear.SampleAlpha(u, v), anisotropic.SampleAlpha(u, v));
        }
    }

    [Fact]
    public void Anisotropic_AveragesEveryTapAcrossTheFootprint()
    {
        // One white texel beside one black one. Two taps half a texture apart, centred between
        // them, land on one each — so the result has to be the midpoint of the two.
        var texture = new Texture(2, 1, [ColorRGB.White.Color, Black.Color]);

        var sampler = new TextureSampler(
            texture,
            new MipSelection(0, 0f, new Vector2(0.5f, 0f), 2),
            TextureFiltering.Anisotropic);

        var texel = sampler.Sample(0.5f, 0.5f);

        Assert.Equal(128, texel.R);
        Assert.Equal(128, texel.G);
        Assert.Equal(128, texel.B);
    }

    [Fact]
    public void Anisotropic_AveragesAlphaAcrossItsTapsToo()
    {
        var opaqueWhite = unchecked((int)0xFFFFFFFF);

        var texture = new Texture(2, 1, [opaqueWhite, 0]);

        var sampler = new TextureSampler(
            texture,
            new MipSelection(0, 0f, new Vector2(0.5f, 0f), 2),
            TextureFiltering.Anisotropic);

        Assert.Equal(0.5f, sampler.SampleAlpha(0.5f, 0.5f), 2);
    }

    [Fact]
    public void Anisotropic_TapsWrapAcrossTheTextureEdge()
    {
        var texture = new Texture(2, 1, [ColorRGB.White.Color, Black.Color]);

        var sampler = new TextureSampler(
            texture,
            new MipSelection(0, 0f, new Vector2(0.5f, 0f), 2),
            TextureFiltering.Anisotropic);

        // Centred on the right-hand texel, the second tap runs off the end and must come back
        // around to the left-hand one rather than clamping onto black twice.
        var texel = sampler.Sample(1f, 0.5f);

        Assert.Equal(128, texel.R);
    }
}
