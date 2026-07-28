namespace SoftEngine.Gpu;

/// <summary>
/// Which branch of the scene fragment shader a frame takes. The values are shared with
/// <c>scene.frag</c>'s <c>MODE_</c> constants and must not be renumbered independently.
/// </summary>
public enum GpuShadingMode
{
    /// <summary>Nothing is drawn — the null painter, which on the CPU fills no pixels either.</summary>
    None = 0,

    Classic = 1,
    Flat = 2,
    Gouraud = 3,
    Phong = 4,
    Textured = 5,
    Material = 6,
    PhysicallyBased = 7,
}
