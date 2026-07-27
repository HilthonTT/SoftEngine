using SoftEngine.Core.Shading;

namespace SoftEngine.Core.Scenes;

/// <summary>
/// Shadow mapping for a scene's first light. The renderer draws the world's depth from
/// the light's point of view into an off-screen buffer before the main pass; lit painters
/// then compare each shaded point's distance to the light against it.
/// Disabled by default — the extra pass costs roughly one depth-only render of the world.
/// </summary>
public sealed class ShadowSettings
{
    private int _resolution = 1024;

    public bool Enabled { get; set; }

    /// <summary>
    /// Side length of the (square) shadow map. Bigger maps resolve finer contact shadows
    /// but cost quadratically more to fill; clamped to 64…8192.
    /// </summary>
    public int Resolution
    {
        get => _resolution;
        set => _resolution = System.Math.Clamp(value, 64, 8192);
    }

    /// <summary>
    /// Constant depth offset applied before the comparison, measured in shadow-map texels
    /// of depth. Counteracts self-shadowing ("shadow acne"): a texel's single depth stands
    /// in for a whole quad of surface, so a surface tests against its own quantized self.
    ///
    /// The unit matters. A bias in raw normalized depth would mean something different in
    /// a 2-unit scene than in a 1500-unit one, and would have to be retuned for every world
    /// and every resolution; one texel of depth is the same amount of error everywhere.
    /// </summary>
    public float DepthBias { get; set; } = 1.5f;

    /// <summary>
    /// Extra bias proportional to how obliquely the light hits the surface, in the same
    /// texel units. A steeply lit surface spreads far more depth across one texel than a
    /// face-on one, so a constant bias is either useless at grazing angles or large enough
    /// to detach every shadow from the object casting it.
    /// </summary>
    public float SlopeBias { get; set; } = 2.5f;

    /// <summary>Averages a 3×3 neighbourhood of the map (PCF), trading a little speed for softer edges.</summary>
    public bool SoftFilter { get; set; } = true;

    /// <summary>
    /// How many slices of the camera's view distance get a depth buffer of their own, from 1
    /// (a single map over the whole world, which is what the engine did before cascades) to
    /// <see cref="ShadowMap.MaxCascades"/>.
    ///
    /// One map spends its resolution uniformly over the scene, which puts the texels where
    /// they do least good: perspective makes a shadow ten units away cover a hundred times the
    /// pixels of one five hundred units away, and both get the same number of texels. Each
    /// extra cascade costs one more depth-only pass — over fewer casters, though, since a
    /// cascade only rasterizes what can reach the slice it covers.
    ///
    /// Cascades need to know where the camera is looking, so a pass rendered without a view
    /// (the standalone shadow-map API, used by tests) falls back to a single map however many
    /// this asks for.
    /// </summary>
    public int CascadeCount
    {
        get => _cascadeCount;
        set => _cascadeCount = System.Math.Clamp(value, 1, ShadowMap.MaxCascades);
    }

    private int _cascadeCount = 1;

    /// <summary>
    /// How the view distance is divided between the cascades, from 0 (evenly by distance) to 1
    /// (evenly by ratio — each slice a fixed multiple of the one before it).
    ///
    /// Neither extreme is right. Splitting evenly by distance gives the near slice, where the
    /// pixels are, the same span as the far one; splitting by ratio alone gives it a span so
    /// small that the second cascade's edge lands in the middle of the frame. The usual
    /// practice is a blend weighted toward the ratio, which is what this defaults to.
    /// </summary>
    public float SplitBlend
    {
        get => _splitBlend;
        set => _splitBlend = System.Math.Clamp(value, 0f, 1f);
    }

    private float _splitBlend = 0.8f;

    /// <summary>
    /// How far from the camera shadows are drawn at all, or 0 to use the projection's own far
    /// plane. Fitting the cascades to a distance nothing is legible at anyway is the single
    /// cheapest way to make the near ones sharper.
    /// </summary>
    public float MaxDistance { get; set; }

    /// <summary>How dark a fully shadowed surface goes: 1 removes the light entirely, 0 disables shadowing.</summary>
    public float Strength { get; set; } = 1f;
}
