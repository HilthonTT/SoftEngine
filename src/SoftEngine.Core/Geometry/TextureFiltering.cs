namespace SoftEngine.Core.Geometry;

public enum TextureFiltering
{
    /// <summary>One texel per pixel — fast, blocky up close and shimmery at a distance.</summary>
    Nearest,

    /// <summary>Weighted average of the four surrounding texels.</summary>
    Bilinear,
}
