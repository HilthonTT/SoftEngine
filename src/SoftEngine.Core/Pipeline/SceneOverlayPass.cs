using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Gizmos;
using SoftEngine.Core.Scenes;
using System.Numerics;

namespace SoftEngine.Core.Pipeline;

internal static class SceneOverlayPass
{
    private const float GridFrom = -10f;

    private const float GridTo = 10f;

    public static void DrawWorldGizmos(
        Scene scene,
        RendererSettings settings,
        in Matrix4x4 viewProjection,
        GraphicsEventLog events,
        bool recordProbeContext)
    {
        var surface = scene.Surface;

        if (settings.ShowXZGrid)
        {
            var gridLines = ((int)(GridTo - GridFrom) + 1) * 2;

            var gridEvent = events.Add(GraphicsEventKind.GizmoDrawGrid, -1, gridLines, GridFrom, GridTo);

            if (recordProbeContext)
            {
                FrameBuffer.SetProbeContext(gridEvent, PixelWriteSource.Grid, -1, -1, null);
            }

            GizmoRenderer.DrawGrid(surface, viewProjection, GridFrom, GridTo);
        }

        if (settings.ShowAxes)
        {
            var axesEvent = events.Add(GraphicsEventKind.GizmoDrawAxes);

            if (recordProbeContext)
            {
                FrameBuffer.SetProbeContext(axesEvent, PixelWriteSource.Axes, -1, -1, null);
            }

            GizmoRenderer.DrawAxes(surface, viewProjection);
        }

        if (settings.ShowSkeleton && scene.World.Root is { } root)
        {
            var joints = 0;
            foreach (var _ in root.SelfAndDescendants())
            {
                joints++;
            }

            var skeletonEvent = events.Add(GraphicsEventKind.GizmoDrawSkeleton, -1, joints);

            if (recordProbeContext)
            {
                FrameBuffer.SetProbeContext(skeletonEvent, PixelWriteSource.Skeleton, -1, -1, null);
            }

            GizmoRenderer.DrawSkeleton(surface, viewProjection, root, settings.SkeletonTickSize);
        }
    }

    public static void DrawTransformGizmo(
        Scene scene,
        RendererSettings settings,
        in Matrix4x4 viewProjection,
        GraphicsEventLog events,
        bool recordProbeContext)
    {
        if (settings.Gizmo is { IsActive: true } gizmo)
        {
            var surface = scene.Surface;

            var origin = gizmo.Origin;
            var scale = TransformGizmo.HandleScale(scene, origin);

            var gizmoEvent = events.Add(GraphicsEventKind.GizmoDrawTransform, -1, (int)gizmo.Mode, scale);

            if (recordProbeContext)
            {
                FrameBuffer.SetProbeContext(gizmoEvent, PixelWriteSource.TransformGizmo, -1, -1, null);
            }

            GizmoRenderer.DrawTransformGizmo(
                surface,
                viewProjection,
                gizmo.Mode,
                origin,
                scale,
                gizmo.IsDragging ? gizmo.ActiveAxis : gizmo.HoveredAxis);
        }
    }
}
