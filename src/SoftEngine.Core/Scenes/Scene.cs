using SoftEngine.Core.Buffers;
using SoftEngine.Core.Scenes.Cameras;
using SoftEngine.Core.Scenes.Projections;

namespace SoftEngine.Core.Scenes;

public sealed class Scene
{
    public ICamera Camera { get; set; } = default!;

    public IWorld World { get; set; } = default!;

    public IProjection Projection { get; set; } = default!;

    public FrameBuffer Surface { get; set; } = default!;

    /// <summary>Distance fog applied by the painters; disabled by default.</summary>
    public FogSettings Fog { get; set; } = new();

    /// <summary>
    /// When true, lit painters shade in linear light and encode to sRGB on output
    /// instead of scaling the sRGB bytes directly. Costs a few table lookups per pixel.
    /// </summary>
    public bool GammaCorrect { get; set; }
}
