using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Scenes.Lights;
using System.Numerics;

namespace SoftEngine.Core.Gizmos;

public static class LightGizmo
{
    private const int RingSegments = 16;

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

    private static void DrawPoint(
        FrameBuffer surface, Matrix4x4 world2Projection, PointLight light, float size, bool showRange)
    {
        var color = Tint(light);

        Ring(surface, world2Projection, light.Position, Vector3.UnitX, Vector3.UnitY, size, color);
        Ring(surface, world2Projection, light.Position, Vector3.UnitY, Vector3.UnitZ, size, color);
        Ring(surface, world2Projection, light.Position, Vector3.UnitZ, Vector3.UnitX, size, color);

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

        foreach (var offset in new[] { right, -right, up, -up })
        {
            GizmoRenderer.DrawLine(surface, world2Projection, to, to - travel * size + offset * size * 0.6f, color);
        }

        foreach (var offset in new[] { right + up, right - up, -right + up, -right - up })
        {
            var start = from + offset * size * 0.7f;
            GizmoRenderer.DrawLine(surface, world2Projection, start, start + travel * size * 3f, color);
        }
    }

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

        var inner = length * MathF.Tan(System.Math.Clamp(light.InnerAngle, 1e-3f, 1.5f));

        if (inner < radius * 0.98f)
        {
            Ring(surface, world2Projection, end, right, up, inner, Dim(color));
        }
    }

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

    private static (Vector3 Right, Vector3 Up) Basis(Vector3 direction)
    {
        var reference = MathF.Abs(direction.Y) < 0.999f ? Vector3.UnitY : Vector3.UnitX;

        var right = Vector3.Normalize(Vector3.Cross(reference, direction));
        var up = Vector3.Cross(direction, right);

        return (right, up);
    }

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
