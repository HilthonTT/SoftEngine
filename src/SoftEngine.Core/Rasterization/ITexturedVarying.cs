using System.Numerics;

namespace SoftEngine.Core.Rasterization;

/// <summary>
/// A varying that carries a texture coordinate.
///
/// It exists so that something which needs only the UV — an alpha cutout, which reads a mask
/// and decides whether the pixel exists at all — can be written once against every varying
/// that has one, rather than once per varying or once per shader. The member is
/// <see cref="TexCoord"/> rather than <c>UV</c> because the varyings expose their UV as a
/// public field, and a field and a property cannot share a name.
/// </summary>
public interface ITexturedVarying
{
    Vector2 TexCoord { get; }
}
