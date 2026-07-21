using System.Numerics;

namespace SoftEngine.Core.Pipeline.Clipping;

public interface IClippingHomogeneous
{
    bool Clip(ref Vector4 begin, ref Vector4 end);
}
