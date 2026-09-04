namespace SoftEngine.Core.Rasterization.Varyings;

public interface IVarying<TSelf> where TSelf : struct, IVarying<TSelf>
{
    static abstract TSelf Lerp(in TSelf a, in TSelf b, float t);

    static abstract TSelf Scale(in TSelf a, float f);

    /// <summary>
    /// Sum of two values. A varying is linear across a triangle once divided by w, so a fill that
    /// knows how much it changes from one pixel to the next can step it with this instead of
    /// interpolating afresh at every pixel.
    /// </summary>
    static abstract TSelf Add(in TSelf a, in TSelf b);

    /// <summary>
    /// Weighted sum of all three vertices. The weights are barycentric when a pixel is being
    /// evaluated, and sum to zero when what is wanted is the gradient between neighbouring pixels,
    /// so nothing here may assume they sum to one. Fuse the three terms rather than composing
    /// <see cref="Scale"/> and <see cref="Add"/>: this runs once per block and per gradient.
    /// </summary>
    static abstract TSelf Combine(in TSelf a, in TSelf b, in TSelf c, float w0, float w1, float w2);
}
