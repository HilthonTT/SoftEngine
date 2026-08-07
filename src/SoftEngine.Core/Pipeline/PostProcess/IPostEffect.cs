namespace SoftEngine.Core.Pipeline.PostProcess;

/// <summary>
/// One full-screen pass over the finished image. Effects are applied in the order the
/// stack holds them, each reading and writing <see cref="PostProcessTarget.Color"/>.
/// </summary>
public interface IPostEffect
{
    /// <summary>Short name for the stats overlay and the graphics event list.</summary>
    string Name { get; }

    /// <summary>When false the stack skips the effect without removing it, so a UI can toggle it.</summary>
    bool Enabled { get; set; }

    /// <summary>
    /// Whether the effect reads <see cref="PostProcessTarget.ViewDepth"/>. Reading back the
    /// depth buffer and converting it costs a full-screen pass of its own, so the stack only
    /// does it when something enabled has asked for it.
    /// </summary>
    bool NeedsDepth => false;

    /// <summary>
    /// Whether the effect reads <see cref="PostProcessTarget.Reflectance"/>. Unlike depth,
    /// which the frame produced anyway, reflectance is only recorded because something asked:
    /// the answer here is what turns the rasterizer's per-pixel record on, so it has to be
    /// true before the frame is drawn rather than by the time the stack runs.
    /// </summary>
    bool NeedsReflectance => false;

    void Apply(PostProcessTarget target);
}
