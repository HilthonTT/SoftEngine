using SoftEngine.Core.Buffers;
using SoftEngine.Core.Scenes.Cameras;
using SoftEngine.Core.Scenes.Projections;
using SoftEngine.Core.Shading;

namespace SoftEngine.Core.Scenes;

public sealed class Scene
{
    public ICamera Camera { get; set; } = default!;

    public IWorld World { get; set; } = default!;

    public IProjection Projection { get; set; } = default!;

    public FrameBuffer Surface { get; set; } = default!;

    /// <summary>Distance fog applied by the painters; disabled by default.</summary>
    public FogSettings Fog { get; set; } = new();

    /// <summary>Shadow mapping for the world's first light; disabled by default.</summary>
    public ShadowSettings Shadows { get; set; } = new();

    /// <summary>
    /// The shadow map the renderer filled for this frame, or null when shadows are off or
    /// the world casts none. Set by the renderer before it prepares the painter, and read
    /// by lit painters for the rest of the frame.
    /// </summary>
    public ShadowMap? ShadowMap { get; set; }

    /// <summary>
    /// When true, lit painters shade in linear light and encode to sRGB on output
    /// instead of scaling the sRGB bytes directly. Costs a few table lookups per pixel.
    /// </summary>
    public bool GammaCorrect { get; set; }
}
