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

    void Apply(PostProcessTarget target);
}
