using SoftEngine.Core.Rasterization;
using SoftEngine.Core.Rasterization.Painters;

namespace SoftEngine.Gpu;

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

    public static bool UsesTextures(this GpuShadingMode mode) =>
        mode is GpuShadingMode.Textured or GpuShadingMode.Material or GpuShadingMode.PhysicallyBased;

    public static bool UsesTangents(this GpuShadingMode mode) =>
        mode is GpuShadingMode.Material or GpuShadingMode.PhysicallyBased;
}
