namespace SoftEngine.Core.Pipeline.PostProcess;

public interface IPostEffect
{
    string Name { get; }

    bool Enabled { get; set; }

    bool NeedsDepth => false;

    bool NeedsReflectance => false;

    bool NeedsAmbient => false;

    void Apply(PostProcessTarget target);
}
