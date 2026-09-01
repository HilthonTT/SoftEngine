using SoftEngine.Core.Rasterization;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Textures;

namespace SoftEngine.Cli.Rendering;

internal static class PainterCatalog
{
    public static IPainter? Create(string name, TextureFiltering filtering)
    {
        var mipMaps = filtering != TextureFiltering.Nearest;

        switch (name.ToLowerInvariant())
        {
            case "none":
                return null;

            case "classic":
                return new ClassicPainter();

            case "flat":
                return new FlatPainter();

            case "phong":
                return new PhongPainter();

            case "textured":
                return new TexturedPainter { Filtering = filtering, UseMipMaps = mipMaps };

            case "material":
                return new MaterialPainter { Filtering = filtering, UseMipMaps = mipMaps };

            case "pbr" or "physicallybased":
                return new PbrPainter { Filtering = filtering, UseMipMaps = mipMaps };

            default:
                return new GouraudPainter();
        }
    }
}
