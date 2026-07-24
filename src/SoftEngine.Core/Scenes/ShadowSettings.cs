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

    /// <summary>How dark a fully shadowed surface goes: 1 removes the light entirely, 0 disables shadowing.</summary>
    public float Strength { get; set; } = 1f;
}
