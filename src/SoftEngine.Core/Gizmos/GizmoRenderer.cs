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

    /// <summary>
    /// A gizmo line drawn over the scene rather than into it: clipped in clip space as usual,
    /// but written without a depth test.
    ///
    /// A grid or a skeleton is describing where things are, so hiding behind them is right. A
    /// manipulator is not — you grab its handles with a ray that knows nothing about depth, so
    /// a handle buried inside the mesh it is attached to would be a control you can use and
    /// cannot see.
    /// </summary>
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

    /// <summary>
    /// Draws a node hierarchy as bones: a line from each node to its parent, and a small
    /// three-axis tick at each joint so a node with no children — the end of a finger, the tip
    /// of a tail — is still visible.
    ///
    /// A skeleton is invisible in the rendered image by construction: it is the thing that
    /// moves the vertices, never a thing that gets drawn. Which makes a rig that is subtly
    /// wrong indistinguishable from a mesh that is subtly wrong, unless you can see it.
    /// </summary>
    /// <param name="tickSize">
    /// Length of each joint's tick in world units. Skeletons are authored at wildly different
    /// scales — a 2-unit skull and a 170-unit figure — so this is the caller's to set.
    /// </param>
    public static void DrawSkeleton(FrameBuffer surface, Matrix4x4 world2Projection, SceneNode root, float tickSize = 1f)
    {
        ArgumentNullException.ThrowIfNull(root, nameof(root));

        foreach (var node in root.SelfAndDescendants())
        {
            // The lights and cameras exported alongside the rig sit far outside the model and
            // are not part of it; bones drawn out to them swamp everything else in the view.
            if (node.Kind is SceneNodeKind.Light or SceneNodeKind.Camera)
            {
                continue;
            }

            var origin = node.WorldMatrix.Translation;

            // Not the root itself: it has no bone leading to it, and the synthetic root an
            // importer wraps a scene in sits at the origin rather than anywhere anatomical.
            if (node.Parent is { } parent && !ReferenceEquals(node, root))
            {
                DrawLine(surface, world2Projection, parent.WorldMatrix.Translation, origin, ColorRGB.Yellow);
            }

            DrawLine(surface, world2Projection, origin, origin + new Vector3(tickSize, 0, 0), ColorRGB.Red);
            DrawLine(surface, world2Projection, origin, origin + new Vector3(0, tickSize, 0), ColorRGB.Green);
            DrawLine(surface, world2Projection, origin, origin + new Vector3(0, 0, tickSize), ColorRGB.Blue);
        }
    }

    /// <summary>The colour a grabbed or hovered handle is drawn in — the amber a picked mesh is outlined with.</summary>
    private static readonly ColorRGB GizmoHighlight = new(255, 190, 60);

    /// <summary>
    /// Draws a transform gizmo's handles at <paramref name="origin"/>, sized to
    /// <paramref name="scale"/> world units per handle.
    ///
    /// The shape says what the drag will do before it happens: arrows slide, a ring turns, a
    /// box on a stick stretches. That matters more here than it would in an editor with a
    /// toolbar, because the mode is the only thing distinguishing one gizmo from another and
    /// the handles are all in the same three places.
    /// </summary>
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

    /// <summary>Four barbs back from the tip — enough to read as an arrow from any angle.</summary>
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

    /// <summary>A wireframe box on the end of a scale arm.</summary>
    private static void DrawHandleBox(
        FrameBuffer surface,
        Matrix4x4 world2Projection,
        Vector3 center,
        Vector3 direction,
        float half,
        ColorRGB color)
    {
        var (u, v) = Basis(direction);

        // Two squares — one across the arm at each end of the box — plus the four struts
        // joining them. Cheaper than eight transformed corners and reads the same.
        foreach (var offset in stackalloc[] { -half, half })
        {
            var plane = center + direction * offset;

            DrawLineOnTop(surface, world2Projection, plane + u * half + v * half, plane - u * half + v * half, color);
            DrawLineOnTop(surface, world2Projection, plane - u * half + v * half, plane - u * half - v * half, color);
            DrawLineOnTop(surface, world2Projection, plane - u * half - v * half, plane + u * half - v * half, color);
            DrawLineOnTop(surface, world2Projection, plane + u * half - v * half, plane + u * half + v * half, color);
        }
    }

    /// <summary>
    /// A circle in the plane the axis is normal to, as a closed run of short lines. Thirty-two
    /// segments is where the corners stop being visible at the size these are drawn at.
    /// </summary>
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

    /// <summary>Two unit vectors perpendicular to <paramref name="direction"/> and to each other.</summary>
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
