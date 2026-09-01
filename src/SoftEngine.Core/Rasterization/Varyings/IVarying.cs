namespace SoftEngine.Core.Rasterization.Varyings;

public interface IVarying<TSelf> where TSelf : struct, IVarying<TSelf>
{
    static abstract TSelf Lerp(in TSelf a, in TSelf b, float t);

    static abstract TSelf Scale(in TSelf a, float f);
}
