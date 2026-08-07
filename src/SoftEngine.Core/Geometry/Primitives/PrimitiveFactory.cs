namespace SoftEngine.Core.Geometry.Primitives;

/// <summary>
/// Builds any <see cref="PrimitiveShape"/> at a common size, so a front-end offering all of them
/// in one menu does not have to know each one's own parameters.
///
/// <para>
/// The primitives take radii, heights and tube widths, and no two of them mean the same thing by
/// the number 1: a cone of radius 1 is twice as wide as a torus of major radius 1, and a plane of
/// width 1 is half of either. This is where that is reconciled — every shape here is built to fit
/// the same cube, so a menu of them produces objects of the same visual weight.
/// </para>
/// </summary>
public static class PrimitiveFactory
{
    /// <summary>How finely the round shapes are divided. A software rasterizer pays for every
    /// triangle, so these are smooth enough to read as curved and no smoother.</summary>
    private const int Segments = 32;
    private const int Rings = 16;

    /// <summary>
    /// Builds a shape reaching <paramref name="size"/> from its own centre along its widest axis
    /// — so every shape fits inside a cube of side <c>2 × size</c>, whatever it is. A sphere's
    /// radius, half a box's edge, half a cylinder's height.
    /// </summary>
    /// <param name="shape">Which shape to build.</param>
    /// <param name="size">Half the extent of the shape, in world units. Clamped above zero.</param>
    public static Mesh Create(PrimitiveShape shape, float size = 1f)
    {
        // Degenerate geometry does not fail loudly — it renders as nothing at all, which reads
        // as the add having silently done nothing rather than as a bad number.
        var half = MathF.Max(size, 1e-4f);
        var full = half * 2f;

        return shape switch
        {
            // Subdivided rather than the two triangles a flat sheet needs: the default painter
            // lights per vertex, and a four-cornered floor under a point light is lit at four
            // points. The rest of the grid is there for the shading, not for the shape.
            PrimitiveShape.Plane => new PlaneMesh(full, full, 8, 8),

            PrimitiveShape.Box => new Box(full, full, full),
            PrimitiveShape.UvSphere => new UvSphere(half, Segments, Rings),
            PrimitiveShape.IcoSphere => new IcoSphere(2, half),
            PrimitiveShape.Cylinder => new Cylinder(half, full, Segments),
            PrimitiveShape.Cone => new Cone(half, full, Segments),

            // Split 4:1 between the ring and the tube, which is the proportion that reads as a
            // torus rather than as a washer or a doughnut, and still reaches exactly `half`.
            PrimitiveShape.Torus => new Torus(half * 0.8f, half * 0.2f, Segments, Rings),

            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "Unknown primitive shape."),
        };
    }
}
