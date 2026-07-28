using SoftEngine.Core.Rasterization;
using SoftEngine.Core.Rasterization.Painters;

namespace SoftEngine.Gpu;

/// <summary>
/// Maps the front-end's choice of painter onto the shader that reproduces it.
///
/// <para>
/// The GPU backend is selected in place of the software rasterizer, not in place of the
/// shading model: whichever mode the viewer's radio buttons are on has to keep meaning the
/// same thing. Since the painter is what those buttons set, the painter is what decides the
/// shader — and a painter this does not recognize falls back to Gouraud rather than to
/// nothing, so a scene still renders.
/// </para>
/// </summary>
public static class GpuShading
{
    public static GpuShadingMode From(IPainter? painter) => painter switch
    {
        null => GpuShadingMode.None,
        PbrPainter => GpuShadingMode.PhysicallyBased,
        MaterialPainter => GpuShadingMode.Material,
        TexturedPainter => GpuShadingMode.Textured,
        PhongPainter => GpuShadingMode.Phong,
        GouraudPainter => GpuShadingMode.Gouraud,
        FlatPainter => GpuShadingMode.Flat,
        ClassicPainter => GpuShadingMode.Classic,
        WireFramePainter => GpuShadingMode.Classic,
        _ => GpuShadingMode.Gouraud,
    };

    /// <summary>Whether the mode reads any of a mesh's texture maps.</summary>
    public static bool UsesTextures(this GpuShadingMode mode) =>
        mode is GpuShadingMode.Textured or GpuShadingMode.Material or GpuShadingMode.PhysicallyBased;

    /// <summary>Whether the mode reads a tangent frame — that is, whether it can normal-map.</summary>
    public static bool UsesTangents(this GpuShadingMode mode) =>
        mode is GpuShadingMode.Material or GpuShadingMode.PhysicallyBased;
}
