using SoftEngine.Core.Pipeline.Debugging;

namespace SoftEngine.Cli.Rendering;

/// <summary>
/// The spellings of <see cref="DebugView"/> a <c>--view</c> may use.
///
/// The names people type are not always the enum's. "occlusion" and "mip" are what the usage text
/// has always offered, and a flag that documents one spelling and accepts another is a worse
/// failure than an unknown view — it looks like the view is broken.
/// </summary>
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
