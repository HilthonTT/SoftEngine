namespace SoftEngine.Core.Geometry.Primitives;

public static class PrimitiveFactory
{
    private const int Segments = 32;
    private const int Rings = 16;

    public static Mesh Create(PrimitiveShape shape, float size = 1f)
    {
        var half = MathF.Max(size, 1e-4f);
        var full = half * 2f;

        return shape switch
        {
            PrimitiveShape.Plane => new PlaneMesh(full, full, 8, 8),

            PrimitiveShape.Box => new Box(full, full, full),
            PrimitiveShape.UvSphere => new UvSphere(half, Segments, Rings),
            PrimitiveShape.IcoSphere => new IcoSphere(2, half),
            PrimitiveShape.Cylinder => new Cylinder(half, full, Segments),
            PrimitiveShape.Cone => new Cone(half, full, Segments),

            PrimitiveShape.Torus => new Torus(half * 0.8f, half * 0.2f, Segments, Rings),

            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "Unknown primitive shape."),
        };
    }
}
