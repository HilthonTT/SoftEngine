namespace SoftEngine.Core.Pipeline.Debugging;

/// <summary>
/// Which of the frame's own buffers to present instead of the shaded image.
///
/// A renderer produces far more per frame than the picture: a depth per pixel, a count of
/// how many times each was written, a second depth buffer taken from the light. All of it is
/// discarded at present time, and all of it is what you actually need when the picture is
/// wrong — a shadow that lands in the wrong place is a shadow map you have never seen, and a
/// frame that is slow for no visible reason is overdraw you have never measured.
/// </summary>
public enum DebugView
{
    /// <summary>The shaded image, as normal.</summary>
    Off,

    /// <summary>Distance from the eye, auto-ranged over the geometry actually on screen.</summary>
    Depth,

    /// <summary>Surface orientation, reconstructed from the depth buffer and encoded as colour.</summary>
    Normals,

    /// <summary>How many times each pixel was written, as a heat map.</summary>
    Overdraw,

    /// <summary>The shadow-map depth buffer, drawn as the light sees it.</summary>
    ShadowMap,
}
