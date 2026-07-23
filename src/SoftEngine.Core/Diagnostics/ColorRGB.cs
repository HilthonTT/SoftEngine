using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Diagnostics;

/// <summary>
/// A packed 32-bit ARGB colour. A value type so it can be produced per pixel in the
/// shader hot path without allocating on the managed heap.
/// </summary>
public readonly struct ColorRGB
{
    private const int ARGBAlphaShift = 24;
    private const int ARGBRedShift = 16;
    private const int ARGBGreenShift = 8;
    private const int ARGBBlueShift = 0;

    private readonly uint _value;

    public ColorRGB(byte r, byte g, byte b)
    {
        _value = unchecked((uint)(
            r << ARGBRedShift |
            g << ARGBGreenShift |
            b << ARGBBlueShift |
            255 << ARGBAlphaShift));
    }

    private ColorRGB(uint packed)
    {
        _value = packed;
    }

    /// <summary>Wraps a packed 32-bit ARGB value (e.g. a texture texel) without re-encoding.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ColorRGB FromPacked(int argb) => new(unchecked((uint)argb));

    public byte R => (byte)((_value >> ARGBRedShift) & 0xFF);

    public byte G => (byte)((_value >> ARGBGreenShift) & 0xFF);

    public byte B => (byte)((_value >> ARGBBlueShift) & 0xFF);

    public int Color => (int)_value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ColorRGB operator *(float f, ColorRGB color) =>
        new((byte)System.Math.Clamp(f * color.R, 0f, 255f),
            (byte)System.Math.Clamp(f * color.G, 0f, 255f),
            (byte)System.Math.Clamp(f * color.B, 0f, 255f));

    /// <summary>Saturating add — channels clamp at 255 instead of wrapping.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ColorRGB operator +(ColorRGB x, ColorRGB y) =>
        new((byte)System.Math.Min(255, x.R + y.R),
            (byte)System.Math.Min(255, x.G + y.G),
            (byte)System.Math.Min(255, x.B + y.B));

    public static readonly ColorRGB White = new(255, 255, 255);

    public static readonly ColorRGB Yellow = new(255, 255, 0);

    public static readonly ColorRGB Blue = new(0, 0, 255);

    public static readonly ColorRGB Gray = new(127, 127, 127);

    public static readonly ColorRGB Green = new(0, 255, 0);

    public static readonly ColorRGB Red = new(255, 0, 0);

    public static readonly ColorRGB Magenta = new(255, 0, 255);
}
