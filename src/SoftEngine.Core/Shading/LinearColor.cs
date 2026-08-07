using SoftEngine.Core.Diagnostics;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Shading;

/// <summary>
/// A colour in linear light, three floats per channel, with no upper bound.
///
/// This is what a pixel shader produces. <see cref="ColorRGB"/> — packed sRGB bytes — is
/// what a texture stores and what the display eventually gets, and it cannot represent a
/// highlight brighter than white: a specular glint five times the intensity of paper white
/// and one exactly at it both encode to 255, and no amount of tone mapping afterwards can
/// tell them apart again. Keeping the shader's answer in linear floats until the very end
/// of the frame is what lets <see cref="Pipeline.PostProcess.BloomEffect"/> find the parts
/// of the image that are actually bright and <see cref="Pipeline.PostProcess.ToneMapEffect"/>
/// compress a range instead of re-expanding a clipped one.
///
/// The implicit conversion from <see cref="ColorRGB"/> decodes sRGB, so a shader that has
/// no HDR range to give — a solid colour, a raw texel — still compiles and still lands in
/// the right space. The round trip through the two lookup tables is stable to the byte.
/// </summary>
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public readonly struct LinearColor(float r, float g, float b)
{
    public readonly float R = r;
    public readonly float G = g;
    public readonly float B = b;

    /// <summary>Decodes packed sRGB bytes to linear light in [0, 1].</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator LinearColor(ColorRGB color) =>
        new(ColorSpace.ToLinear(color.R), ColorSpace.ToLinear(color.G), ColorSpace.ToLinear(color.B));

    /// <summary>Encodes back to sRGB bytes, clamping anything above white.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ColorRGB ToColorRGB() => new(ColorSpace.ToSrgb(R), ColorSpace.ToSrgb(G), ColorSpace.ToSrgb(B));

    /// <summary>Rec. 709 luminance, the weighting used by bloom's bright pass and FXAA.</summary>
    public float Luminance => 0.2126f * R + 0.7152f * G + 0.0722f * B;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static LinearColor operator *(float f, LinearColor color) =>
        new(f * color.R, f * color.G, f * color.B);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static LinearColor operator *(LinearColor color, float f) =>
        new(f * color.R, f * color.G, f * color.B);

    /// <summary>Adds light to light — no saturation, which is the whole point of the type.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static LinearColor operator +(LinearColor x, LinearColor y) =>
        new(x.R + y.R, x.G + y.G, x.B + y.B);

    /// <summary>
    /// Per-channel product — light falling on a surface times what the surface reflects.
    /// A red light on a blue surface goes black, which is the answer.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static LinearColor operator *(LinearColor x, LinearColor y) =>
        new(x.R * y.R, x.G * y.G, x.B * y.B);

    /// <summary>
    /// Modulates sRGB bytes by this colour channel-for-channel, saturating at 255.
    ///
    /// This is the naive shading path — the one <see cref="Scenes.Scene.GammaCorrect"/>
    /// turns off — where an intensity scales the encoded bytes directly instead of the
    /// light they stand for. It is kept because it is what the engine did before linear
    /// shading existed, and switching between the two side by side is the clearest
    /// demonstration of why the encoded version darkens midtones.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ColorRGB ScaleBytes(ColorRGB color) => new(
        Saturate(R * color.R),
        Saturate(G * color.G),
        Saturate(B * color.B));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte Saturate(float channel) => (byte)System.Math.Clamp(channel, 0f, 255f);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static LinearColor Lerp(LinearColor from, LinearColor to, float t) =>
        new(from.R + (to.R - from.R) * t,
            from.G + (to.G - from.G) * t,
            from.B + (to.B - from.B) * t);

    public static readonly LinearColor Black = new(0f, 0f, 0f);

    /// <summary>Paper white — the brightest colour an 8-bit target can hold, and the reference for everything above it.</summary>
    public static readonly LinearColor White = new(1f, 1f, 1f);
}
