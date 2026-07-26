using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
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
