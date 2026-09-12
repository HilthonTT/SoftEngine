namespace SoftEngine.Core.Shading;

public readonly struct SurfaceSample
{
    public SurfaceSample(LinearColor ambient, SurfaceReflectance reflectance)
    {
        Ambient = ambient;
        Reflectance = reflectance;
    }

    public LinearColor Ambient { get; }

    public SurfaceReflectance Reflectance { get; }

    public static SurfaceSample None => default;
}
