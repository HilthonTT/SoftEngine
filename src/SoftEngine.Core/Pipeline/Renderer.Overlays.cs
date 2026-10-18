using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Gizmos;
using SoftEngine.Core.Rasterization;
using SoftEngine.Core.Scenes;
using System.Numerics;

namespace SoftEngine.Core.Pipeline;

public sealed partial class Renderer
{
    private static readonly ColorRGB HighlightColor = new(255, 190, 60);

    private void DrawOverlays(
        Scene scene,
        FrameBuffer surface,
        WorldBuffer worldBuffer,
        RendererSettings rendererSettings,
        in Matrix4x4 viewProjection,
        GraphicsEventLog events,
        int[]? drawEvents,
        int meshIdBase,
        int transparentCount)
    {
        if (rendererSettings.ShowTriangles)
        {
            var wireFrameEvent = events.Add(GraphicsEventKind.WireFrameOverlayDraw, -1, _visible.Count + transparentCount);

            DrawWireframeOverlay(surface, worldBuffer, _visible, wireFrameEvent, drawEvents, meshIdBase);
            DrawWireframeOverlay(surface, worldBuffer, _transparent, wireFrameEvent, drawEvents, meshIdBase);
        }

        SceneOverlayPass.DrawWorldGizmos(scene, rendererSettings, viewProjection, events, drawEvents is not null);

        foreach (var highlighted in rendererSettings.HighlightedMeshes)
        {
            if (highlighted >= 0)
            {
                DrawHighlight(surface, worldBuffer, events, highlighted, drawEvents, meshIdBase);
            }
        }

        if (rendererSettings.ShowLights && scene.World.Lights.Count > 0)
        {
            events.Add(GraphicsEventKind.GizmoDrawAxes, -1, scene.World.Lights.Count);

            LightGizmo.Draw(
                surface,
                viewProjection,
                scene.World.Lights,
                MathF.Max(rendererSettings.SkeletonTickSize, 1e-4f) * 2f);
        }

        SceneOverlayPass.DrawTransformGizmo(scene, rendererSettings, viewProjection, events, drawEvents is not null);
    }

    private void DrawHighlight(
        FrameBuffer surface,
        WorldBuffer worldBuffer,
        GraphicsEventLog events,
        int meshIndex,
        int[]? drawEvents,
        int meshIdBase)
    {
        var highlightEvent = events.Add(GraphicsEventKind.WireFrameOverlayDraw, meshIdBase + meshIndex);

        DrawHighlightList(surface, worldBuffer, _visible, meshIndex, highlightEvent, drawEvents, meshIdBase);
        DrawHighlightList(surface, worldBuffer, _transparent, meshIndex, highlightEvent, drawEvents, meshIdBase);
    }

    private void DrawHighlightList(
        FrameBuffer surface,
        WorldBuffer worldBuffer,
        List<(int MeshIndex, int TriangleIndex)> list,
        int meshIndex,
        int highlightEvent,
        int[]? drawEvents,
        int meshIdBase)
    {
        foreach (var (mesh, triangleIndex) in list)
        {
            if (mesh != meshIndex)
            {
                continue;
            }

            var vbx = worldBuffer.VertexBuffers[mesh];

            if (drawEvents is not null)
            {
                FrameBuffer.SetProbeContext(
                    highlightEvent,
                    PixelWriteSource.WireFrame,
                    meshIdBase + mesh,
                    vbx.SourceTriangleIndex(triangleIndex),
                    vbx);
            }

            _internalWireFramePainter.DrawTriangle(surface, HighlightColor, vbx, triangleIndex, ScreenTile.Full);
        }
    }

    private void DrawWireframeOverlay(
        FrameBuffer surface,
        WorldBuffer worldBuffer,
        List<(int MeshIndex, int TriangleIndex)> list,
        int wireFrameEvent,
        int[]? drawEvents,
        int meshIdBase)
    {
        foreach (var (meshIndex, triangleIndex) in list)
        {
            var vbx = worldBuffer.VertexBuffers[meshIndex];

            if (drawEvents is not null)
            {
                FrameBuffer.SetProbeContext(
                    wireFrameEvent,
                    PixelWriteSource.WireFrame,
                    meshIdBase + meshIndex, vbx.SourceTriangleIndex(triangleIndex),
                    vbx);
            }

            _internalWireFramePainter.DrawTriangle(surface, ColorRGB.Magenta, vbx, triangleIndex, ScreenTile.Full);
        }
    }
}
