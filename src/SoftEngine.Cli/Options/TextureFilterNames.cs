using SoftEngine.Core.Textures;

namespace SoftEngine.Cli.Options;

internal static class TextureFilterNames
{
    public static bool TryParse(string name, out TextureFiltering filtering)
    {
        switch (name.ToLowerInvariant())
        {
            case "nearest" or "point" or "none":
                filtering = TextureFiltering.Nearest;
                return true;

            case "bilinear" or "linear":
                filtering = TextureFiltering.Bilinear;
                return true;

            case "trilinear":
                filtering = TextureFiltering.Trilinear;
                return true;

            case "anisotropic" or "aniso" or "af":
                filtering = TextureFiltering.Anisotropic;
                return true;

            default:
                filtering = TextureFiltering.Bilinear;
                return false;
        }
    }
}
