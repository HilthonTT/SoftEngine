using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Scenes.Lights;
using System.Numerics;

namespace SoftEngine.Core.Gizmos;

/// <summary>
/// Draws the scene's lights as wireframe markers.
///
/// <para>
/// A light has no geometry, which makes it the one thing in a scene that cannot be seen — only its
/// effects can. That is fine until something is wrong with it, at which point the question "is the
/// spot pointing where I think it is" has no way to be answered except by moving it and watching
/// what changes. Every light here draws where it is, which way it faces and how far it reaches, in
/// the same wireframe the grid and the skeleton are drawn in.
/// </para>
///
/// <para>
/// Sizes are given in world units by the caller rather than derived: the scenes this engine ships
/// with span three orders of magnitude, and a marker sized for one is invisible in another or
/// swallows it. The front-end already computes a reference distance for exactly this reason.
/// </para>
/// </summary>
public static class LightGizmo
{
    /// <summary>Segments in a ring. Twelve is round enough at gizmo sizes and cheap enough to draw three of.</summary>
    private const int RingSegments = 16;

    /// <summary>
    /// Draws every light in the world.
    /// </summary>
    /// <param name="size">Radius of a light's marker in world units.</param>
    /// <param name="showRange">
    /// Whether a positional light's falloff range is drawn as a ring. It is the one number about a
    /// light that is invisible in the frame and easy to get wrong by an order of magnitude — but on a
    /// light with no range set it is infinite, and nothing is drawn.
    /// </param>
    public static void Draw(
        FrameBuffer surface,
        Matrix4x4 world2Projection,
        IReadOnlyList<ILight> lights,
        float size,
        bool showRange = true)
    {
        ArgumentNullException.ThrowIfNull(surface, nameof(surface));
        ArgumentNullException.ThrowIfNull(lights, nameof(lights));

        foreach (var light in lights)
        {
            switch (light)
            {
                case DirectionalLight directional:
                    DrawDirectional(surface, world2Projection, directional, size);
                    break;

                case SpotLight spot:
                    DrawSpot(surface, world2Projection, spot, size, showRange);
                    break;

                case PointLight point:
                    DrawPoint(surface, world2Projection, point, size, showRange);
                    break;
            }
        }
    }

    /// <summary>
    /// A point light: three rings in the cardinal planes, which read as a sphere from any angle, plus
    /// a ring at its range where it has one.
    /// </summary>
    private static void DrawPoint(
        FrameBuffer surface, Matrix4x4 world2Projection, PointLight light, float size, bool showRange)
    {
        var color = Tint(light);

        Ring(surface, world2Projection, light.Position, Vector3.UnitX, Vector3.UnitY, size, color);
        Ring(surface, world2Projection, light.Position, Vector3.UnitY, Vector3.UnitZ, size, color);
        Ring(surface, world2Projection, light.Position, Vector3.UnitZ, Vector3.UnitX, size, color);

        // Spikes, so a light seen edge-on to all three rings is still something rather than a dot.
        foreach (var axis in new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ })
        {
            GizmoRenderer.DrawLine(surface, world2Projection,
                light.Position - axis * size * 1.6f, light.Position + axis * size * 1.6f, color);
        }

        if (showRange && float.IsFinite(light.Range) && light.Range > size)
        {
            Ring(surface, world2Projection, light.Position, Vector3.UnitX, Vector3.UnitZ, light.Range, Dim(color));
        }
    }

    /// <summary>
    /// A directional light: an arrow through the origin along the direction it travels, drawn as a
    /// shaft with a ring around it.
    ///
    /// It has no position — it is a direction and nothing else — so the marker is placed where the
    /// light <em>comes from</em> relative to the world origin, at a fixed standoff. That is a
    /// fiction, and it is the only one available: drawing it at the origin would bury it in whatever
    /// is being lit.
    /// </summary>
    private static void DrawDirectional(
        FrameBuffer surface, Matrix4x4 world2Projection, DirectionalLight light, float size)
    {
        var travel = light.Direction;

        if (travel.LengthSquared() < 1e-12f)
        {
            return;
        }

        travel = Vector3.Normalize(travel);

        var from = -travel * size * 6f;
        var to = from + travel * size * 4f;

        var color = Tint(light);

        GizmoRenderer.DrawLine(surface, world2Projection, from, to, color);

        var (right, up) = Basis(travel);

        Ring(surface, world2Projection, from, right, up, size, color);

        // An arrowhead at the far end, so which way along the shaft it points is unambiguous.
        foreach (var offset in new[] { right, -right, up, -up })
        {
            GizmoRenderer.DrawLine(surface, world2Projection, to, to - travel * size + offset * size * 0.6f, color);
        }

        // Four parallel rays through the ring, which is how a directional light is drawn everywhere.
        foreach (var offset in new[] { right + up, right - up, -right + up, -right - up })
        {
            var start = from + offset * size * 0.7f;
            GizmoRenderer.DrawLine(surface, world2Projection, start, start + travel * size * 3f, color);
        }
    }

    /// <summary>
    /// A spot: the cone it actually lights, drawn at its outer angle — four edge lines and a ring at
    /// the far end, which is enough to read the aim and the spread from.
    /// </summary>
    private static void DrawSpot(
        FrameBuffer surface, Matrix4x4 world2Projection, SpotLight light, float size, bool showRange)
    {
        var axis = light.Direction;

        if (axis.LengthSquared() < 1e-12f)
        {
            return;
        }

        axis = Vector3.Normalize(axis);

        var color = Tint(light);

        // As far as the light reaches, or a marker's length when it reaches forever.
        var length = showRange && float.IsFinite(light.Range) && light.Range > size
            ? light.Range
            : size * 6f;

        var radius = length * MathF.Tan(System.Math.Clamp(light.OuterAngle, 1e-3f, 1.5f));

        var (right, up) = Basis(axis);
        var end = light.Position + axis * length;

        Ring(surface, world2Projection, end, right, up, radius, color);

        foreach (var offset in new[] { right, -right, up, -up })
        {
            GizmoRenderer.DrawLine(surface, world2Projection, light.Position, end + offset * radius, color);
        }

        // The inner cone, where the light is at full strength, as a second fainter ring.
        var inner = length * MathF.Tan(System.Math.Clamp(light.InnerAngle, 1e-3f, 1.5f));

        if (inner < radius * 0.98f)
        {
            Ring(surface, world2Projection, end, right, up, inner, Dim(color));
        }
    }

    /// <summary>A closed ring around a centre, in the plane the two axes span.</summary>
    private static void Ring(
        FrameBuffer surface,
        Matrix4x4 world2Projection,
        Vector3 centre,
        Vector3 axisA,
        Vector3 axisB,
        float radius,
        ColorRGB color)
    {
        var previous = centre + axisA * radius;

        for (var i = 1; i <= RingSegments; i++)
        {
            var angle = MathF.Tau * i / RingSegments;

            var point = centre + (axisA * MathF.Cos(angle) + axisB * MathF.Sin(angle)) * radius;

            GizmoRenderer.DrawLine(surface, world2Projection, previous, point, color);

            previous = point;
        }
    }

    /// <summary>Two unit vectors perpendicular to a direction and to each other.</summary>
    private static (Vector3 Right, Vector3 Up) Basis(Vector3 direction)
    {
        var reference = MathF.Abs(direction.Y) < 0.999f ? Vector3.UnitY : Vector3.UnitX;

        var right = Vector3.Normalize(Vector3.Cross(reference, direction));
        var up = Vector3.Cross(direction, right);

        return (right, up);
    }

    /// <summary>
    /// The light's own colour, brightened toward white so a dim or saturated light is still legible
    /// as a marker. A gizmo is a label, not a sample of what it labels — but a scene with a red key
    /// and a blue fill is much easier to read when the two markers are not the same colour.
    /// </summary>
    private static ColorRGB Tint(ILight light)
    {
        var color = light.Color;

        return new ColorRGB(
            (byte)(128 + color.R / 2),
            (byte)(128 + color.G / 2),
            (byte)(128 + color.B / 2));
    }

    private static ColorRGB Dim(ColorRGB color) =>
        new((byte)(color.R / 2), (byte)(color.G / 2), (byte)(color.B / 2));
}
