namespace SoftEngine.Core.Animation;

/// <summary>
/// How a track gets from one key to the next.
///
/// Collada files this engine reads are sampled, so every curve in one is
/// <see cref="Linear"/>. glTF names the mode per sampler, and the other two are not
/// decoration: <see cref="Step"/> is how a blinking light or a swapped-out prop is authored,
/// and blending it produces a value the animator never wrote; <see cref="CubicSpline"/> is
/// what an exporter emits when it keeps the artist's Bézier handles instead of baking them,
/// and reading it as linear turns every eased motion into a sequence of constant-speed
/// segments with a visible corner at each key.
/// </summary>
public enum TrackInterpolation
{
    /// <summary>Straight line between neighbouring keys.</summary>
    Linear,

    /// <summary>Hold the earlier key until the later one is reached.</summary>
    Step,

    /// <summary>Cubic Hermite through the keys, shaped by a tangent on each side of each one.</summary>
    CubicSpline,
}
