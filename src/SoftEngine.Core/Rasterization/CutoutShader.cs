using SoftEngine.Core.Shading;

namespace SoftEngine.Core.Rasterization;

/// <summary>
/// Any shader, with an alpha cutout in front of it: the mask is sampled at the pixel's UV and
/// anything below the cutoff is not drawn — not shaded, not blended, and not written to the
/// depth buffer either.
///
/// <para>
/// It wraps rather than being folded into the three shaders that can use it, and that is a
/// decision about the pixel loop rather than about tidiness. A cutoff carried as a field on
/// <see cref="TexturedShader"/> would put a compare per pixel into every textured surface in
/// every scene, whether or not one was ever authored with a mask. Wrapped, the fill is
/// instantiated over a different shader type, <see cref="IPixelShader{TVarying}.HasAlphaTest"/>
/// is a constant in each, and the surface that has no cutout is compiled without one.
/// </para>
///
/// <para>
/// The shading itself is untouched: a leaf that survives the test is lit exactly as the same
/// leaf would be with no mask on it, because a cutout is a statement about where the surface
/// <em>is</em>, not about how it responds to light.
/// </para>
/// </summary>
public readonly struct CutoutShader<TVarying, TInner> : IPixelShader<TVarying>
    where TVarying : struct, IVarying<TVarying>, ITexturedVarying
    where TInner : struct, IPixelShader<TVarying>
{
    private readonly TInner _inner;
    private readonly TextureSampler _mask;
    private readonly float _cutoff;

    /// <param name="inner">The shader that colours whatever survives the test.</param>
    /// <param name="mask">
    /// The map whose alpha channel is the mask — the albedo map, bound at the same mip level
    /// and filter the colour is sampled at, so the cut follows the image rather than a
    /// sharper or blurrier copy of it.
    /// </param>
    /// <param name="cutoff">Alpha at or above which the pixel is drawn.</param>
    public CutoutShader(in TInner inner, in TextureSampler mask, float cutoff)
    {
        _inner = inner;
        _mask = mask;
        _cutoff = cutoff;
    }

    /// <inheritdoc/>
    public static bool HasAlphaTest => true;

    /// <inheritdoc/>
    public bool IsCovered(in TVarying varying) => _mask.SampleAlpha(varying.TexCoord) >= _cutoff;

    /// <inheritdoc/>
    public LinearColor Shade(in TVarying varying) => _inner.Shade(varying);
}
