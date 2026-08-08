using SoftEngine.Core.Rasterization.Varyings;
using SoftEngine.Core.Shading;

namespace SoftEngine.Core.Rasterization.Shaders;

/// <summary>
/// Converts interpolated varyings into a final pixel colour.
/// Implementations must be structs for the same reason.
///
/// The result is a <see cref="LinearColor"/>: linear light with no ceiling, so a shader
/// can report a highlight brighter than white and leave it to the frame's resolve to
/// decide what becomes of it. Returning a <see cref="Diagnostics.ColorRGB"/> still works —
/// it converts implicitly — which is what shaders with no range above white do.
/// </summary>
public interface IPixelShader<TVarying> where TVarying : struct, IVarying<TVarying>
{
    LinearColor Shade(in TVarying varying);

    /// <summary>
    /// Whether this shader can reject a pixel outright, before it is shaded or written —
    /// what <see cref="CutoutShader{TVarying, TInner}"/> is, and what nothing else here is.
    ///
    /// <para>
    /// It is static, and that is the whole point of it. The rasterizer is generic over the
    /// shader's own type, so this resolves to a constant the moment the fill is instantiated
    /// for a shader that does not cut anything out — and the test, the call, and the branch
    /// around them fold away entirely. A per-instance flag would be a compare per pixel in
    /// every painter, paid for a feature all but one of them can use.
    /// </para>
    /// </summary>
    static virtual bool HasAlphaTest => false;

    /// <summary>
    /// Whether the pixel exists at all. Only consulted where
    /// <see cref="HasAlphaTest"/> says so; a shader that does not cut pixels out never has
    /// this called and never has to implement it.
    /// </summary>
    bool IsCovered(in TVarying varying) => true;
}
