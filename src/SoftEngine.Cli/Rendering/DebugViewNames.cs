using SoftEngine.Core.Pipeline.Debugging;

namespace SoftEngine.Cli.Rendering;

internal static class DebugViewNames
{
    public static bool TryParse(string name, out DebugView view)
    {
        var named = name.Trim().ToLowerInvariant() switch
        {
            "occlusion" => nameof(DebugView.OcclusionBuffer),
            "mip" or "mips" or "mipmap" => nameof(DebugView.MipLevel),
            "shadow" => nameof(DebugView.ShadowMap),
            _ => name,
        };

        return Enum.TryParse(named, ignoreCase: true, out view);
    }
}
