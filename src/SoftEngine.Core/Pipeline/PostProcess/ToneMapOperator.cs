namespace SoftEngine.Core.Pipeline.PostProcess;

public enum ToneMapOperator
{
    /// <summary>c / (1 + c) — the simplest sensible curve; desaturates highlights gently.</summary>
    Reinhard,

    /// <summary>A curve fitted to the ACES filmic tone-map: darker shadows and a harder shoulder.</summary>
    Aces,
}
