namespace SoftEngine.Core.Baking;

/// <summary>What an <see cref="IrradianceBaker"/> run is allowed to spend, and on what.</summary>
public sealed class BakeSettings
{
    private int _resolution = 12;
    private int _rays = 128;

    /// <summary>
    /// Probes along the world's longest axis; the other two get proportionally fewer, so cells come
    /// out roughly cubic whatever shape the scene is. Clamped to 2…64.
    ///
    /// Cost is the cube of this. It is also the resolution at which indirect light can vary, and
    /// indirect light varies slowly — a corner darkening over a metre is what a probe grid is for,
    /// and a shadow with an edge is not.
    /// </summary>
    public int Resolution
    {
        get => _resolution;
        set => _resolution = System.Math.Clamp(value, 2, 64);
    }

    /// <summary>
    /// Paths traced out of each probe. Noise falls as the square root of this, and unlike a noisy
    /// image a noisy probe does not look like noise: it looks like the ambient light being slightly
    /// the wrong colour in one part of the room.
    /// </summary>
    public int Rays
    {
        get => _rays;
        set => _rays = System.Math.Max(1, value);
    }

    /// <summary>
    /// How many further surfaces each path may bounce off. 0 stores light that has bounced exactly
    /// once — off the surface the ray hit, which is already most of what a bake buys — and each
    /// bounce after that fills in light that has been around a corner one more time.
    /// </summary>
    public int Bounces { get; set; } = 2;

    /// <summary>
    /// Margin added around the world's bounds before the grid is laid out, as a fraction of its
    /// longest axis.
    ///
    /// Without it the outermost probes sit exactly on the geometry — half inside it, in practice —
    /// and a surface on the boundary is blended from probes that are all buried. It also gives a
    /// flat scene some thickness to interpolate through.
    /// </summary>
    public float Padding { get; set; } = 0.05f;

    /// <summary>
    /// Fraction of a probe's rays that may end on the back of a surface before the probe is called
    /// buried and dropped from the blend.
    ///
    /// It is a fraction rather than a yes/no because a probe just above a floor legitimately sees
    /// the back of nothing, one in a doorway sees a few, and one inside a wall sees them in every
    /// direction. A closed room's probes see backfaces the whole way round too — which is why the
    /// test is against the <em>near</em> side of what it hits, not against being enclosed.
    /// </summary>
    public float InsideThreshold { get; set; } = 0.6f;

    /// <summary>
    /// Scales every probe on the way out.
    ///
    /// There is deliberately no equivalent of <see cref="Scenes.Scene.AmbientIntensity"/>'s 0.35
    /// here. That number exists because the sky's own brightness is not what a surface facing it
    /// receives, and a bake answers that question by measuring it instead of guessing — so the
    /// default is 1, and turning this knob is admitting to an exposure preference.
    /// </summary>
    public float Intensity { get; set; } = 1f;

    /// <summary>
    /// Ceiling on a single path's contribution, or 0 for none.
    ///
    /// One path that finds a light through a specular bounce can carry hundreds of times what its
    /// neighbours do, and averaged over a few hundred rays it is still enough to make one probe
    /// visibly brighter than the ones beside it. Clamping removes that at the cost of losing the
    /// energy it stood for; the honest fix is more rays, and this is the cheap one.
    /// </summary>
    public float MaxRadiance { get; set; }

    /// <summary>Whether paths that escape the world pick up the scene's environment.</summary>
    public bool LightFromEnvironment { get; set; } = true;

    /// <summary>
    /// Scales light arriving straight from a light, exactly as
    /// <see cref="Tracing.TraceSettings.DirectLightScale"/> does — and it must match, since what is
    /// baked here is that light after it has bounced, and it is added to a frame the rasterizer lit
    /// with the same lights at the same exposure.
    /// </summary>
    public float DirectLightScale { get; set; } = MathF.PI;

    /// <summary>
    /// Seeds the sampler. Two bakes of the same world with the same seed produce the same probes to
    /// the last bit, whatever order the threads happened to run in.
    /// </summary>
    public uint Seed { get; set; } = 0x9E3779B9;
}
