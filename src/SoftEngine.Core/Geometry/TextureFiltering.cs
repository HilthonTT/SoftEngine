namespace SoftEngine.Core.Geometry;

public enum TextureFiltering
{
    /// <summary>One texel per pixel — fast, blocky up close and shimmery at a distance.</summary>
    Nearest,

    /// <summary>Weighted average of the four surrounding texels.</summary>
    Bilinear,

    /// <summary>
    /// Bilinear within each of the two mip levels the surface falls between, blended by how
    /// far it falls between them.
    ///
    /// Bilinear filtering smooths a texture across itself; it says nothing about the seam
    /// between one mip level and the next. A triangle sampled from level 2 meets one sampled
    /// from level 3 along a line where the texture visibly changes sharpness — and because
    /// the level is chosen from the screen footprint, that line moves as the camera does.
    /// Blending the two levels replaces the seam with a gradient.
    ///
    /// Costs a second bilinear tap per pixel wherever the surface falls between levels, so it
    /// is opt-in rather than the default.
    /// </summary>
    Trilinear,
}
