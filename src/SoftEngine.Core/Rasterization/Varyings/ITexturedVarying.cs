using System.Numerics;

namespace SoftEngine.Core.Rasterization.Varyings;

public interface ITexturedVarying
{
    Vector2 TexCoord { get; }
}
