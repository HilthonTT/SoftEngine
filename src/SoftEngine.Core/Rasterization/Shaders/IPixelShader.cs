using SoftEngine.Core.Rasterization.Varyings;
using SoftEngine.Core.Shading;

namespace SoftEngine.Core.Rasterization.Shaders;

public interface IPixelShader<TVarying> where TVarying : struct, IVarying<TVarying>
{
    LinearColor Shade(in TVarying varying);

    static virtual bool HasAlphaTest => false;

    static virtual bool WritesSurface => false;

    bool IsCovered(in TVarying varying) => true;

    LinearColor Shade(in TVarying varying, out SurfaceSample surface)
    {
        surface = SurfaceSample.None;
        return Shade(varying);
    }
}
