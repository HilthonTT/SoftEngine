namespace SoftEngine.Core.Tracing;

/// <summary>What the <see cref="PathTracer"/> is allowed to spend, and on what.</summary>
public sealed class TraceSettings
{
    /// <summary>
    /// Paths traced per pixel per call. Noise falls as the square root of this, so the last
    /// halving of the grain costs four times what the first one did.
    /// </summary>
    public int SamplesPerPixel { get; set; } = 16;

    /// <summary>
    /// How many surfaces a path may bounce off before it is abandoned. 0 is direct lighting only —
    /// what the rasterizer computes — 1 adds one bounce of indirect light, which is where colour
    /// bleeding and contact darkening come from, and past about 4 the difference stops being
    /// visible in anything but a white room.
    /// </summary>
    public int MaxBounces { get; set; } = 3;

    /// <summary>
    /// Bounce after which paths start being killed at random, in proportion to how little light
    /// they are still carrying.
    ///
    /// Cutting a path off outright biases the image dark. Killing it with probability <c>1 - p</c>
    /// and dividing the survivors by <c>p</c> costs the same and leaves the average untouched,
    /// which is the whole of Russian roulette.
    /// </summary>
    public int RouletteDepth { get; set; } = 2;

    /// <summary>
    /// Whether paths that escape the scene pick up <see cref="Scenes.Scene.Environment"/>. Off
    /// makes the background — and the light from it — black, which is how you see what the lights
    /// alone are doing.
    /// </summary>
    public bool LightFromEnvironment { get; set; } = true;

    /// <summary>
    /// Scales light arriving straight from a <see cref="Scenes.Lights.ILight"/>.
    ///
    /// <para>
    /// The default is π, and it is not a physical constant here: it is the same exposure correction
    /// <see cref="Rasterization.PbrShader"/> applies to its direct term, so that switching painters
    /// does not change how bright a scene looks. Matching it is what makes a traced frame
    /// comparable with a rasterized one — which is most of what this renderer is for.
    /// </para>
    ///
    /// <para>
    /// Set it to 1 for an image that is internally consistent instead: lights and bounced light on
    /// the same scale, which is the physically correct answer and about three times darker wherever
    /// a light is doing the work.
    /// </para>
    /// </summary>
    public float DirectLightScale { get; set; } = MathF.PI;

    /// <summary>
    /// Whether successive calls average into what is already there rather than replacing it, so a
    /// viewport can refine a still image over many frames. <see cref="PathTracer.Reset"/> starts
    /// again; moving anything resets it anyway, since the geometry it accumulated against is gone.
    /// </summary>
    public bool Accumulate { get; set; }

    /// <summary>
    /// Seeds the sampler. Two runs with the same seed produce the same image down to the last bit,
    /// which is what makes a stochastic renderer testable.
    /// </summary>
    public uint Seed { get; set; } = 0x9E3779B9;

    /// <summary>
    /// How far a bounced ray starts from the surface it left, as a fraction of the distance the
    /// previous ray travelled.
    ///
    /// Scaling it with distance rather than fixing it is what makes one number work across scenes
    /// three orders of magnitude apart in size: the floating-point error in a hit position grows
    /// with how far away it is, and so does this.
    /// </summary>
    public float RayOffset { get; set; } = 1e-4f;
}
