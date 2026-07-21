using SoftEngine.Core.Diagnostics;

namespace SoftEngine.Core.Rasterization;

/// <summary>
/// Converts interpolated varyings into a final pixel colour.
/// Implementations must be structs for the same reason.
/// </summary>
public interface IPixelShader<TVarying> where TVarying : struct, IVarying<TVarying>
{
    ColorRGB Shade(in TVarying varying);
}
