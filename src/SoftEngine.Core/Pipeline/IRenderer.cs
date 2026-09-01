using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Pipeline.PostProcess;
using SoftEngine.Core.Rasterization;
using SoftEngine.Core.Scenes;

namespace SoftEngine.Core.Pipeline;

public interface IRenderer
{
    RendererSettings Settings { get; set; }

    PostProcessStack? PostProcess { get; set; }

    RenderStats Stats { get; }

    RenderDiagnostics Diagnostics { get; }

    void Render(Scene scene, IPainter? painter);
}
