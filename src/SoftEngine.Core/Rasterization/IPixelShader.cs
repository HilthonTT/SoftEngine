using SoftEngine.Core.Shading;

namespace SoftEngine.Core.Rasterization;

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
}
