using SoftEngine.Core.Rasterization.Varyings;
using SoftEngine.Core.Shading;

namespace SoftEngine.Core.Rasterization.Shaders;

public readonly struct CutoutShader<TVarying, TInner> : IPixelShader<TVarying>
    where TVarying : struct, IVarying<TVarying>, ITexturedVarying
    where TInner : struct, IPixelShader<TVarying>
{
    private readonly TInner _inner;
    private readonly TextureSampler _mask;
    private readonly float _cutoff;

    public CutoutShader(in TInner inner, in TextureSampler mask, float cutoff)
    {
        _inner = inner;
        _mask = mask;
        _cutoff = cutoff;
    }

    public static bool HasAlphaTest => true;

    public bool IsCovered(in TVarying varying) => _mask.SampleAlpha(varying.TexCoord) >= _cutoff;

    public LinearColor Shade(in TVarying varying) => _inner.Shade(varying);
}
