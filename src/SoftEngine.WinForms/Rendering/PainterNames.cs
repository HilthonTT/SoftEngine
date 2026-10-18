using SoftEngine.Core.Rasterization;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Textures;

namespace SoftEngine.WinForms.Rendering;

internal static class PainterNames
{
    public static PainterKind KindOf(IPainter? painter) => painter switch
    {
        ClassicPainter => PainterKind.Classic,
        FlatPainter => PainterKind.Flat,
        GouraudPainter => PainterKind.Gouraud,
        PhongPainter => PainterKind.Phong,
        PbrPainter => PainterKind.Pbr,
        MaterialPainter => PainterKind.Material,
        TexturedPainter => PainterKind.Textured,
        null => PainterKind.None,
        _ => PainterKind.Gouraud,
    };

    /// <summary>The name a painter is written to a scene file under.</summary>
    public static string NameOf(IPainter? painter) => KindOf(painter).ToString();

    // An unrecognised name falls back to Gouraud rather than throwing: a scene written by a
    // build with a painter this one does not have should still open.
    public static PainterKind Parse(string? name) => name?.ToLowerInvariant() switch
    {
        "none" => PainterKind.None,
        "classic" => PainterKind.Classic,
        "flat" => PainterKind.Flat,
        "phong" => PainterKind.Phong,
        "textured" => PainterKind.Textured,
        "material" => PainterKind.Material,
        "pbr" or "physicallybased" => PainterKind.Pbr,
        _ => PainterKind.Gouraud,
    };

    public static IPainter? Create(PainterKind kind) => kind switch
    {
        PainterKind.None => null,
        PainterKind.Classic => new ClassicPainter(),
        PainterKind.Flat => new FlatPainter(),
        PainterKind.Phong => new PhongPainter(),
        PainterKind.Textured => new TexturedPainter(),
        PainterKind.Material => new MaterialPainter(),
        PainterKind.Pbr => new PbrPainter(),
        _ => new GouraudPainter(),
    };

    public static TextureFiltering Filtering(bool filtering, bool trilinear) => (filtering, trilinear) switch
    {
        (true, true) => TextureFiltering.Trilinear,
        (true, false) => TextureFiltering.Bilinear,
        _ => TextureFiltering.Nearest,
    };
}
