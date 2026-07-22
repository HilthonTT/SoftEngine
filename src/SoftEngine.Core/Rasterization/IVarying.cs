namespace SoftEngine.Core.Rasterization;

/// <summary>
/// Per-vertex data interpolated across a triangle. Implementations must be structs
/// so the JIT can devirtualize the static abstract Lerp/Scale calls.
/// </summary>
public interface IVarying<TSelf> where TSelf : struct, IVarying<TSelf>
{
    static abstract TSelf Lerp(in TSelf a, in TSelf b, float t);

    /// <summary>Multiplies every component by <paramref name="f"/> (used for the perspective divide).</summary>
    static abstract TSelf Scale(in TSelf a, float f);
}
