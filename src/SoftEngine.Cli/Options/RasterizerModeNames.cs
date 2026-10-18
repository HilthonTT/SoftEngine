using SoftEngine.Core.Rasterization;

namespace SoftEngine.Cli.Options;

internal static class RasterizerModeNames
{
    public static bool TryParse(string name, out RasterizerMode mode)
    {
        switch (name.ToLowerInvariant())
        {
            case "scanline" or "span" or "spans":
                mode = RasterizerMode.Scanline;
                return true;

            case "half-space" or "halfspace" or "block" or "blocks":
                mode = RasterizerMode.HalfSpace;
                return true;

            default:
                mode = RasterizerMode.Scanline;
                return false;
        }
    }
}
