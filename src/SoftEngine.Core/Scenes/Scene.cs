using SoftEngine.Core.Buffers;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Scenes.Cameras;
using SoftEngine.Core.Scenes.Projections;
using SoftEngine.Core.Shading;
using SoftEngine.Core.Textures;

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

    /// <summary>
    /// When true, the frame is rasterized into an unbounded linear float target rather
    /// than 8-bit sRGB, so highlights brighter than white survive to the post-process
    /// stack. See <see cref="FrameBuffer.SetHighDynamicRange"/>.
    ///
    /// It only buys anything with <see cref="GammaCorrect"/> on: that is the path where
    /// the shaders produce light rather than pre-encoded bytes, and so the only one with
    /// a range above white to keep.
    /// </summary>
    public bool HighDynamicRange { get; set; }

    /// <summary>
    /// What surrounds the scene. Drawn behind everything as a skybox, and — unless
    /// <see cref="AmbientFromEnvironment"/> says otherwise — reduced to the ambient light
    /// the painters use, so a scene under a blue sky over brown ground is lit from above
    /// by the one and from below by the other instead of by a flat grey constant.
    ///
    /// Null (the default) leaves the background cleared and ambient a constant, as before.
    /// </summary>
    public CubeMap? Environment { get; set; }

    /// <summary>Whether <see cref="Environment"/> is drawn as a background. Off leaves it lighting the scene invisibly.</summary>
    public bool ShowSky { get; set; } = true;

    /// <summary>Brightness of the drawn sky. Above 1 needs <see cref="HighDynamicRange"/> to mean anything.</summary>
    public float SkyIntensity { get; set; } = 1f;

    /// <summary>Whether the painters take their ambient light from <see cref="Environment"/>.</summary>
    public bool AmbientFromEnvironment { get; set; } = true;

    /// <summary>
    /// Scales the ambient light taken from the environment. The environment's own
    /// brightness is what the sky looks like, which is rarely what a surface facing it
    /// should receive; this is the knob between the two.
    /// </summary>
    public float AmbientIntensity { get; set; } = 0.35f;

    /// <summary>
    /// Indirect light measured ahead of time by <see cref="Baking.IrradianceBaker"/>, or null for
    /// the ambient term the engine has always used.
    ///
    /// When set it <em>replaces</em> <see cref="Environment"/>'s contribution to the ambient rather
    /// than adding to it — the bake already saw the sky, and counting it twice would be brighter
    /// than either answer. The environment still draws as the sky and still feeds the PBR painter's
    /// reflections, which are a different question about the same map.
    ///
    /// Two renderers ignore it, for opposite reasons. The <see cref="Tracing.PathTracer"/> computes
    /// the thing this is a measurement of. The GPU backend cannot: six uniforms hold a cube and not
    /// a grid, so it keeps lighting the frame with the environment.
    /// </summary>
    public Shading.IrradianceVolume? Irradiance { get; set; }
}
