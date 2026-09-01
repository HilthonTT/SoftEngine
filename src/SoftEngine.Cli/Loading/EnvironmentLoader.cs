using SoftEngine.Core.Imaging;
using SoftEngine.Core.Textures;

namespace SoftEngine.Cli.Loading;

internal static class EnvironmentLoader
{
    public static CubeMap Load(string path, int resolution)
    {
        var extension = Path.GetExtension(path);

        if (extension.Equals(".hdr", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".pic", StringComparison.OrdinalIgnoreCase))
        {
            return Equirectangular.ToCubeMap(RadianceHdrCodec.Load(path), resolution);
        }

        var (pixels, width, height) = PngCodec.Load(path);

        return Equirectangular.ToCubeMap(new Texture(width, height, pixels), resolution);
    }
}
