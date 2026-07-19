namespace SoftEngine.Core.Diagnostics;

public sealed class ColorRGB
{
    private const int ARGBAlphaShift = 24;
    private const int ARGBRedShift = 16;
    private const int ARGBGreenShift = 8;
    private const int ARGBBlueShift = 0;

    private readonly long _value;

    public ColorRGB(byte r, byte g, byte b)
    {
        _value = (unchecked((uint)(r << ARGBRedShift | g << ARGBGreenShift | b << ARGBBlueShift | 255 << ARGBAlphaShift))) & 0xffffffff;
    }

    public byte R => (byte)((_value >> ARGBRedShift) & 0xFF);

    public byte G => (byte)((_value >> ARGBGreenShift) & 0xFF);

    public byte B => (byte)((_value >> ARGBBlueShift) & 0xFF);

    public int Color => (int)_value;

    public static ColorRGB operator *(float f, ColorRGB color) =>
        new((byte)(f * color.R), (byte)(f * color.G), (byte)(f * color.B));

    public static readonly ColorRGB Yellow = new(255, 255, 0);

    public static readonly ColorRGB Blue = new(0, 0, 255);

    public static readonly ColorRGB Gray = new(127, 127, 127);

    public static readonly ColorRGB Green = new(0, 255, 0);

    public static readonly ColorRGB Red = new(255, 0, 0);

    public static readonly ColorRGB Magenta = new(255, 0, 255);
}
