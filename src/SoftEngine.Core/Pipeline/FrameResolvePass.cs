using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Pipeline.Culling;
using SoftEngine.Core.Pipeline.Debugging;
using SoftEngine.Core.Pipeline.PostProcess;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Projections;

namespace SoftEngine.Core.Pipeline;

internal static class FrameResolvePass
{
    public static void Resolve(
        FrameBuffer surface,
        IProjection projection,
        PostProcessStack? postProcess,
        GraphicsEventLog events)
    {
        var stack = postProcess is { HasEffects: true } candidate ? candidate : null;

        if (stack is null && !surface.IsHighDynamicRange)
        {
            return;
        }

        var eventIndex = events.Add(GraphicsEventKind.PostProcessApply, SceneObjectIds.PostProcess,
            stack?.EnabledCount ?? 0, surface.Width, surface.Height);

        var before = surface.IsProbing ? surface.GetProbedColor() : 0;

        if (stack is not null)
        {
            stack.Apply(surface, projection);
        }
        else
        {
            surface.ResolveToScreen();
        }

        if (surface.IsProbing)
        {
            surface.RecordProbeOverwrite(eventIndex, PixelWriteSource.PostProcess, SceneObjectIds.PostProcess, before);
        }
    }

    public static void RenderDebugView(
        ref BufferVisualizer? visualizer,
        FrameBuffer surface,
        Scene scene,
        IProjection projection,
        GraphicsEventLog events,
        DebugView view,
        OcclusionBuffer? occlusion,
        VelocityBuffer? velocity)
    {
        visualizer ??= new BufferVisualizer();

        var before = surface.IsProbing ? surface.GetProbedColor() : 0;

        var drawn = visualizer.Render(surface, projection, scene.ShadowMap, view, occlusion, velocity);

        var eventIndex = events.Add(
            GraphicsEventKind.DebugViewRender, SceneObjectIds.RenderTarget, (int)view, drawn ? 1f : 0f);

        if (drawn && surface.IsProbing)
        {
            surface.RecordProbeOverwrite(eventIndex, PixelWriteSource.DebugView, SceneObjectIds.RenderTarget, before);
        }
    }
}
