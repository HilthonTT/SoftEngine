using SoftEngine.Core.Rasterization;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Textures;

namespace SoftEngine.Cli.Rendering;

/// <summary>The painter a <c>--painter</c> name asks for.</summary>
internal static class PainterCatalog
{
    public static IPainter? Create(string name, TextureFiltering filtering)
    {
        // Filtering is on for every painter that samples a texture: a still image has no shimmer to
        // trade away, so there is nothing to gain by turning it off and detail to lose. Mip maps go
        // with it — a nearest fill was asked for the unfiltered image, chain and all.
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
