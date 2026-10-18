using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Pipeline.Clipping;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Scenes.Graph;
using System.Numerics;

namespace SoftEngine.Core.Gizmos;

public static class GizmoRenderer
{
    public static void DrawGrid(FrameBuffer surface, Matrix4x4 world2Projection, float from, float to)
    {
        for (var xz = from; xz <= to; xz++)
        {
            DrawLine(surface, world2Projection, new Vector3(xz, 0, from), new Vector3(xz, 0, to), xz == 0 ? ColorRGB.Red : ColorRGB.Green);
            DrawLine(surface, world2Projection, new Vector3(from, 0, xz), new Vector3(to, 0, xz), ColorRGB.Green);
        }
    }

    public static void DrawLine(FrameBuffer surface, Matrix4x4 world2Projection, Vector3 worldP0, Vector3 worldP1, ColorRGB color)
    {
        Vector4 projectionP0 = Vector4.Transform(worldP0, world2Projection);
        var projectionP1 = Vector4.Transform(worldP1, world2Projection);

        WireFramePainter.DrawLine(surface, color, projectionP0, projectionP1);
    }

    private static void DrawLineOnTop(FrameBuffer surface, Matrix4x4 world2Projection, Vector3 worldP0, Vector3 worldP1, ColorRGB color)
    {
        var p0 = Vector4.Transform(worldP0, world2Projection);
        var p1 = Vector4.Transform(worldP1, world2Projection);

        if (!_liangBarskyClipping.Clip(ref p0, ref p1))
        {
            return;
        }

        surface.DrawLineOnTop(surface.ToScreen3(p0), surface.ToScreen3(p1), color);
    }

    private static readonly LiangBarskyClippingHomogeneous _liangBarskyClipping = new();

    public static void DrawSkeleton(FrameBuffer surface, Matrix4x4 world2Projection, SceneNode root, float tickSize = 1f)
    {
        ArgumentNullException.ThrowIfNull(root, nameof(root));

        foreach (var node in root.SelfAndDescendants())
        {
            if (node.Kind is SceneNodeKind.Light or SceneNodeKind.Camera)
            {
                continue;
            }

            var origin = node.WorldMatrix.Translation;

            if (node.Parent is { } parent && !ReferenceEquals(node, root))
            {
                DrawLine(surface, world2Projection, parent.WorldMatrix.Translation, origin, ColorRGB.Yellow);
            }

            DrawLine(surface, world2Projection, origin, origin + new Vector3(tickSize, 0, 0), ColorRGB.Red);
            DrawLine(surface, world2Projection, origin, origin + new Vector3(0, tickSize, 0), ColorRGB.Green);
            DrawLine(surface, world2Projection, origin, origin + new Vector3(0, 0, tickSize), ColorRGB.Blue);
        }
    }

    private static readonly ColorRGB GizmoHighlight = new(255, 190, 60);

    public static void DrawTransformGizmo(
        FrameBuffer surface,
        Matrix4x4 world2Projection,
        GizmoMode mode,
        Vector3 origin,
        float scale,
        GizmoAxis highlighted)
    {
        if (mode == GizmoMode.Off)
        {
            return;
        }

        for (var i = 0; i < 3; i++)
        {
            var axis = (GizmoAxis)i;
            var direction = TransformGizmo.Direction(axis);

            var color = axis == highlighted
                ? GizmoHighlight
                : axis switch
                {
                    GizmoAxis.X => ColorRGB.Red,
                    GizmoAxis.Y => ColorRGB.Green,
                    _ => ColorRGB.Blue,
                };

            if (mode == GizmoMode.Rotate)
            {
                DrawRing(surface, world2Projection, origin, direction, scale, color);
                continue;
            }

            var tip = origin + direction * scale;
            DrawLineOnTop(surface, world2Projection, origin, tip, color);

            if (mode == GizmoMode.Translate)
            {
                DrawArrowHead(surface, world2Projection, origin, direction, scale, color);
            }
            else
            {
                DrawHandleBox(surface, world2Projection, tip, direction, scale * 0.09f, color);
            }
        }
    }

    private static void DrawArrowHead(
        FrameBuffer surface,
        Matrix4x4 world2Projection,
        Vector3 origin,
        Vector3 direction,
        float scale,
        ColorRGB color)
    {
        var (u, v) = Basis(direction);

        var tip = origin + direction * scale;
        var back = origin + direction * (scale * 0.78f);
        var spread = scale * 0.09f;

        DrawLineOnTop(surface, world2Projection, tip, back + u * spread, color);
        DrawLineOnTop(surface, world2Projection, tip, back - u * spread, color);
        DrawLineOnTop(surface, world2Projection, tip, back + v * spread, color);
        DrawLineOnTop(surface, world2Projection, tip, back - v * spread, color);
    }

    private static void DrawHandleBox(
        FrameBuffer surface,
        Matrix4x4 world2Projection,
        Vector3 center,
        Vector3 direction,
        float half,
        ColorRGB color)
    {
        var (u, v) = Basis(direction);

        foreach (var offset in stackalloc[] { -half, half })
        {
            var plane = center + direction * offset;

            DrawLineOnTop(surface, world2Projection, plane + u * half + v * half, plane - u * half + v * half, color);
            DrawLineOnTop(surface, world2Projection, plane - u * half + v * half, plane - u * half - v * half, color);
            DrawLineOnTop(surface, world2Projection, plane - u * half - v * half, plane + u * half - v * half, color);
            DrawLineOnTop(surface, world2Projection, plane + u * half - v * half, plane + u * half + v * half, color);
        }
    }

    private static void DrawRing(
        FrameBuffer surface,
        Matrix4x4 world2Projection,
        Vector3 origin,
        Vector3 axis,
        float radius,
        ColorRGB color)
    {
        const int segments = 32;

        var (u, v) = Basis(axis);
        var previous = origin + u * radius;

        for (var i = 1; i <= segments; i++)
        {
            var angle = i * MathF.Tau / segments;
            var point = origin + (u * MathF.Cos(angle) + v * MathF.Sin(angle)) * radius;

            DrawLineOnTop(surface, world2Projection, previous, point, color);
            previous = point;
        }
    }

    private static (Vector3 U, Vector3 V) Basis(Vector3 direction)
    {
        var reference = MathF.Abs(direction.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY;

        var u = Vector3.Normalize(Vector3.Cross(direction, reference));

        return (u, Vector3.Cross(direction, u));
    }

    public static void DrawAxes(FrameBuffer surface, Matrix4x4 world2Projection)
    {
        DrawLine(surface, world2Projection, new Vector3(0, 0, 0), new Vector3(1, 0, 0), ColorRGB.Red);
        DrawLine(surface, world2Projection, new Vector3(1, 0, 0), new Vector3(.75f, .25f, 0), ColorRGB.Red);
        DrawLine(surface, world2Projection, new Vector3(1, 0, 0), new Vector3(.75f, -.25f, 0), ColorRGB.Red);
        DrawLine(surface, world2Projection, new Vector3(1, 0, 0), new Vector3(.75f, 0, .25f), ColorRGB.Red);
        DrawLine(surface, world2Projection, new Vector3(1, 0, 0), new Vector3(.75f, 0, -.25f), ColorRGB.Red);

        DrawLine(surface, world2Projection, new Vector3(0, 0, 0), new Vector3(0, 1, 0), ColorRGB.Green);
        DrawLine(surface, world2Projection, new Vector3(0, 1, 0), new Vector3(-.25f, .75f, 0), ColorRGB.Green);
        DrawLine(surface, world2Projection, new Vector3(0, 1, 0), new Vector3(.25f, .75f, 0), ColorRGB.Green);
        DrawLine(surface, world2Projection, new Vector3(0, 1, 0), new Vector3(0, .75f, -.25f), ColorRGB.Green);
        DrawLine(surface, world2Projection, new Vector3(0, 1, 0), new Vector3(0, .75f, .25f), ColorRGB.Green);

        DrawLine(surface, world2Projection, new Vector3(0, 0, 0), new Vector3(0, 0, 1), ColorRGB.Blue);
        DrawLine(surface, world2Projection, new Vector3(0, 0, 1), new Vector3(-.25f, 0, .75f), ColorRGB.Blue);
        DrawLine(surface, world2Projection, new Vector3(0, 0, 1), new Vector3(.25f, 0, .75f), ColorRGB.Blue);
        DrawLine(surface, world2Projection, new Vector3(0, 0, 1), new Vector3(0, -.25f, .75f), ColorRGB.Blue);
        DrawLine(surface, world2Projection, new Vector3(0, 0, 1), new Vector3(0, .25f, .75f), ColorRGB.Blue);
    }
}
