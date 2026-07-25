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

    void Apply(PostProcessTarget target);
}
