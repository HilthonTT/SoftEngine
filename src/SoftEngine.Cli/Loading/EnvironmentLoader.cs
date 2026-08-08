using SoftEngine.Core.Imaging;
using SoftEngine.Core.Textures;

namespace SoftEngine.Cli.Loading;

/// <summary>
/// Turns a panorama on disk into the cube map the renderer lights with.
///
/// Resolving a path and guessing a format from its extension is a question about the machine the
/// program is run on, which is why it lives out here rather than in the engine — the same reason
/// <see cref="Core.Scenes.Serialization.WorldSource"/> is stored and never interpreted.
/// </summary>
internal static class EnvironmentLoader
{
    /// <summary>
    /// Loads <paramref name="path"/> as an environment. A <c>.hdr</c> keeps its range; a PNG is
    /// projected as it is, which costs the reflections their highlights and is all an 8-bit
    /// panorama had to give.
    /// </summary>
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
